using ImpiFact.Application.Abstractions.Results;

namespace ImpiFact.Application.Features.Identity;

/// <summary>Errores esperados de los casos de uso de Identity, con mensajes en español para quien usa la API.</summary>
public static class IdentityErrors
{
    /// <summary>
    /// El mismo error para un correo que no existe, una contraseña equivocada o un usuario inactivo o bloqueado: así
    /// la respuesta no revela qué correos tienen cuenta.
    /// </summary>
    public static readonly Error InvalidCredentials =
        new("identity.invalid_credentials", "El correo o la contraseña no son correctos.", ErrorType.Unauthorized);

    /// <summary>El mismo error para un token de renovación inexistente, vencido, revocado o de un usuario inactivo.</summary>
    public static readonly Error InvalidRefreshToken =
        new("identity.invalid_refresh_token", "El token de renovación no es válido o ya venció.", ErrorType.Unauthorized);

    /// <summary>
    /// El mismo error para un token de restablecimiento de contraseña desconocido, vencido, ya usado o revocado, o de un
    /// usuario inactivo: no dice cuál de los casos es.
    /// </summary>
    public static readonly Error InvalidResetToken = new(
        "identity.invalid_reset_token",
        "El enlace para restablecer la contraseña no es válido o ya venció. Solicite uno nuevo.",
        ErrorType.Validation);

    public static readonly Error CompanyNotAllowed =
        new("identity.company_not_allowed", "El usuario no pertenece a esa empresa.", ErrorType.Forbidden);

    public static readonly Error Forbidden =
        new("identity.forbidden", "La credencial no tiene permiso para esta operación.", ErrorType.Forbidden);

    public static readonly Error ApiKeyNotFound =
        new("api_key.not_found", "La API key no existe.", ErrorType.NotFound);

    public static readonly Error ApiKeyScopeNotAllowed =
        new("api_key.scope_not_allowed", "La credencial no puede crear una API key con esos alcances.", ErrorType.Forbidden);

    public static readonly Error EmailTaken =
        new("user.email_taken", "Ya existe un usuario con ese correo.", ErrorType.Conflict);

    public static readonly Error RoleNotAllowed =
        new("user.role_not_allowed", "La credencial no puede crear usuarios con ese rol.", ErrorType.Forbidden);
}
