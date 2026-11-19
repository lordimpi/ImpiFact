namespace ImpiFact.Application.Abstractions.Identity;

/// <summary>
/// Crea los tokens para restablecer la contraseña: 32 bytes aleatorios (256 bits) del generador criptográfico del
/// sistema, sin datos dentro, y su resumen SHA-256, que es lo único que se guarda.
/// </summary>
public interface IPasswordResetTokenFactory
{
    IssuedPasswordResetToken Create();

    /// <summary>Resumen con el que se busca en <c>password_reset_tokens.token_hash</c> el token que presenta el usuario.</summary>
    string Hash(string token);
}

/// <param name="Value">El token que va en el enlace del correo. Nunca se guarda en claro ni se escribe en los registros.</param>
/// <param name="Hash">Resumen SHA-256 en hexadecimal en minúsculas (64 caracteres).</param>
public sealed record IssuedPasswordResetToken(string Value, string Hash)
{
    /// <summary>No incluye el token, para que un registro accidental del objeto no lo filtre.</summary>
    public override string ToString() => "IssuedPasswordResetToken";
}
