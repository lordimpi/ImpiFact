using FluentValidation;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Abstractions.Results;

namespace ImpiFact.Application.Features.Identity.Sessions;

/// <summary>
/// Inicio de sesión del panel con correo y contraseña (Arquitectura detallada, secciones 3 y 9.2). No exige
/// empresa: el usuario todavía no eligió ninguna.
/// </summary>
/// <param name="CompanyId">Empresa con la que quiere trabajar; opcional.</param>
/// <param name="ClientIp">IP (Internet Protocol) de origen, que se guarda con el token de renovación.</param>
/// <param name="UserAgent">Navegador o cliente que hace la solicitud, que se guarda con el token de renovación.</param>
public sealed record LoginCommand(string Email, string Password, Guid? CompanyId, string? ClientIp, string? UserAgent)
    : ICommand, ICompanyOptional
{
    /// <summary>No incluye la contraseña, para que un registro accidental del objeto no la filtre.</summary>
    public override string ToString() => $"LoginCommand {{ CompanyId = {CompanyId} }}";
}

internal sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(command => command.Email)
            .NotEmpty().WithMessage("El correo es obligatorio.")
            .MaximumLength(255).WithMessage("El correo admite hasta 255 caracteres.")
            .OverridePropertyName("email");
        RuleFor(command => command.Password)
            .NotEmpty().WithMessage("La contraseña es obligatoria.")
            .MaximumLength(256).WithMessage("La contraseña admite hasta 256 caracteres.")
            .OverridePropertyName("password");
    }
}

/// <summary>
/// Verifica la contraseña (también cuando el correo no existe, contra un resumen de relleno, para que la respuesta
/// tarde lo mismo) y responde el mismo error ante un correo desconocido, una contraseña equivocada o un usuario
/// inactivo o bloqueado. Si el resumen usa parámetros viejos, lo guarda de nuevo con los actuales.
/// </summary>
internal sealed class LoginHandler(
    IUserStore users,
    IPasswordHasher hasher,
    IClock clock,
    IAccessTokenIssuer accessTokens,
    IRefreshTokenFactory refreshTokens,
    IRefreshTokenStore refreshStore) : IHandler<LoginCommand, SessionTokens>
{
    private readonly SessionIssuer _sessions = new(users, accessTokens, refreshTokens, refreshStore);

    public async Task<Result<SessionTokens>> HandleAsync(LoginCommand request, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(IdentityAccess.NormalizeEmail(request.Email), ct);
        var check = hasher.Verify(user?.PasswordHash, request.Password);
        var now = clock.UtcNow;
        if (user is null || check == PasswordCheck.Failed || !user.IsActive || user.LockoutEnd > now)
        {
            return IdentityErrors.InvalidCredentials;
        }

        if (check == PasswordCheck.SuccessRehashNeeded)
        {
            await users.UpdatePasswordHashAsync(user.Id, hasher.Hash(request.Password), ct);
        }

        return await _sessions.IssueAsync(user, request.CompanyId, request.ClientIp, request.UserAgent, replaces: null, now, ct);
    }
}
