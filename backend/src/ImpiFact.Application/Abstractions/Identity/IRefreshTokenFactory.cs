namespace ImpiFact.Application.Abstractions.Identity;

/// <summary>
/// Crea tokens de renovación (refresh tokens): valores aleatorios opacos, sin datos dentro, que permiten pedir un
/// token de acceso nuevo sin volver a escribir la contraseña. Son rotativos: cada uso entrega uno nuevo y el
/// anterior queda reemplazado (Arquitectura detallada, sección 9.2). Solo se guarda su resumen.
/// </summary>
public interface IRefreshTokenFactory
{
    /// <summary>Crea un token nuevo con la vigencia configurada (14 días por defecto).</summary>
    IssuedRefreshToken Create();

    /// <summary>Resumen con el que se busca en <c>refresh_tokens.token_hash</c> el token que presenta el cliente.</summary>
    string Hash(string token);
}

/// <param name="Value">El token que se entrega al cliente. Nunca se guarda ni se escribe en los registros.</param>
/// <param name="Hash">Resumen SHA-256 en hexadecimal en minúsculas (<c>token_hash</c>, 64 caracteres).</param>
/// <param name="ExpiresAt">Vencimiento (<c>expires_at</c>).</param>
public sealed record IssuedRefreshToken(string Value, string Hash, DateTimeOffset ExpiresAt)
{
    /// <summary>No incluye el token, para que un registro accidental del objeto no lo filtre.</summary>
    public override string ToString() => $"IssuedRefreshToken {{ ExpiresAt = {ExpiresAt:O} }}";
}
