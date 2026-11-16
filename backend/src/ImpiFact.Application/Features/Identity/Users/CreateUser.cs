using System.Text.Json.Serialization;
using FluentValidation;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Domain.Identity;

namespace ImpiFact.Application.Features.Identity.Users;

/// <summary>
/// Crea un usuario del panel. El <c>owner</c> de una empresa crea usuarios de su empresa con rol <c>owner</c> o
/// <c>seller</c>; la plataforma crea administradores de plataforma (<c>platform_admin</c>, sin empresa).
/// </summary>
public sealed record CreateUserCommand(string Email, string Password, string Name, string? Phone, string Role)
    : ICommand, ICompanyOptional
{
    /// <summary>No incluye la contraseña, para que un registro accidental del objeto no la filtre.</summary>
    public override string ToString() => $"CreateUserCommand {{ Role = {Role} }}";
}

public sealed record CreatedUser(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("company_id")] Guid? CompanyId);

internal sealed class CreateUserValidator : AbstractValidator<CreateUserCommand>
{
    /// <summary>Largo mínimo de una contraseña nueva.</summary>
    public const int MinimumPasswordLength = 10;

    private static readonly string[] KnownRoles =
        [RoleNames.PlatformAdmin, RoleNames.Reseller, RoleNames.Owner, RoleNames.Seller, RoleNames.Customer];

    public CreateUserValidator()
    {
        RuleFor(command => command.Email)
            .NotEmpty().WithMessage("El correo es obligatorio.")
            .MaximumLength(255).WithMessage("El correo admite hasta 255 caracteres.")
            .EmailAddress().WithMessage("El correo no es válido.")
            .OverridePropertyName("email");
        RuleFor(command => command.Password)
            .NotEmpty().WithMessage("La contraseña es obligatoria.")
            .MinimumLength(MinimumPasswordLength).WithMessage($"La contraseña debe tener al menos {MinimumPasswordLength} caracteres.")
            .MaximumLength(256).WithMessage("La contraseña admite hasta 256 caracteres.")
            .OverridePropertyName("password");
        RuleFor(command => command.Name)
            .NotEmpty().WithMessage("El nombre es obligatorio.")
            .MaximumLength(255).WithMessage("El nombre admite hasta 255 caracteres.")
            .OverridePropertyName("name");
        RuleFor(command => command.Phone)
            .MaximumLength(30).WithMessage("El teléfono admite hasta 30 caracteres.")
            .OverridePropertyName("phone");
        RuleFor(command => command.Role)
            .Must(role => KnownRoles.Contains(role)).WithMessage("El rol «{PropertyValue}» no existe.")
            .OverridePropertyName("role");
    }
}

/// <summary>
/// Confirma en la base que quien crea el usuario sea <c>owner</c> de la empresa activa o actúe por la plataforma; las
/// API keys de empresa no crean usuarios. El correo se guarda normalizado y no puede repetirse.
/// </summary>
internal sealed class CreateUserHandler(ITenantContext tenant, IUserStore users, IPasswordHasher hasher)
    : IHandler<CreateUserCommand, CreatedUser>
{
    private static readonly string[] CompanyRoles = [RoleNames.Owner, RoleNames.Seller];

    public async Task<Result<CreatedUser>> HandleAsync(CreateUserCommand request, CancellationToken ct)
    {
        IReadOnlyList<string> platformRoles;
        CompanyMembership? membership;
        if (tenant.CompanyId is { } companyId)
        {
            if (!await IdentityAccess.IsCompanyOwnerAsync(tenant, users, ct))
            {
                return IdentityErrors.Forbidden;
            }

            if (!CompanyRoles.Contains(request.Role))
            {
                return IdentityErrors.RoleNotAllowed;
            }

            platformRoles = [];
            membership = new CompanyMembership(companyId, request.Role);
        }
        else
        {
            if (!await IdentityAccess.IsPlatformAsync(tenant, users, ct))
            {
                return IdentityErrors.Forbidden;
            }

            if (request.Role != RoleNames.PlatformAdmin)
            {
                return IdentityErrors.RoleNotAllowed;
            }

            platformRoles = [RoleNames.PlatformAdmin];
            membership = null;
        }

        var email = IdentityAccess.NormalizeEmail(request.Email);
        if (await users.EmailExistsAsync(email, ct))
        {
            return IdentityErrors.EmailTaken;
        }

        var id = Guid.CreateVersion7();
        var name = request.Name.Trim();
        await users.AddAsync(new NewUser(id, email, hasher.Hash(request.Password), name, request.Phone, platformRoles, membership), ct);
        return new CreatedUser(id, email, name, request.Role, membership?.CompanyId);
    }
}
