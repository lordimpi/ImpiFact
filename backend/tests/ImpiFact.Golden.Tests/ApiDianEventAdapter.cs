using System.Globalization;
using System.Text.Json;
using ImpiFact.Dian.Cufe;
using ImpiFact.Dian.Radian;
using ImpiFact.Dian.Ubl;

namespace ImpiFact.Golden.Tests;

/// <summary>
/// Converts the input.json of a RADIAN event case (tests/golden/event) into the <see cref="RadianEvent"/> that ApiDian's
/// SendEventController::sendeventdata builds (php-rules 16.3 to 16.5): the company of the capture is the sender, the
/// provider read from the received invoice is the receiver, and the referenced invoice comes from
/// <c>document_reference</c>.
/// </summary>
public sealed class ApiDianEventAdapter(ApiDianCatalog catalog)
{
    /// <summary>type_liabilities 117 (R-99-PN): the PHP fixes it for the receiver of every event (php-rules 16.4, item 5).</summary>
    private const int ReceiverLiabilityId = 117;

    public RadianEvent Map(JsonElement input)
    {
        var now = DateTime.ParseExact(ApiDianJson.RequiredText(input, "now"), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var eventId = ApiDianJson.RequiredInt(input, "event_id");
        var (code, name) = Events[eventId];
        var reference = input.GetProperty("document_reference");
        var provider = input.GetProperty("provider");
        var tax = catalog.Get("taxes", GoldenCaptureContext.PartyTaxId);
        var providerNit = ApiDianJson.RequiredText(provider, "identification_number");

        return new RadianEvent
        {
            Code = code,
            Description = name,
            Rejection = ApiDianJson.OptionalText(input, "type_rejection_id") is { } rejectionId
                ? Rejections[int.Parse(rejectionId, CultureInfo.InvariantCulture)]
                : null,
            Environment = (DianEnvironment)GoldenCaptureContext.EnvironmentId,
            IssueDate = DateOnly.FromDateTime(now),
            IssueTime = TimeOnly.FromDateTime(now),
            Sender = new EventParty
            {
                Name = GoldenCaptureContext.CompanyName,
                Nit = GoldenCaptureContext.CompanyNit,
                Dv = GoldenCaptureContext.CompanyDv,
                OrganizationTypeCode = catalog.Get("organizationTypes", GoldenCaptureContext.CompanyOrganizationTypeId).Code,
                RegimeCode = catalog.Get("regimes", GoldenCaptureContext.CompanyRegimeId).Code,
                LiabilityCode = catalog.Get("liabilities", GoldenCaptureContext.CompanyLiabilityId).Code,
                TaxSchemeCode = tax.Code,
                TaxSchemeName = tax.Name,
            },
            Receiver = new EventParty
            {
                Name = ApiDianJson.RequiredText(provider, "name"),
                Nit = providerNit,
                Dv = NitCheckDigit.Compute(providerNit).ToString(CultureInfo.InvariantCulture),
                OrganizationTypeCode = catalog.Get("organizationTypes", ApiDianJson.RequiredInt(provider, "type_organization_id")).Code,
                RegimeCode = catalog.Get("regimes", ApiDianJson.RequiredInt(provider, "type_regime_id")).Code,
                LiabilityCode = catalog.Get("liabilities", ReceiverLiabilityId).Code,
                TaxSchemeCode = tax.Code,
                TaxSchemeName = tax.Name,
            },
            Reference = new EventDocumentReference(
                ApiDianJson.RequiredText(reference, "number"),
                ApiDianJson.RequiredText(reference, "uuid"),
                "CUFE-SHA384",
                catalog.Get("documentTypes", ApiDianJson.RequiredInt(reference, "type_document_id")).Code),
            Person = input.TryGetProperty("issuer_party", out var person)
                ? new EventPerson(
                    ApiDianJson.RequiredText(person, "identification_number"),
                    ApiDianJson.RequiredText(person, "first_name"),
                    ApiDianJson.RequiredText(person, "last_name"),
                    ApiDianJson.RequiredText(person, "job_title"),
                    ApiDianJson.RequiredText(person, "organization_department"))
                : null,
            Software = new SoftwareCredentials(GoldenCaptureContext.SoftwareId, GoldenCaptureContext.SoftwarePin),
        };
    }

    // events.csv and type_rejections.csv of ApiDian (php-rules 16.3).
    private static readonly Dictionary<int, (string Code, string Name)> Events = new()
    {
        [1] = ("030", "Acuse de recibo de Factura Electrónica de Venta"),
        [2] = ("031", "Reclamo de la Factura Electrónica de Venta"),
        [3] = ("032", "Recibo del bien y/o prestación del servicio"),
        [4] = ("033", "Aceptación expresa"),
        [5] = ("034", "Aceptación Tácita"),
    };

    private static readonly Dictionary<int, EventRejection> Rejections = new()
    {
        [1] = new("01", "Documento con inconsistencias"),
        [2] = new("02", "Mercancía no entregada totalmente"),
        [3] = new("03", "Mercancía no entregada parcialmente"),
        [4] = new("04", "Servicio no prestado"),
    };
}
