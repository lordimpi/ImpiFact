using System.Text.Json.Serialization;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Domain.Common;

namespace ImpiFact.Application.Features.Companies.SetSoftware;

/// <summary>
/// Registra o actualiza el software de la empresa ante la DIAN (<c>PUT config/software</c>): identificador, PIN de
/// 5 dígitos y URL del servicio por familia de documentos, con los nombres del PHP
/// (<c>ConfigurationSoftwareRequest.php</c>). El software queda en el ambiente actual de cada familia. El PIN se
/// guarda cifrado y nunca se devuelve ni se registra.
/// </summary>
public sealed record SetSoftwareCommand : ICommand
{
    [JsonIgnore]
    public string? Nit { get; init; }

    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("pin")]
    [JsonConverter(typeof(NumberOrStringConverter))]
    public string? Pin { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }

    /// <summary>Set de pruebas de habilitación de la factura (<c>testSetId</c> de la DIAN).</summary>
    [JsonPropertyName("test_set_id")]
    public string? TestSetId { get; init; }

    [JsonPropertyName("idpayroll")]
    public string? IdPayroll { get; init; }

    [JsonPropertyName("pinpayroll")]
    [JsonConverter(typeof(NumberOrStringConverter))]
    public string? PinPayroll { get; init; }

    [JsonPropertyName("urlpayroll")]
    public string? UrlPayroll { get; init; }

    [JsonPropertyName("ideqdocs")]
    public string? IdEqdocs { get; init; }

    [JsonPropertyName("pineqdocs")]
    [JsonConverter(typeof(NumberOrStringConverter))]
    public string? PinEqdocs { get; init; }

    [JsonPropertyName("urleqdocs")]
    public string? UrlEqdocs { get; init; }

    [JsonPropertyName("idsupportdocument")]
    public string? IdSupportDocument { get; init; }

    [JsonPropertyName("pinsupportdocument")]
    [JsonConverter(typeof(NumberOrStringConverter))]
    public string? PinSupportDocument { get; init; }

    [JsonPropertyName("urlsupportdocument")]
    public string? UrlSupportDocument { get; init; }

    /// <summary>Otro nombre que el PHP acepta para <see cref="IdSupportDocument"/>.</summary>
    [JsonPropertyName("identifier_support_document")]
    public string? IdentifierSupportDocument { get; init; }

    /// <summary>Otro nombre que el PHP acepta para <see cref="PinSupportDocument"/>.</summary>
    [JsonPropertyName("pin_support_document")]
    [JsonConverter(typeof(NumberOrStringConverter))]
    public string? PinSupportDocumentAlias { get; init; }

    /// <summary>Datos de cada familia que trae la solicitud (las que no traen nada se dejan como están).</summary>
    public IReadOnlyList<SoftwareEntry> Entries()
    {
        SoftwareEntry[] all =
        [
            new(DocumentKind.Invoice, "id", Id, "pin", Pin, "url", Url, TestSetId),
            new(DocumentKind.Payroll, "idpayroll", IdPayroll, "pinpayroll", PinPayroll, "urlpayroll", UrlPayroll, null),
            new(DocumentKind.EquivalentDocument, "ideqdocs", IdEqdocs, "pineqdocs", PinEqdocs, "urleqdocs", UrlEqdocs, null),
            new(DocumentKind.SupportDocument, "idsupportdocument", IdSupportDocument ?? IdentifierSupportDocument,
                "pinsupportdocument", PinSupportDocument ?? PinSupportDocumentAlias, "urlsupportdocument", UrlSupportDocument, null),
        ];
        return all.Where(entry => entry.HasData).ToList();
    }

    /// <summary>No incluye los PIN, para que un registro accidental del objeto no los filtre.</summary>
    public override string ToString() => "SetSoftwareCommand { PINs hidden }";
}

/// <summary>Datos del software de una familia, con los nombres JSON de sus campos para los mensajes.</summary>
public sealed record SoftwareEntry(
    DocumentKind Kind, string IdField, string? Identifier, string PinField, string? Pin, string UrlField, string? Url, string? TestSetId)
{
    public bool HasData => Identifier is not null || Pin is not null || Url is not null || TestSetId is not null;

    public override string ToString() => $"SoftwareEntry {{ Kind = {Kind}, PIN hidden }}";
}

/// <summary>Respuesta con el mensaje del PHP y el software que quedó, sin el PIN.</summary>
public sealed record SoftwareSavedResponse
{
    public const string SavedMessage = "Software creado/actualizado con éxito";

    [JsonPropertyName("success")]
    public bool Success { get; init; } = true;

    [JsonPropertyName("message")]
    public string Message { get; init; } = SavedMessage;

    [JsonPropertyName("software")]
    public IReadOnlyList<SoftwareResponse> Software { get; init; } = [];
}
