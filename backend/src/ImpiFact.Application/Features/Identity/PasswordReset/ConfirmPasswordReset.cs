using FluentValidation;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Abstractions.Results;
using Microsoft.Extensions.Logging;

namespace ImpiFact.Application.Features.Identity.PasswordReset;

/// <summary>Fija la contraseña nueva con el token del enlace del correo. Es anónima, como la solicitud.</summary>
/// <param name="Token">El token del enlace (<c>?token=</c>).</param>
public sealed record ConfirmPasswordResetCommand(string Token, string NewPassword) : ICommand, ICompanyOptional
{
    /// <summary>No incluye el token ni la contraseña, para que un registro accidental del objeto no los filtre.</summary>
    public override string ToString() => "ConfirmPasswordResetCommand";
}

/// <summary>La contraseña quedó cambiada y todas las sesiones del usuario cerradas.</summary>
public sealed record PasswordResetConfirmed;

internal sealed class ConfirmPasswordResetValidator : AbstractValidator<ConfirmPasswordResetCommand>
{
    public ConfirmPasswordResetValidator()
    {
        RuleFor(command => command.Token)
            .NotEmpty().WithMessage("El token es obligatorio.")
            .MaximumLength(128).WithMessage("El token admite hasta 128 caracteres.")
            .OverridePropertyName("token");
        RuleFor(command => command.NewPassword)
            .NewPassword()
            .OverridePropertyName("new_password");
    }
}

/// <summary>
/// Un token desconocido, vencido, ya usado o revocado (porque se pidió otro), o de un usuario inactivo, responde el
/// mismo error, sin decir cuál de los casos es. La política de contraseñas se revisa antes (validador), así una
/// contraseña rechazada no gasta el token. Con un token válido: lo marca usado, guarda el resumen de la contraseña
/// nueva con un sello de seguridad nuevo y revoca todos los tokens de renovación del usuario, que debe volver a
/// iniciar sesión en todos sus equipos.
/// </summary>
/// <remarks>
/// La revocación de las sesiones se guarda aparte, de inmediato (<see cref="IRefreshTokenStore.RevokeAllActiveForUserAsync"/>):
/// si después la transacción no se confirma, las sesiones quedan cerradas y la contraseña sin cambiar, que es el lado
/// seguro. Los tokens de acceso (JWT) ya entregados siguen valiendo hasta vencer (15 minutos como máximo).
/// </remarks>
internal sealed class ConfirmPasswordResetHandler(
    IPasswordResetTokenStore resetTokens,
    IPasswordResetTokenFactory tokenFactory,
    IUserStore users,
    IPasswordHasher hasher,
    IRefreshTokenStore refreshStore,
    IClock clock,
    ILogger<ConfirmPasswordResetHandler> logger) : IHandler<ConfirmPasswordResetCommand, PasswordResetConfirmed>
{
    public async Task<Result<PasswordResetConfirmed>> HandleAsync(ConfirmPasswordResetCommand request, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var token = await resetTokens.FindByHashAsync(tokenFactory.Hash(request.Token), ct);
        if (token is null || !token.IsUsableAt(now))
        {
            return IdentityErrors.InvalidResetToken;
        }

        var user = await users.FindByIdAsync(token.UserId, ct);
        if (user is not { IsActive: true } || !await resetTokens.MarkUsedAsync(token.Id, now, ct))
        {
            return IdentityErrors.InvalidResetToken;
        }

        await users.ChangePasswordAsync(user.Id, hasher.Hash(request.NewPassword), ct);
        await refreshStore.RevokeAllActiveForUserAsync(user.Id, now, ct);
        logger.LogInformation(
            "Password of user {UserId} was reset with reset token {ResetTokenId}; every refresh token of the user was revoked.", user.Id, token.Id);
        return new PasswordResetConfirmed();
    }
}
