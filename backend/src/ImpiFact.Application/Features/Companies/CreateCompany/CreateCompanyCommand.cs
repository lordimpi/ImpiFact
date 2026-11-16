using System.Text.Json.Serialization;
using ImpiFact.Application.Abstractions.Handlers;

namespace ImpiFact.Application.Features.Companies.CreateCompany;

/// <summary>
/// Alta o actualización de una empresa por su NIT (<c>POST config/{nit}/{dv}</c>; «php-rules» 5.2.1: idempotente).
/// Es una operación de plataforma: la pide una API key sin empresa con alcance <c>platform:*</c>, por eso no exige
/// empresa en el contexto (<see cref="ICompanyOptional"/>).
/// </summary>
public sealed record CreateCompanyCommand : CompanyData, ICommand, ICompanyOptional
{
    // Ambientes iniciales por familia de documentos (solo al crear, como el PHP); sin valor, habilitación.

    [JsonPropertyName("type_environment_id")]
    public int? TypeEnvironmentId { get; init; }

    [JsonPropertyName("payroll_type_environment_id")]
    public int? PayrollTypeEnvironmentId { get; init; }

    [JsonPropertyName("eqdocs_type_environment_id")]
    public int? EqdocsTypeEnvironmentId { get; init; }

    [JsonPropertyName("support_document_type_environment_id")]
    public int? SupportDocumentTypeEnvironmentId { get; init; }

    [JsonPropertyName("event_type_environment_id")]
    public int? EventTypeEnvironmentId { get; init; }
}

/// <summary>Respuesta del alta o actualización, con los campos <c>success</c>, <c>message</c> y <c>token</c> del PHP.</summary>
public sealed record CompanySavedResponse
{
    public const string SavedMessage = "Empresa creada/actualizada con éxito";

    [JsonPropertyName("success")]
    public bool Success { get; init; } = true;

    [JsonPropertyName("message")]
    public string Message { get; init; } = SavedMessage;

    /// <summary>Verdadero si la empresa se creó en esta solicitud; falso si ya existía y se actualizó.</summary>
    [JsonPropertyName("created")]
    public bool Created { get; init; }

    /// <summary>
    /// API key de la empresa en texto claro. Solo viene al crearla y se muestra esta única vez; en una actualización
    /// es nulo, porque de las llaves guardadas solo queda su resumen.
    /// </summary>
    [JsonPropertyName("token")]
    public string? Token { get; init; }

    [JsonPropertyName("company")]
    public required CompanyResponse Company { get; init; }

    /// <summary>No incluye la API key, para que un registro accidental del objeto no la filtre.</summary>
    public override string ToString() => $"CompanySavedResponse {{ Created = {Created}, CompanyId = {Company.Id} }}";
}
