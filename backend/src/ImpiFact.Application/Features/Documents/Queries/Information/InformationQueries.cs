using System.Text.Json.Serialization;

namespace ImpiFact.Application.Features.Documents.Queries.Information;

// Consultas heredadas de resumen por empresa (ResumeController.php del PHP; anexo A.5). El PHP las dejaba públicas;
// aquí piden una credencial con documents:read y el NIT (Número de Identificación Tributaria) debe ser el de la
// empresa de la credencial. Las respuestas conservan los nombres JSON del PHP.

/// <summary><c>GET information/{nit}[/{desde}[/{hasta}]]</c>: documentos validados de los tipos 1, 4, 5 y 15.</summary>
/// <param name="From">Fecha inicial <c>yyyy-MM-dd</c> en hora Colombia; con una sola fecha se consulta ese día.</param>
/// <param name="To">Fecha final incluida.</param>
public sealed record GetInformationQuery(string Nit, string? From, string? To);

/// <summary><c>GET information/{nit}/page/{page}/page</c>: los mismos tipos, 100 por tipo y página, del más antiguo al más nuevo.</summary>
public sealed record GetInformationPageQuery(string Nit, string Page);

/// <summary><c>GET total_documents/{nit}[/{desde}[/{hasta}]]</c>: cantidad de documentos validados de los 8 tipos del PHP.</summary>
public sealed record GetTotalDocumentsQuery(string Nit, string? From, string? To);

/// <summary>Respuesta de <c>information</c>: un grupo por tipo de documento.</summary>
public sealed record InformationResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; init; } = true;

    [JsonPropertyName("message")]
    public string Message { get; init; } = LegacyTypes.Found;

    [JsonPropertyName("data")]
    public IReadOnlyList<InformationGroup> Data { get; init; } = [];

    /// <summary>Razón social de la empresa (en el PHP, el nombre de su usuario).</summary>
    [JsonPropertyName("company")]
    public string Company { get; init; } = "";
}

/// <summary>Documentos de un tipo. La variante por página del PHP no trae <c>type_document_id</c>.</summary>
public sealed record InformationGroup
{
    [JsonPropertyName("type_document_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TypeDocumentId { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("count")]
    public int Count { get; init; }

    [JsonPropertyName("documents")]
    public IReadOnlyList<LegacyDocument> Documents { get; init; } = [];
}

/// <summary>
/// Un documento con los nombres de columna del PHP (tabla <c>documents</c>). Los valores salen del modelo nuevo:
/// <c>sale</c> y <c>total</c> son el total a pagar, <c>subtotal</c> la base de las líneas y <c>total_tax</c> la
/// diferencia entre el total con y sin impuestos, como los calculaba el PHP. No trae los nombres de archivo XML y
/// PDF del PHP: los archivos se descargan por sus rutas propias.
/// </summary>
public sealed record LegacyDocument
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    /// <summary>NIT de la empresa emisora.</summary>
    [JsonPropertyName("identification_number")]
    public string IdentificationNumber { get; init; } = "";

    [JsonPropertyName("state_document_id")]
    public int StateDocumentId { get; init; }

    [JsonPropertyName("type_document_id")]
    public int TypeDocumentId { get; init; }

    [JsonPropertyName("prefix")]
    public string Prefix { get; init; } = "";

    [JsonPropertyName("number")]
    public long Number { get; init; }

    [JsonPropertyName("cufe")]
    public string? Cufe { get; init; }

    /// <summary>Número de identificación del adquiriente.</summary>
    [JsonPropertyName("customer")]
    public string Customer { get; init; } = "";

    /// <summary>Datos del adquiriente copiados al emitir, como el <c>customer</c> de la solicitud.</summary>
    [JsonPropertyName("client")]
    public LegacyClient Client { get; init; } = new();

    [JsonPropertyName("currency_id")]
    public int CurrencyId { get; init; }

    /// <summary>Fecha y hora de emisión en hora Colombia, <c>yyyy-MM-dd HH:mm:ss</c>.</summary>
    [JsonPropertyName("date_issue")]
    public string DateIssue { get; init; } = "";

    [JsonPropertyName("sale")]
    public decimal Sale { get; init; }

    [JsonPropertyName("total_discount")]
    public decimal TotalDiscount { get; init; }

    [JsonPropertyName("total_tax")]
    public decimal TotalTax { get; init; }

    [JsonPropertyName("subtotal")]
    public decimal Subtotal { get; init; }

    [JsonPropertyName("total")]
    public decimal Total { get; init; }

    /// <summary>Ambiente de la DIAN en que se emitió (1 producción, 2 habilitación).</summary>
    [JsonPropertyName("ambient_id")]
    public int AmbientId { get; init; }
}

/// <summary>Adquiriente con los nombres del objeto <c>customer</c> del contrato heredado.</summary>
public sealed record LegacyClient
{
    [JsonPropertyName("identification_number")]
    public string IdentificationNumber { get; init; } = "";

    [JsonPropertyName("dv")]
    public string? Dv { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("phone")]
    public string? Phone { get; init; }

    [JsonPropertyName("address")]
    public string? Address { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("merchant_registration")]
    public string? MerchantRegistration { get; init; }

    [JsonPropertyName("type_document_identification_id")]
    public int TypeDocumentIdentificationId { get; init; }

    [JsonPropertyName("type_organization_id")]
    public int? TypeOrganizationId { get; init; }

    [JsonPropertyName("type_liability_id")]
    public int? TypeLiabilityId { get; init; }

    [JsonPropertyName("type_regime_id")]
    public int? TypeRegimeId { get; init; }

    [JsonPropertyName("municipality_id")]
    public int? MunicipalityId { get; init; }
}

/// <summary>Respuesta de <c>total_documents</c>.</summary>
public sealed record TotalDocumentsResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; init; } = true;

    [JsonPropertyName("message")]
    public string Message { get; init; } = LegacyTypes.Found;

    [JsonPropertyName("data")]
    public IReadOnlyList<DocumentTypeTotal> Data { get; init; } = [];

    [JsonPropertyName("company")]
    public string Company { get; init; } = "";
}

public sealed record DocumentTypeTotal
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("count")]
    public long Count { get; init; }
}
