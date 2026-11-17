using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace ImpiFact.Integration.Tests.DianSend;

/// <summary>
/// DIAN falsa: manejador HTTP final que anota cada solicitud y responde con las respuestas en cola, en orden. Sin
/// respuestas en cola usa <see cref="Route"/>, después <see cref="Otherwise"/> y, si no hay, falla la prueba con una
/// excepción que no es de red.
/// </summary>
public sealed class FakeDian : HttpMessageHandler
{
    private readonly ConcurrentQueue<Func<CancellationToken, Task<HttpResponseMessage>>> replies = new();
    private readonly List<FakeDianRequest> requests = [];

    /// <summary>Si es verdadero, ninguna solicitud recibe respuesta: espera hasta que el cliente la cancele.</summary>
    public bool Hang { get; set; }

    /// <summary>Respuesta según la solicitud cuando la cola está vacía (por ejemplo, por operación); nula para seguir.</summary>
    public Func<FakeDianRequest, HttpResponseMessage?>? Route { get; set; }

    /// <summary>Respuesta cuando la cola está vacía y <see cref="Route"/> no responde; nula para fallar.</summary>
    public Func<HttpResponseMessage>? Otherwise { get; set; }

    /// <summary>Se ejecuta al recibir cada solicitud, antes de responder (por ejemplo, para adelantar el reloj).</summary>
    public Action<FakeDianRequest>? OnRequest { get; set; }

    public IReadOnlyList<FakeDianRequest> Requests
    {
        get
        {
            lock (requests)
            {
                return [.. requests];
            }
        }
    }

    /// <summary>Encola una respuesta SOAP con el código HTTP indicado.</summary>
    public void Reply(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        replies.Enqueue(_ => Task.FromResult(Response(body, status)));

    /// <summary>Encola una falla de red.</summary>
    public void Fail(Func<Exception> failure) => replies.Enqueue(_ => throw failure());

    /// <summary>Encola una solicitud que se recibe y nunca se responde: espera hasta que el cliente la cancele.</summary>
    public void HangOnce() => replies.Enqueue(async token =>
    {
        await Task.Delay(Timeout.Infinite, token);
        throw new InvalidOperationException("Unreachable.");
    });

    public static HttpResponseMessage Response(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/soap+xml") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        var received = new FakeDianRequest(request.RequestUri!, request.Content?.Headers.ContentType?.ToString(), body);
        lock (requests)
        {
            requests.Add(received);
        }

        OnRequest?.Invoke(received);
        if (Hang)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        if (replies.TryDequeue(out var reply))
        {
            return await reply(cancellationToken);
        }

        return Route?.Invoke(received)
            ?? Otherwise?.Invoke()
            ?? throw new InvalidOperationException("The fake DIAN got an unexpected request.");
    }
}

/// <summary>Una solicitud que recibió la DIAN falsa.</summary>
public sealed partial record FakeDianRequest(Uri Url, string? ContentType, string Body)
{
    /// <summary>Operación del cuerpo SOAP, por ejemplo <c>SendBillSync</c>.</summary>
    public string Operation => OperationPattern().Match(Body).Groups[1].Value;

    /// <summary>Hora de creación de la marca de tiempo de WS-Security (<c>wsu:Created</c>).</summary>
    public string Created => CreatedPattern().Match(Body).Groups[1].Value;

    /// <summary>Texto de un parámetro de la operación, por ejemplo <c>fileName</c>.</summary>
    public string? Parameter(string name)
    {
        var match = Regex.Match(Body, $"<wcf:{name}>([^<]*)</wcf:{name}>");
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex("<soap:Body[^>]*><wcf:(\\w+)")]
    private static partial Regex OperationPattern();

    [GeneratedRegex("<wsu:Created>([^<]*)</wsu:Created>")]
    private static partial Regex CreatedPattern();
}
