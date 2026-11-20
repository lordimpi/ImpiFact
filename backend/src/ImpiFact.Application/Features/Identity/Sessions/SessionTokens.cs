using System.Text.Json.Serialization;

namespace ImpiFact.Application.Features.Identity.Sessions;

/// <summary>
/// Lo que reciben el inicio de sesión y la renovación: un token de acceso (JWT, JSON Web Token, de vida corta) y un
/// token de renovación nuevo. Son credenciales: solo viajan en esta respuesta y nunca se escriben en los registros.
/// </summary>
/// <param name="CompanyId">Empresa activa del token; nula si no tiene (por ejemplo, un administrador de plataforma).</param>
/// <param name="Roles">Roles que van en el token.</param>
/// <param name="Companies">Empresas a las que pertenece el usuario, para elegir otra con la renovación.</param>
/// <param name="Email">
/// Correo del usuario, tal como está guardado. El panel lo muestra en la barra superior; al recargar la página solo
/// tiene la renovación para saberlo, porque el JWT no lo lleva.
/// </param>
public sealed record SessionTokens(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("expires_at")] DateTimeOffset AccessTokenExpiresAt,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("refresh_token_expires_at")] DateTimeOffset RefreshTokenExpiresAt,
    [property: JsonPropertyName("company_id")] Guid? CompanyId,
    [property: JsonPropertyName("roles")] IReadOnlyList<string> Roles,
    [property: JsonPropertyName("companies")] IReadOnlyList<Guid> Companies,
    [property: JsonPropertyName("email")] string Email)
{
    [JsonPropertyName("token_type")]
    public string TokenType => "Bearer";

    /// <summary>No incluye los tokens, para que un registro accidental del objeto no los filtre.</summary>
    public override string ToString() => $"SessionTokens {{ CompanyId = {CompanyId}, ExpiresAt = {AccessTokenExpiresAt:O} }}";
}
