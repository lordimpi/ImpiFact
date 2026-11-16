using Microsoft.Net.Http.Headers;

namespace ImpiFact.Api.Identity;

/// <summary>Lee la credencial del encabezado <c>Authorization: Bearer &lt;token&gt;</c>.</summary>
internal static class BearerToken
{
    private const string Scheme = "Bearer ";

    /// <returns><see langword="true"/> si el encabezado existe una sola vez, usa el esquema Bearer y trae un token.</returns>
    public static bool TryRead(HttpRequest request, out string token)
    {
        token = "";
        var values = request.Headers[HeaderNames.Authorization];
        if (values.Count != 1 || values[0] is not { } header
            || !header.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        token = header[Scheme.Length..].Trim();
        return token.Length > 0;
    }
}
