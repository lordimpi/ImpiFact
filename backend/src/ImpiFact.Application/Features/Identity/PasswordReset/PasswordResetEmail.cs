namespace ImpiFact.Application.Features.Identity.PasswordReset;

/// <summary>Reglas comunes del restablecimiento de contraseña: vigencia del enlace y cifrado del token en la bandeja de salida.</summary>
public static class PasswordResetEmail
{
    /// <summary>Vigencia del enlace desde que se pide: 30 minutos. El correo lo dice con <see cref="LifetimeText"/>.</summary>
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(30);

    /// <summary>La vigencia como la lee el usuario en el correo.</summary>
    public const string LifetimeText = "30 minutos";

    /// <summary>
    /// Propósito de cifrado (<c>ISecretProtector</c>) del token dentro del mensaje de la bandeja de salida: la base solo
    /// guarda el resumen del token, y el mensaje que el Worker convierte en correo lo lleva cifrado, nunca en claro. No
    /// se cambia una vez en uso.
    /// </summary>
    public const string SecretPurpose = "password-reset.token";
}
