using System.Text.Json.Serialization;

namespace ImpiFact.Application.Features.Companies;

/// <summary>
/// Datos de la empresa que llegan a <c>POST config/{nit}/{dv}</c> (anexo A.5 y <c>ConfigurationRequest.php</c>), con
/// los nombres JSON del contrato heredado. Los usan el alta (<c>CreateCompany</c>) y la actualización
/// (<c>UpdateCompany</c>). Todo es anulable para que un dato que falta se informe como error de validación y no
/// como un cuerpo ilegible. Los campos del PHP que ImpiFact maneja en otro lugar (correo SMTP, planes) se ignoran.
/// </summary>
public abstract record CompanyData
{
    /// <summary>NIT sin dígito de verificación; viene de la ruta.</summary>
    [JsonIgnore]
    public string? Nit { get; init; }

    /// <summary>Dígito de verificación; viene de la ruta.</summary>
    [JsonIgnore]
    public string? Dv { get; init; }

    [JsonPropertyName("type_document_identification_id")]
    public int? TypeDocumentIdentificationId { get; init; }

    [JsonPropertyName("type_organization_id")]
    public int? TypeOrganizationId { get; init; }

    [JsonPropertyName("type_regime_id")]
    public int? TypeRegimeId { get; init; }

    /// <summary>Una responsabilidad fiscal; se suma a <see cref="TypeLiabilityIds"/> (anexo A.8: aceptar ambos).</summary>
    [JsonPropertyName("type_liability_id")]
    public int? TypeLiabilityId { get; init; }

    [JsonPropertyName("type_liability_ids")]
    public IReadOnlyList<int>? TypeLiabilityIds { get; init; }

    /// <summary>Razón social.</summary>
    [JsonPropertyName("business_name")]
    public string? BusinessName { get; init; }

    [JsonPropertyName("merchant_registration")]
    [JsonConverter(typeof(NumberOrStringConverter))]
    public string? MerchantRegistration { get; init; }

    [JsonPropertyName("municipality_id")]
    public int? MunicipalityId { get; init; }

    [JsonPropertyName("address")]
    public string? Address { get; init; }

    [JsonPropertyName("phone")]
    [JsonConverter(typeof(NumberOrStringConverter))]
    public string? Phone { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("language_id")]
    public int? LanguageId { get; init; }

    [JsonPropertyName("tax_id")]
    public int? TaxId { get; init; }

    [JsonPropertyName("type_operation_id")]
    public int? TypeOperationId { get; init; }

    [JsonPropertyName("country_id")]
    public int? CountryId { get; init; }

    [JsonPropertyName("type_currency_id")]
    public int? TypeCurrencyId { get; init; }

    /// <summary>
    /// Responsabilidades sin repetir: <c>type_liability_ids</c> más <c>type_liability_id</c>, en el orden recibido.
    /// </summary>
    public IReadOnlyList<int> NormalizedLiabilityIds() =>
        (TypeLiabilityIds ?? []).Concat(TypeLiabilityId is { } single ? [single] : []).Distinct().ToList();
}
