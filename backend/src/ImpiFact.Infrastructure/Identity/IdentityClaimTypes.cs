namespace ImpiFact.Infrastructure.Identity;

/// <summary>
/// Nombres de los claims (datos de identidad dentro de la credencial) que emite y lee la API. Se usan tal cual,
/// sin la conversión de .NET a los nombres largos de Microsoft, para que coincidan con los del token.
/// </summary>
public static class IdentityClaimTypes
{
    /// <summary>Usuario del panel (claim estándar <c>sub</c>, «subject»).</summary>
    public const string Subject = "sub";

    /// <summary>Rol del usuario; puede repetirse.</summary>
    public const string Role = "role";

    /// <summary>Empresa activa del usuario o empresa dueña de la API key.</summary>
    public const string CompanyId = "company_id";

    /// <summary>Alcance de una API key; hay un claim por cada alcance.</summary>
    public const string Scope = "scope";

    /// <summary>Fila de <c>api_keys</c> con la que se autenticó la solicitud.</summary>
    public const string ApiKeyId = "api_key_id";
}
