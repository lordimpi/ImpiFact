using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Dian;
using ImpiFact.Dian.Soap;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.CircuitBreaker;
using Polly.Registry;
using Polly.Timeout;

namespace ImpiFact.Infrastructure.Dian;

/// <summary>
/// Cliente del servicio web de la DIAN (la autoridad tributaria de Colombia) sobre <see cref="HttpClient"/>
/// (Arquitectura detallada, sección 6.4): arma el sobre SOAP 1.2 (protocolo de mensajes XML de servicios web) firmado
/// con <see cref="DianSoapEnvelope"/>, lo envía con <c>Content-Type: application/soap+xml; charset=utf-8</c> y lee la
/// respuesta con <see cref="DianResponseReader"/>.
/// </summary>
/// <remarks>
/// <para>
/// Los reintentos, los tiempos de espera y el <i>circuit breaker</i> (el corte que deja de llamar a la DIAN un rato
/// cuando viene fallando) los pone la cadena de resiliencia registrada en <see cref="DianServiceCollectionExtensions"/>,
/// alrededor de cada intento. Cada intento arma y firma su propio sobre con la hora del momento, así un reintento no
/// repite la marca de tiempo (<c>wsu:Timestamp</c>) del intento anterior.
/// </para>
/// <para>
/// Cómo se informa la llamada: si algún intento pudo llegar a la DIAN sin respuesta (tiempo agotado o conexión cortada
/// después de enviar, o un 502/504 de un intermediario que ya pudo reenviarla), la llamada nunca es
/// <see cref="DianCallOutcome.NotSent"/>, aunque el último intento no haya salido (por ejemplo, con el
/// <i>circuit breaker</i> abierto): es <see cref="DianCallOutcome.NoAnswer"/>, o <see cref="DianCallOutcome.Answered"/>
/// con <see cref="DianCall.PossiblyReceived"/> si el último intento sí tuvo respuesta. Sin conexión en todos los
/// intentos (nombre no resuelto, conexión rechazada, TLS fallido) o con el <i>circuit breaker</i> abierto,
/// <see cref="DianCallOutcome.NotSent"/>: la DIAN no recibió nada. Los logs nunca llevan el sobre (trae el documento
/// completo).
/// </para>
/// </remarks>
internal sealed class DianSoapClient(IHttpClientFactory httpClients, ResiliencePipelineProvider<string> pipelines, IClock clock)
    : IDianWebService
{
    /// <summary>Nombre del <see cref="HttpClient"/> de la DIAN; las pruebas lo usan para cambiar el manejador final.</summary>
    internal const string HttpClientName = "Dian";

    private static readonly UTF8Encoding Utf8 = new(false);

    public async Task<DianCall> CallAsync(DianSoapRequest request, string endpointUrl, X509Certificate2 certificate, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var trail = new AttemptTrail();
        var started = Stopwatch.GetTimestamp();
        try
        {
            // El HttpClient y la cadena se piden en cada llamada, no al crear el cliente: así crear un handler del
            // Worker (que lo hace el procesador solo para leer su tipo de mensaje) no los arma.
            var http = httpClients.CreateClient(HttpClientName);
            var pipeline = pipelines.GetPipeline<HttpResponseMessage>(DianServiceCollectionExtensions.PipelineName);
            using var response = await pipeline.ExecuteAsync(
                attempt => SendAttemptAsync(http, request, endpointUrl, certificate, trail, ct, attempt), ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            return new DianCall(
                DianCallOutcome.Answered, trail.Envelope ?? string.Empty, (int)response.StatusCode, body, DianResponseReader.Read(body),
                Elapsed(started), Failure: null, trail.PossiblyReceived);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (BrokenCircuitException exception)
        {
            return Failed(trail, started, $"The DIAN circuit breaker is open: {exception.Message}");
        }
        catch (HttpRequestException exception) when (IsBeforeSending(exception))
        {
            return Failed(trail, started, $"Could not connect to the DIAN ({exception.HttpRequestError}): {exception.Message}");
        }
        catch (HttpRequestException exception)
        {
            return Failed(trail, started, $"The DIAN connection failed ({exception.HttpRequestError}): {exception.Message}");
        }
        catch (TimeoutRejectedException exception)
        {
            return Failed(trail, started, $"The DIAN did not answer in time: {exception.Message}");
        }
    }

    /// <summary>
    /// Errores transitorios que se reintentan y cuentan para el <i>circuit breaker</i>: los de
    /// <see cref="HttpClientResiliencePredicates.IsTransient(Outcome{HttpResponseMessage})"/> (red, tiempo agotado,
    /// HTTP 5xx, 408 y 429) menos el SOAP Fault, que es una respuesta de negocio de la DIAN aunque llegue con HTTP 500.
    /// </summary>
    internal static async ValueTask<bool> IsTransientAsync(Outcome<HttpResponseMessage> outcome, CancellationToken ct)
    {
        if (!HttpClientResiliencePredicates.IsTransient(outcome))
        {
            return false;
        }

        if (outcome.Result is not { } response)
        {
            return true;
        }

        // Leer el cuerpo lo deja en memoria: quien recibe la respuesta lo vuelve a leer sin problema.
        var body = await response.Content.ReadAsStringAsync(ct);
        return DianResponseReader.Read(body) is not DianSoapFaultResponse;
    }

    /// <summary>
    /// Un intento: arma y firma un sobre nuevo, lo envía y anota en <paramref name="trail"/> si pudo llegar a la DIAN
    /// sin que llegara su respuesta.
    /// </summary>
    /// <param name="caller">Cancelación de quien llama; si es ella la que corta, no se anota nada.</param>
    /// <param name="attempt">Cancelación del intento, que también corta cuando vence el plazo del intento o el total.</param>
    private async ValueTask<HttpResponseMessage> SendAttemptAsync(
        HttpClient http, DianSoapRequest request, string endpointUrl, X509Certificate2 certificate, AttemptTrail trail,
        CancellationToken caller, CancellationToken attempt)
    {
        var envelope = DianSoapEnvelope.Build(request, endpointUrl, certificate, clock.UtcNow);
        trail.Envelope = envelope;
        using var message = new HttpRequestMessage(HttpMethod.Post, endpointUrl)
        {
            Content = new StringContent(envelope, Utf8, DianSoapEnvelope.ContentType),
        };
        try
        {
            // HttpClient lee el cuerpo completo antes de devolver: liberar la solicitud no afecta la respuesta.
            var response = await http.SendAsync(message, attempt);
            if (response.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout)
            {
                // Un intermediario (proxy o balanceador) pudo haber reenviado el sobre a la DIAN antes de fallar.
                trail.PossiblyReceived = true;
            }

            return response;
        }
        catch (HttpRequestException exception) when (!IsBeforeSending(exception))
        {
            trail.PossiblyReceived = true;
            throw;
        }
        catch (OperationCanceledException) when (!caller.IsCancellationRequested)
        {
            // Se agotó el plazo del intento o el total mientras se enviaba o se esperaba la respuesta.
            trail.PossiblyReceived = true;
            throw;
        }
    }

    private static bool IsBeforeSending(HttpRequestException exception) => exception.HttpRequestError is
        HttpRequestError.NameResolutionError or HttpRequestError.ConnectionError or HttpRequestError.SecureConnectionError
        or HttpRequestError.ProxyTunnelError;

    private static DianCall Failed(AttemptTrail trail, long started, string failure) =>
        new(trail.PossiblyReceived ? DianCallOutcome.NoAnswer : DianCallOutcome.NotSent, trail.Envelope ?? string.Empty,
            HttpStatus: null, Body: null, Response: null, Elapsed(started), failure, trail.PossiblyReceived);

    private static TimeSpan Elapsed(long started) => Stopwatch.GetElapsedTime(started);

    /// <summary>Lo que dejaron los intentos de una llamada: el último sobre y si alguno pudo llegar a la DIAN.</summary>
    private sealed class AttemptTrail
    {
        public string? Envelope { get; set; }

        public bool PossiblyReceived { get; set; }
    }
}
