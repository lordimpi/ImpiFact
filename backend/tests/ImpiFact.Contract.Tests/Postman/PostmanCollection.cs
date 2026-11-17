using System.Text;
using System.Text.Json;

namespace ImpiFact.Contract.Tests.Postman;

/// <summary>
/// La colección Postman (formato v2 de Postman: carpetas anidadas con solicitudes y sus respuestas de ejemplo) leída
/// de la copia limpia en <c>collection/</c>, aplanada en el orden en que aparece.
/// </summary>
public sealed class PostmanCollection
{
    public const string SnapshotFileName = "apidian-v2.1.postman_collection.json";

    private PostmanCollection(string name, IReadOnlyList<string> topFolders, IReadOnlyList<PostmanRequest> requests)
    {
        Name = name;
        TopFolders = topFolders;
        Requests = requests;
    }

    public string Name { get; }

    /// <summary>Carpetas del primer nivel, por ejemplo «01 - Configuraciones Basicas».</summary>
    public IReadOnlyList<string> TopFolders { get; }

    public IReadOnlyList<PostmanRequest> Requests { get; }

    /// <summary>La copia guardada junto a las pruebas (se copia a la carpeta de salida al compilar).</summary>
    public static PostmanCollection LoadSnapshot() =>
        Load(Path.Combine(AppContext.BaseDirectory, "collection", SnapshotFileName));

    public static PostmanCollection Load(string path)
    {
        using var json = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = json.RootElement;
        var requests = new List<PostmanRequest>();
        var topFolders = new List<string>();
        foreach (var folder in root.GetProperty("item").EnumerateArray())
        {
            topFolders.Add(folder.GetProperty("name").GetString()!);
            Walk(folder, [], requests);
        }

        return new PostmanCollection(root.GetProperty("info").GetProperty("name").GetString()!, topFolders, requests);
    }

    private static void Walk(JsonElement node, IReadOnlyList<string> parents, List<PostmanRequest> requests)
    {
        var name = node.GetProperty("name").GetString()!;
        if (node.TryGetProperty("item", out var children))
        {
            string[] path = [.. parents, name];
            foreach (var child in children.EnumerateArray())
            {
                Walk(child, path, requests);
            }

            return;
        }

        var request = node.GetProperty("request");
        var url = request.GetProperty("url");
        var headers = request.TryGetProperty("header", out var headerList)
            ? headerList.EnumerateArray()
                .Select(header => new KeyValuePair<string, string>(header.GetProperty("key").GetString()!, header.TryGetProperty("value", out var value) ? value.GetString() ?? "" : ""))
                .ToList()
            : [];
        string? body = null;
        if (request.TryGetProperty("body", out var bodyNode) && bodyNode.ValueKind == JsonValueKind.Object
            && bodyNode.TryGetProperty("raw", out var raw) && !string.IsNullOrWhiteSpace(raw.GetString()))
        {
            body = raw.GetString();
        }

        var examples = node.TryGetProperty("response", out var responses)
            ? responses.EnumerateArray().Select(response => new PostmanExample(
                    response.TryGetProperty("name", out var exampleName) ? exampleName.GetString() ?? "" : "",
                    response.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Number ? code.GetInt32() : 0,
                    response.TryGetProperty("body", out var exampleBody) ? exampleBody.GetString() : null))
                .ToList()
            : [];

        requests.Add(new PostmanRequest(
            requests.Count,
            parents,
            name,
            request.GetProperty("method").GetString()!,
            url.ValueKind == JsonValueKind.String ? url.GetString()! : url.GetProperty("raw").GetString()!,
            headers,
            body,
            examples));
    }
}

/// <param name="Index">Posición en la colección, desde 0.</param>
/// <param name="Folders">Carpetas que la contienen, desde la del primer nivel.</param>
/// <param name="RawUrl">URL tal como está en la colección, con el servidor de quien la grabó.</param>
/// <param name="Body">Cuerpo crudo (puede traer comentarios <c>//</c>, que Postman quita al enviar).</param>
public sealed record PostmanRequest(
    int Index,
    IReadOnlyList<string> Folders,
    string Name,
    string Method,
    string RawUrl,
    IReadOnlyList<KeyValuePair<string, string>> Headers,
    string? Body,
    IReadOnlyList<PostmanExample> Examples)
{
    /// <summary>Número de la carpeta del primer nivel, por ejemplo «01».</summary>
    public string TopFolder => Folders[0][..2];

    public string Title => $"{string.Join(" / ", Folders)} / {Name}";

    /// <summary>
    /// Ruta desde <c>/api/</c>, sin servidor ni consulta, por ejemplo <c>/api/ubl2.1/invoice</c>. Quita el
    /// <c>/public</c> de las URL grabadas contra un Laravel servido desde su carpeta <c>public/</c>: es parte de cómo se
    /// instaló aquel servidor, no de la ruta.
    /// </summary>
    public string ApiPath
    {
        get
        {
            var start = RawUrl.IndexOf("/api/", StringComparison.Ordinal);
            var path = start < 0 ? RawUrl : RawUrl[start..];
            var query = path.IndexOf('?', StringComparison.Ordinal);
            return query < 0 ? path : path[..query];
        }
    }

    public bool HasHeader(string name) => Headers.Any(header => string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>El cuerpo sin los comentarios <c>//</c> y <c>/* */</c>, como lo envía Postman.</summary>
    public string? JsonBody => Body is null ? null : JsonComments.Strip(Body);
}

/// <summary>Una respuesta de ejemplo guardada en la colección.</summary>
public sealed record PostmanExample(string Name, int Code, string? Body)
{
    /// <summary>Claves del primer nivel del cuerpo si es un objeto JSON; nulo si no lo es.</summary>
    public IReadOnlyList<string>? TopLevelKeys
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Body))
            {
                return null;
            }

            try
            {
                using var json = JsonDocument.Parse(Body);
                return json.RootElement.ValueKind == JsonValueKind.Object
                    ? [.. json.RootElement.EnumerateObject().Select(property => property.Name)]
                    : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}

/// <summary>
/// Quita los comentarios de un cuerpo JSON fuera de las cadenas, como hace Postman antes de enviar un cuerpo «raw»
/// de tipo JSON. Sin esto ningún servidor (ni el PHP) aceptaría los cuerpos comentados de la colección.
/// </summary>
public static class JsonComments
{
    public static string Strip(string text)
    {
        var output = new StringBuilder(text.Length);
        var inString = false;
        for (var i = 0; i < text.Length; i++)
        {
            var current = text[i];
            if (inString)
            {
                output.Append(current);
                if (current == '\\' && i + 1 < text.Length)
                {
                    output.Append(text[++i]);
                }
                else if (current == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (current == '"')
            {
                inString = true;
                output.Append(current);
            }
            else if (current == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i + 1 < text.Length && text[i + 1] is not '\n' and not '\r')
                {
                    i++;
                }
            }
            else if (current == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? text.Length : end + 1;
            }
            else
            {
                output.Append(current);
            }
        }

        return output.ToString();
    }
}
