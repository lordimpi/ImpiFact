using System.Text.Json.Serialization;
using FluentValidation;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Abstractions.Protection;
using ImpiFact.Application.Abstractions.Results;
using Microsoft.Extensions.Logging;

namespace ImpiFact.Application.Features.Identity.PasswordReset;

/// <summary>
/// Pide el enlace para restablecer la contraseña (Arquitectura frontend, sección 16, punto 8). Es anónima: quien la pide
/// no puede iniciar sesión.
/// </summary>
/// <param name="ClientIp">IP (Internet Protocol) de origen, que se guarda con el token.</param>
public sealed record RequestPasswordResetCommand(string Email, string? ClientIp) : ICommand, ICompanyOptional
{
    /// <summary>No incluye el correo (dato personal) ni la IP.</summary>
    public override string ToString() => "RequestPasswordResetCommand";
}

/// <summary>Respuesta de la solicitud: siempre la misma, exista o no la cuenta.</summary>
public sealed record PasswordResetRequested([property: JsonPropertyName("message")] string Message)
{
    public static PasswordResetRequested Instance { get; } = new(
        "Si el correo corresponde a una cuenta activa, le enviaremos un enlace para restablecer la contraseña. " +
        "Revise su bandeja de entrada en unos minutos.");
}

internal sealed class RequestPasswordResetValidator : AbstractValidator<RequestPasswordResetCommand>
{
    public RequestPasswordResetValidator() =>
        RuleFor(command => command.Email)
            .NotEmpty().WithMessage("El correo es obligatorio.")
            .MaximumLength(255).WithMessage("El correo admite hasta 255 caracteres.")
            // PostgreSQL no acepta el carácter nulo en un texto: sin esta regla, buscar el correo terminaba en un 500.
            .Must(email => !email.Any(char.IsControl)).WithMessage("El correo no admite caracteres de control.")
            .OverridePropertyName("email");
}

/// <summary>
/// No revela qué correos tienen cuenta: responde lo mismo para un correo desconocido, un usuario inactivo o uno activo,
/// y en los tres casos hace el trabajo de crear y cifrar un token, así el tiempo de respuesta se parece. Solo para un
/// usuario activo deja sin efecto sus tokens sin usar, guarda el resumen del nuevo y encola el correo
/// (<c>SendPasswordResetEmail</c>) con el token cifrado, en la misma transacción.
/// </summary>
/// <remarks>
/// La diferencia que queda (las escrituras en la base) es de milisegundos, y el límite estricto de intentos por IP
/// (el del inicio de sesión) vuelve muy cara una medición estadística. Dejar sin efecto los tokens anteriores es un solo
/// UPDATE en la base, sin token de concurrencia: si varias solicitudes del mismo usuario llegan a la vez, la segunda
/// espera a la primera y no choca con ella, así que todas responden el mismo 202 que un correo desconocido. Pueden
/// quedar varios tokens vigentes (uno por solicitud simultánea); todos van al mismo correo y vencen igual. Si aun así
/// el guardado choca con otra solicitud, la ruta responde el mismo 202 (ver el endpoint de Identity): un 409 solo
/// para los correos registrados revelaría que la cuenta existe.
/// </remarks>
internal sealed class RequestPasswordResetHandler(
    IUserStore users,
    IPasswordResetTokenStore resetTokens,
    IPasswordResetTokenFactory tokenFactory,
    ISecretProtector secrets,
    IPasswordResetEmailQueue emails,
    IClock clock,
    ILogger<RequestPasswordResetHandler> logger) : IHandler<RequestPasswordResetCommand, PasswordResetRequested>
{
    public async Task<Result<PasswordResetRequested>> HandleAsync(RequestPasswordResetCommand request, CancellationToken ct)
    {
        var token = tokenFactory.Create();
        var cipher = secrets.Protect(PasswordResetEmail.SecretPurpose, token.Value);
        var user = await users.FindByEmailAsync(IdentityAccess.NormalizeEmail(request.Email), ct);
        if (user is not { IsActive: true })
        {
            return PasswordResetRequested.Instance;
        }

        var now = clock.UtcNow;
        await resetTokens.RevokeUnusedForUserAsync(user.Id, now, ct);
        var tokenId = Guid.CreateVersion7();
        await resetTokens.AddAsync(
            new PasswordResetTokenRecord(tokenId, user.Id, token.Hash, now + PasswordResetEmail.TokenLifetime, null, null, request.ClientIp), ct);
        await emails.EnqueueAsync(new SendPasswordResetEmailPayload(tokenId, cipher), ct);
        logger.LogInformation("Password reset requested for user {UserId}; reset token {ResetTokenId} was created and its email queued.", user.Id, tokenId);
        return PasswordResetRequested.Instance;
    }
}
