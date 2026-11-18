namespace ImpiFact.Application.Abstractions.Identity;

/// <summary>
/// Encola en la bandeja de salida el correo con el enlace para restablecer la contraseña (mensaje
/// <c>SendPasswordResetEmail</c>), en la misma transacción que el token.
/// </summary>
/// <remarks>
/// Existe aparte de <c>IOutbox</c> porque la solicitud es anónima: no tiene empresa ni usuario, y la bandeja de salida
/// solo deja escribir sin empresa a la plataforma o al sistema. Este puerto escribe ese único tipo de mensaje, siempre
/// sin empresa, sin abrir esa puerta para nada más.
/// </remarks>
public interface IPasswordResetEmailQueue
{
    /// <summary>Agrega el mensaje; se guarda cuando el decorador de transacción guarda los cambios.</summary>
    Task EnqueueAsync(SendPasswordResetEmailPayload payload, CancellationToken ct);
}

/// <summary>Datos del mensaje <c>SendPasswordResetEmail</c>.</summary>
/// <param name="ResetTokenId">Fila de <c>password_reset_tokens</c>; el Worker lee de ahí el correo y el nombre.</param>
/// <param name="TokenCipher">El token cifrado con el propósito del restablecimiento de contraseña, nunca en claro.</param>
public sealed record SendPasswordResetEmailPayload(Guid ResetTokenId, string TokenCipher)
{
    /// <summary>No incluye el token cifrado.</summary>
    public override string ToString() => $"SendPasswordResetEmailPayload {{ ResetTokenId = {ResetTokenId} }}";
}
