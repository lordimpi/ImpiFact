using System.Text.Json.Serialization;

namespace ImpiFact.Application.Features.Companies;

// Respuestas del módulo Company. Nunca se exponen entidades (Arquitectura detallada, sección 4.2): Mapster las copia
// a estos registros (ver CompanyMapping). Los nombres JSON siguen el contrato heredado (snake_case). Ninguna lleva
// el PIN, la contraseña del certificado ni la referencia al secreto.

/// <summary>Datos de la empresa.</summary>
public sealed record CompanyResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("identification_number")]
    public string IdentificationNumber { get; init; } = "";

    [JsonPropertyName("dv")]
    public string Dv { get; init; } = "";

    [JsonPropertyName("business_name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("email")]
    public string Email { get; init; } = "";

    [JsonPropertyName("phone")]
    public string Phone { get; init; } = "";

    [JsonPropertyName("address")]
    public string Address { get; init; } = "";

    [JsonPropertyName("merchant_registration")]
    public string? MerchantRegistration { get; init; }

    [JsonPropertyName("language_id")]
    public int LanguageId { get; init; }

    [JsonPropertyName("country_id")]
    public int CountryId { get; init; }

    [JsonPropertyName("municipality_id")]
    public int MunicipalityId { get; init; }

    [JsonPropertyName("tax_id")]
    public int TaxId { get; init; }

    [JsonPropertyName("type_currency_id")]
    public int TypeCurrencyId { get; init; }

    [JsonPropertyName("type_operation_id")]
    public int TypeOperationId { get; init; }

    [JsonPropertyName("type_document_identification_id")]
    public int TypeDocumentIdentificationId { get; init; }

    [JsonPropertyName("type_organization_id")]
    public int TypeOrganizationId { get; init; }

    [JsonPropertyName("type_regime_id")]
    public int TypeRegimeId { get; init; }

    [JsonPropertyName("type_liability_ids")]
    public IReadOnlyList<int> TypeLiabilityIds { get; init; } = [];

    [JsonPropertyName("environments")]
    public IReadOnlyList<EnvironmentResponse> Environments { get; init; } = [];

    [JsonPropertyName("is_active")]
    public bool IsActive { get; init; }
}

/// <summary>Ambiente de una familia de documentos.</summary>
public sealed record EnvironmentResponse
{
    /// <summary>1 factura y notas, 2 nómina, 3 documento equivalente, 4 documento soporte, 5 eventos.</summary>
    [JsonPropertyName("document_kind")]
    public int DocumentKind { get; init; }

    /// <summary>1 producción, 2 habilitación.</summary>
    [JsonPropertyName("type_environment_id")]
    public int TypeEnvironmentId { get; init; }
}

/// <summary>Software de una familia de documentos en un ambiente, sin el PIN.</summary>
public sealed record SoftwareResponse
{
    [JsonPropertyName("document_kind")]
    public int DocumentKind { get; init; }

    [JsonPropertyName("type_environment_id")]
    public int TypeEnvironmentId { get; init; }

    [JsonPropertyName("identifier")]
    public string Identifier { get; init; } = "";

    [JsonPropertyName("url")]
    public string Url { get; init; } = "";

    [JsonPropertyName("test_set_id")]
    public string? TestSetId { get; init; }
}

/// <summary>Resolución de numeración.</summary>
public sealed record ResolutionResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("type_document_id")]
    public int TypeDocumentId { get; init; }

    [JsonPropertyName("type_environment_id")]
    public int TypeEnvironmentId { get; init; }

    [JsonPropertyName("prefix")]
    public string? Prefix { get; init; }

    [JsonPropertyName("resolution")]
    public string Resolution { get; init; } = "";

    [JsonPropertyName("resolution_date")]
    public DateOnly ResolutionDate { get; init; }

    [JsonPropertyName("technical_key")]
    public string? TechnicalKey { get; init; }

    [JsonPropertyName("from")]
    public long From { get; init; }

    [JsonPropertyName("to")]
    public long To { get; init; }

    [JsonPropertyName("date_from")]
    public DateOnly? DateFrom { get; init; }

    [JsonPropertyName("date_to")]
    public DateOnly? DateTo { get; init; }
}

/// <summary>Metadatos de un certificado guardado.</summary>
public sealed record CertificateResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("thumbprint")]
    public string Thumbprint { get; init; } = "";

    [JsonPropertyName("subject")]
    public string Subject { get; init; } = "";

    [JsonPropertyName("issuer")]
    public string Issuer { get; init; } = "";

    [JsonPropertyName("not_before")]
    public DateTimeOffset NotBefore { get; init; }

    [JsonPropertyName("expiration_date")]
    public DateTimeOffset NotAfter { get; init; }

    [JsonPropertyName("is_active")]
    public bool IsActive { get; init; }

    /// <summary>Días que faltan para el vencimiento (regla del PHP; ver <see cref="CertificateDays"/>).</summary>
    [JsonPropertyName("certificate_days_left")]
    public int DaysLeft { get; init; }
}
