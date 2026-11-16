using ImpiFact.Infrastructure.Options;

namespace ImpiFact.Infrastructure.Identity;

/// <summary>
/// Distingue una API key de un JWT (JSON Web Token) en el mismo encabezado Bearer (ADR-12): la API key empieza con
/// el prefijo configurado (<c>apk_</c>). Es un singleton sin dependencias de base de datos, porque lo consultan en
/// cada solicitud el selector de esquema y el límite de solicitudes, antes de autenticar.
/// </summary>
public sealed class ApiKeyFormat(IOptionsService<ApiKeyOptions> options)
{
    private readonly string _prefix = options.Value.Prefix;

    public bool IsApiKey(string token) => token.StartsWith(_prefix, StringComparison.Ordinal);
}
