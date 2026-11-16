namespace ImpiFact.Application.Abstractions.Identity;

/// <summary>
/// Resume y verifica contraseñas. El resumen es irreversible y lento a propósito (PBKDF2: una función que repite el
/// cálculo muchas veces para que probar contraseñas al azar sea caro), con sal aleatoria por contraseña.
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>
    /// Compara la contraseña con su resumen. Si <paramref name="passwordHash"/> es nulo (el usuario no existe),
    /// hace el mismo trabajo contra un resumen de relleno y devuelve <see cref="PasswordCheck.Failed"/>: así la
    /// respuesta tarda lo mismo y no revela si el correo existe.
    /// </summary>
    PasswordCheck Verify(string? passwordHash, string password);
}

public enum PasswordCheck
{
    Failed,
    Success,

    /// <summary>La contraseña es correcta, pero su resumen usa parámetros viejos y conviene guardarlo de nuevo.</summary>
    SuccessRehashNeeded,
}
