namespace ImpiFact.Application.Abstractions.Identity;

/// <summary>
/// Emite los tokens de acceso del panel: JWT (JSON Web Token, una credencial firmada que el navegador envía en
/// cada solicitud) con vida corta (15 minutos por configuración). Los casos de uso de inicio de sesión, renovación
/// y cambio de empresa lo llaman; cambiar de empresa emite un token nuevo (Arquitectura detallada, sección 9.2).
/// </summary>
public interface IAccessTokenIssuer
{
    /// <param name="userId">Usuario; va en el claim <c>sub</c>.</param>
    /// <param name="roles">Roles del usuario en la empresa activa (o de plataforma); van en el claim <c>role</c>.</param>
    /// <param name="companyId">Empresa activa; va en el claim <c>company_id</c> y se omite si es nula.</param>
    AccessToken Issue(Guid userId, IReadOnlyCollection<string> roles, Guid? companyId);
}

/// <param name="Value">El token firmado. Es una credencial: nunca se guarda ni se escribe en los registros.</param>
/// <param name="ExpiresAt">Cuándo deja de servir.</param>
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt)
{
    /// <summary>No incluye el token, para que un registro accidental del objeto no lo filtre.</summary>
    public override string ToString() => $"AccessToken {{ ExpiresAt = {ExpiresAt:O} }}";
}
