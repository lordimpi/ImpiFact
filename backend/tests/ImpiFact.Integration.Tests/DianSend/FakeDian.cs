using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace ImpiFact.Integration.Tests.DianSend;

/// <summary>
/// DIAN falsa: manejador HTTP final que anota cada solicitud y responde con las respuestas en cola, en orden. Sin
/// respuestas en cola usa <see cref="Otherwise"/> y, si no hay, falla la prueba con una excepción que no es de red.
/// </summary>
public sealed class FakeDian : HttpMessageHandler
{
    private readonly ConcurrentQueue<Func<HttpResponseMessage>> replies = new();
    private readonly List<FakeDianRequest> requests = [];

    /// <summary>Si es verdadero, ninguna solicitud recibe respuesta: espera hasta que el cliente la cancele.</summary>
    public bool Hang { get; set; }

    /// <summary>Respuesta cuando la cola está vacía; nula para fallar.</summary>
    public Func<HttpResponseMessage>? Otherwise { get; set; }

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
    public void Reply(string body, HttpStatusCode status = HttpStatusCode.OK) => replies.Enqueue(() => Response(body, status));

    /// <summary>Encola una falla de red.</summary>
    public void Fail(Func<Exception> failure) => replies.Enqueue(() => throw failure());

    public static HttpResponseMessage Response(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/soap+xml") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (requests)
        {
            requests.Add(new FakeDianRequest(request.RequestUri!, request.Content?.Headers.ContentType?.ToString(), body));
        }

        if (Hang)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        if (replies.TryDequeue(out var reply))
        {
            return reply();
        }

        return Otherwise?.Invoke() ?? throw new InvalidOperationException("The fake DIAN got an unexpected request.");
    }
}

/// <summary>Una solicitud que recibió la DIAN falsa.</summary>
public sealed partial record FakeDianRequest(Uri Url, string? ContentType, string Body)
{
    /// <summary>Operación del cuerpo SOAP, por ejemplo <c>SendBillSync</c>.</summary>
    public string Operation => OperationPattern().Match(Body).Groups[1].Value;

    /// <summary>Texto de un parámetro de la operación, por ejemplo <c>fileName</c>.</summary>
    public string? Parameter(string name)
    {
        var match = Regex.Match(Body, $"<wcf:{name}>([^<]*)</wcf:{name}>");
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex("<soap:Body[^>]*><wcf:(\\w+)")]
    private static partial Regex OperationPattern();
}
