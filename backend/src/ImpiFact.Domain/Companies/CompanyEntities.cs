using ImpiFact.Domain.Common;

namespace ImpiFact.Domain.Companies;

// Empresa y configuración (Modelo de datos detallado, sección 3.3). Las columnas *_cipher guardan secretos
// cifrados por la aplicación; nunca el valor en claro.

/// <summary>
/// Empresa emisora (<c>companies</c>): el tenant (cada empresa cliente con sus datos aislados). Su razón social y
/// correo vivían en <c>users</c> del PHP. Las responsabilidades, ambientes, software y planes van en tablas hijas.
/// </summary>
public sealed class Company : PlatformEntity
{
    public Guid? ResellerId { get; set; }

    /// <summary>NIT sin dígito de verificación, solo dígitos; es único.</summary>
    public required string IdentificationNumber { get; init; }

    public required string Dv { get; set; }

    /// <summary>Razón social.</summary>
    public required string Name { get; set; }

    public required string Email { get; set; }

    public required string Phone { get; set; }

    public required string Address { get; set; }

    public string? MerchantRegistration { get; set; }

    public int LanguageId { get; set; }

    public int CountryId { get; set; }

    public int MunicipalityId { get; set; }

    public int TaxId { get; set; }

    public int TypeCurrencyId { get; set; }

    public int TypeOperationId { get; set; }

    public int TypeDocumentIdentificationId { get; set; }

    public int TypeOrganizationId { get; set; }

    public int TypeRegimeId { get; set; }

    /// <summary>Dónde se guardan los archivos; nulo usa el valor de la plataforma.</summary>
    public StorageMode? StorageMode { get; set; }

    /// <summary>Plantilla de la representación gráfica (PDF); el PHP usa la 2 por defecto.</summary>
    public int GraphicRepresentationTemplate { get; set; } = 2;

    public bool AllowSellerLogin { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>Almacenamiento de archivos de la empresa (migración PHP <c>add_storage_mode</c>).</summary>
public enum StorageMode : short
{
    Local = 0,
    Cloud = 1,
}

/// <summary>Responsabilidad fiscal de la empresa (<c>company_liabilities</c>); reemplaza la lista JSON del PHP.</summary>
public sealed class CompanyLiability : TenantRecord
{
    public int TypeLiabilityId { get; init; }
}

/// <summary>Ambiente DIAN de cada familia de documentos (<c>company_environments</c>).</summary>
public sealed class CompanyEnvironment : TenantEntity
{
    public DocumentKind DocumentKind { get; init; }

    public int TypeEnvironmentId { get; set; }
}

/// <summary>
/// Software registrado ante la DIAN para una familia de documentos y un ambiente (<c>company_software</c>; en el
/// PHP, una sola fila de <c>software</c> con cinco juegos de columnas).
/// </summary>
public sealed class CompanySoftware : TenantEntity
{
    public DocumentKind DocumentKind { get; init; }

    public int TypeEnvironmentId { get; init; }

    public required string SoftwareIdentifier { get; set; }

    /// <summary>PIN del software cifrado por la aplicación.</summary>
    public required string PinCipher { get; set; }

    public required string DianUrl { get; set; }

    /// <summary>Identificador del set de pruebas de habilitación.</summary>
    public string? TestSetId { get; set; }
}

/// <summary>
/// Certificado de firma de la empresa (<c>certificates</c>): solo la referencia al secreto, la huella y la
/// vigencia; el archivo y su clave quedan en el almacén de certificados. Una empresa tiene un solo certificado activo.
/// </summary>
public sealed class Certificate : TenantEntity
{
    public required string Name { get; set; }

    /// <summary>Clave del secreto en el almacén (Key Vault, Secrets Manager, Secret Manager o ruta cifrada).</summary>
    public required string SecretRef { get; init; }

    public required string Thumbprint { get; init; }

    public required string Subject { get; init; }

    public required string Issuer { get; init; }

    public DateTimeOffset NotBefore { get; init; }

    public DateTimeOffset NotAfter { get; init; }

    public bool IsActive { get; set; }

    /// <summary>Usuario del panel que lo subió; nulo si lo subió una integración con su API key.</summary>
    public Guid? UploadedBy { get; init; }
}

/// <summary>Resolución de numeración de la DIAN (<c>resolutions</c>), con el ambiente obligatorio.</summary>
public sealed class Resolution : TenantEntity
{
    public int DocumentTypeId { get; init; }

    public int TypeEnvironmentId { get; init; }

    public string? Prefix { get; set; }

    /// <summary>Número y fecha de la resolución DIAN; el PHP los deja vacíos en las resoluciones que no son de factura (notas).</summary>
    public string? ResolutionNumber { get; set; }

    public DateOnly? ResolutionDate { get; set; }

    /// <summary>Clave técnica de la resolución (entra en el cálculo del CUFE).</summary>
    public string? TechnicalKey { get; set; }

    public long RangeFrom { get; set; }

    public long RangeTo { get; set; }

    public DateOnly? ValidFrom { get; set; }

    public DateOnly? ValidTo { get; set; }

    public bool IsActive { get; set; }
}

/// <summary>
/// Servidor de correo propio de la empresa (<c>company_mail_settings</c>), una fila por empresa. La contraseña
/// SMTP (protocolo de envío de correo) se guarda cifrada.
/// </summary>
public sealed class CompanyMailSettings : TenantRecord
{
    public bool UsePlatformDefault { get; set; }

    public string? Host { get; set; }

    public int? Port { get; set; }

    public string? Username { get; set; }

    public string? PasswordCipher { get; set; }

    /// <summary>Cifrado de la conexión, por ejemplo <c>tls</c> o <c>ssl</c>.</summary>
    public string? Encryption { get; set; }

    public string? FromAddress { get; set; }

    public string? FromName { get; set; }
}

/// <summary>Logo de la empresa (<c>company_logos</c>), una fila por empresa; el archivo va al almacenamiento de archivos.</summary>
public sealed class CompanyLogo : TenantRecord
{
    public required string StorageKey { get; set; }

    public required string ContentType { get; set; }
}
