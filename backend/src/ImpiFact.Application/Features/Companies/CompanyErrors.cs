using ImpiFact.Application.Abstractions.Results;

namespace ImpiFact.Application.Features.Companies;

/// <summary>Errores esperados del módulo Company. Los textos que vienen del PHP se conservan tal cual.</summary>
public static class CompanyErrors
{
    /// <summary>
    /// El NIT de la solicitud no es el de la empresa de la credencial. Es el mismo error exista o no ese NIT, para no
    /// revelar qué empresas hay en la plataforma (Arquitectura detallada, sección 9.2).
    /// </summary>
    public static readonly Error ForeignNit = new(
        "company.forbidden",
        "No tiene permiso sobre la empresa indicada.",
        ErrorType.Forbidden);

    /// <summary>Crear empresas es una operación de plataforma (API key sin empresa con alcance <c>platform:*</c>).</summary>
    public static readonly Error PlatformOnly = new(
        "company.platform_only",
        "Solo la plataforma puede crear empresas.",
        ErrorType.Forbidden);

    /// <summary>La empresa de la credencial ya no existe.</summary>
    public static readonly Error NotFound = new(
        "company.not_found",
        "La empresa de la credencial no existe.",
        ErrorType.NotFound);

    /// <summary>Texto del PHP (<c>ConfigurationController.php</c>, alta de empresa).</summary>
    public static readonly Error EmailTaken = new(
        "company.email_taken",
        "Error de registro, el correo electronico ya existe o es el mismo que tiene registrado actualmente.",
        ErrorType.Conflict);

    /// <summary>Texto del PHP (<c>storeEnvironment</c>): no se cambia el ambiente sin software configurado.</summary>
    public static readonly Error SoftwareMissing = new(
        "company.software_missing",
        "No se ha configurado el software para esta empresa. Configure el software primero.",
        ErrorType.Validation);

    public static readonly Error CertificateNotFound = new(
        "certificate.not_found",
        "La empresa no tiene un certificado activo.",
        ErrorType.NotFound);
}
