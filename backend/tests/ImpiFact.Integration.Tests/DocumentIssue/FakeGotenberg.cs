using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace ImpiFact.Integration.Tests.DocumentIssue;

/// <summary>
/// Gotenberg falso (el conversor de HTML a PDF) como manejador final del cliente de Gotenberg de la API: anota los
/// archivos de cada conversión y responde un PDF mínimo. Con <see cref="Down"/> falla como un servicio caído.
/// </summary>
public sealed class FakeGotenberg : HttpMessageHandler
{
    /// <summary>Lo que responde una conversión correcta: empieza como un PDF.</summary>
    public static readonly byte[] Pdf = "%PDF-1.7\n% ImpiFact fake preview\n%%EOF\n"u8.ToArray();

    private readonly ConcurrentQueue<GotenbergConversion> conversions = new();

    /// <summary>Si es verdadero, cada conversión falla como si no hubiera nadie escuchando.</summary>
    public bool Down { get; set; }

    public IReadOnlyCollection<GotenbergConversion> Conversions => conversions;

    /// <summary>Las conversiones cuyo <c>index.html</c> contiene <paramref name="text"/> (por ejemplo, el NIT de la empresa).</summary>
    public List<GotenbergConversion> ConversionsWith(string text) =>
        conversions.Where(conversion => conversion.Html.Contains(text, StringComparison.Ordinal)).ToList();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Down)
        {
            throw new HttpRequestException("No connection could be made because the target machine actively refused it (fake).");
        }

        var files = new Dictionary<string, (byte[] Content, string? ContentType)>(StringComparer.Ordinal);
        if (request.Content is MultipartFormDataContent form)
        {
            foreach (var part in form)
            {
                if (part.Headers.ContentDisposition?.FileName is { } name)
                {
                    files[name.Trim('"')] = (await part.ReadAsByteArrayAsync(cancellationToken), part.Headers.ContentType?.MediaType);
                }
            }
        }

        conversions.Enqueue(new GotenbergConversion(request.RequestUri!.AbsolutePath, files));
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Pdf) };
    }
}

/// <summary>Una conversión recibida: la ruta y los archivos por nombre.</summary>
public sealed record GotenbergConversion(string Path, IReadOnlyDictionary<string, (byte[] Content, string? ContentType)> Files)
{
    public string Html => Files.TryGetValue("index.html", out var html) ? Encoding.UTF8.GetString(html.Content) : "";
}
