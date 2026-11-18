using FluentValidation;

namespace ImpiFact.Application.Features.Identity;

/// <summary>
/// Política de contraseñas nuevas, la misma al crear un usuario y al restablecer la contraseña: obligatoria, de 10 a
/// 256 caracteres. Las contraseñas que ya existen no se revisan al iniciar sesión.
/// </summary>
internal static class PasswordRules
{
    /// <summary>Largo mínimo de una contraseña nueva.</summary>
    public const int MinimumLength = 10;

    /// <summary>Largo máximo de una contraseña, también al iniciar sesión.</summary>
    public const int MaximumLength = 256;

    public static IRuleBuilderOptions<T, string> NewPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule
            .NotEmpty().WithMessage("La contraseña es obligatoria.")
            .MinimumLength(MinimumLength).WithMessage($"La contraseña debe tener al menos {MinimumLength} caracteres.")
            .MaximumLength(MaximumLength).WithMessage($"La contraseña admite hasta {MaximumLength} caracteres.");
}
