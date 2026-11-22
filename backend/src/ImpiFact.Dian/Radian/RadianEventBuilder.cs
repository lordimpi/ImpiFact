using ImpiFact.Dian.Cufe;
using ImpiFact.Dian.Ubl;

namespace ImpiFact.Dian.Radian;

/// <summary>
/// Builds the unsigned XML of a RADIAN event (ApplicationResponse) as ApiDian's template 88 and its partials
/// <c>_ubl_extensions_appresponse</c>, <c>_customer_attached</c> and <c>_issuer_party</c> write it (php-rules 16.4), byte for
/// byte with the goldens of tests/golden/event. The second ext:ExtensionContent is the empty slot of the signature, as in
/// the invoice; <see cref="Signing.XmlSigner"/> signs it the same way.
/// </summary>
public static class RadianEventBuilder
{
    /// <summary>
    /// Start of the QR of every event: Anexo Técnico 1.9, rule AAB36, asks for the production catalog URL followed by the
    /// CUFE of the referenced invoice (not the CUDE of the event), whatever the environment. ApiDian writes the same.
    /// </summary>
    public const string QrCodeBaseUrl = "https://catalogo-vpfe.dian.gov.co/document/searchqr?documentkey=";

    /// <summary>schemeName of cbc:UUID (rule AAD08).</summary>
    public const string CudeSchemeName = "CUDE-SHA384";

    /// <summary>
    /// Identification type the PHP writes for the receiver and for the default person: its model never assigns the type,
    /// so it is always 13 (php-rules 16.4, items 5 and 7). Kept for parity; open question in php-rules 18.6.
    /// </summary>
    private const string PhpIdentificationTypeCode = "13";

    private const string DefaultJobTitle = "ADMINISTRADOR";
    private const string DefaultDepartment = "ADMINISTRATIVA";
    private const string AuthorizationProviderNit = "800197268";

    public static RadianEventResult Build(RadianEvent radianEvent)
    {
        ArgumentNullException.ThrowIfNull(radianEvent);
        var e = radianEvent;
        var number = e.Receiver.Nit + e.Reference.Number + e.Code;
        var cude = CufeCalculator.ComputeEventCude(
            number, e.IssueDate, e.IssueTime, e.Sender.Nit, e.Receiver.Nit, e.Code, e.Reference.Number, e.Reference.DocumentTypeCode,
            e.Software.Pin);
        var environment = UblParts.EnvironmentCode(e.Environment);

        var root = new UblElement("ApplicationResponse")
            .Attr("xmlns:ds", "http://www.w3.org/2000/09/xmldsig#")
            .Attr("xmlns:xades", "http://uri.etsi.org/01903/v1.3.2#")
            .Attr("xmlns:cbc", "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")
            .Attr("xmlns:ext", "urn:oasis:names:specification:ubl:schema:xsd:CommonExtensionComponents-2")
            .Attr("xmlns:xades141", "http://uri.etsi.org/01903/v1.4.1#")
            .Attr("xmlns:sts", "urn:dian:gov:co:facturaelectronica:Structures-2-1")
            .Attr("xmlns:cac", "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2")
            .Attr("xmlns:xsi", "http://www.w3.org/2001/XMLSchema-instance")
            .Attr("xmlns", "urn:oasis:names:specification:ubl:schema:xsd:ApplicationResponse-2");

        root.Add(Extensions(e, number))
            .Add("cbc:UBLVersionID", "UBL 2.1")
            .Add("cbc:CustomizationID", "1")
            .Add("cbc:ProfileID", "DIAN 2.1: ApplicationResponse de la Factura Electrónica de Venta")
            .Add("cbc:ProfileExecutionID", environment)
            .Add("cbc:ID", number)
            .Add(new UblElement("cbc:UUID", cude).Attr("schemeID", environment).Attr("schemeName", CudeSchemeName))
            .Add("cbc:IssueDate", UblParts.Date(e.IssueDate))
            .Add("cbc:IssueTime", UblParts.Time(e.IssueTime));
        if (!string.IsNullOrEmpty(e.Note))
        {
            root.Add("cbc:Note", e.Note);
        }

        root.Add(new UblElement("cac:SenderParty").Add(PartyTaxScheme(e.Sender, sender: true)))
            .Add(new UblElement("cac:ReceiverParty").Add(PartyTaxScheme(e.Receiver, sender: false)))
            .Add(DocumentResponse(e));

        return new RadianEventResult(UblXmlSerializer.Serialize(root), cude, number);
    }

    private static UblElement Extensions(RadianEvent e, string number)
    {
        var dianExtensions = new UblElement("sts:DianExtensions")
            .Add(new UblElement("sts:InvoiceSource")
                .Add(new UblElement("cbc:IdentificationCode", "CO")
                    .Attr("listAgencyID", "6")
                    .Attr("listAgencyName", "United Nations Economic Commission for Europe")
                    .Attr("listSchemeURI", "urn:oasis:names:specification:ubl:codelist:gc:CountryIdentificationCode-2.1")))
            .Add(new UblElement("sts:SoftwareProvider")
                .Add(Dian(new UblElement("sts:ProviderID", e.Sender.Nit)).Attr("schemeID", e.Sender.Dv).Attr("schemeName", "31"))
                .Add(Dian(new UblElement("sts:SoftwareID", e.Software.SoftwareId))))
            .Add(Dian(new UblElement("sts:SoftwareSecurityCode", SoftwareSecurityCode.Compute(e.Software.SoftwareId, e.Software.Pin, number))))
            .Add(new UblElement("sts:AuthorizationProvider")
                .Add(Dian(new UblElement("sts:AuthorizationProviderID", AuthorizationProviderNit)).Attr("schemeID", "4").Attr("schemeName", "31")))
            .Add("sts:QRCode", QrCodeBaseUrl + e.Reference.Uuid);

        return new UblElement("ext:UBLExtensions")
            .Add(new UblElement("ext:UBLExtension").Add(new UblElement("ext:ExtensionContent").Add(dianExtensions)))
            .Add(new UblElement("ext:UBLExtension").Add(new UblElement("ext:ExtensionContent")));
    }

    // The sender writes schemeVersionID before schemeName (fixed 31); the receiver, schemeName (13) before
    // schemeVersionID, as the two templates of the PHP do.
    private static UblElement PartyTaxScheme(EventParty party, bool sender)
    {
        var companyId = Dian(new UblElement("cbc:CompanyID", party.Nit)).Attr("schemeID", party.Dv);
        if (sender)
        {
            companyId.Attr("schemeVersionID", party.OrganizationTypeCode).Attr("schemeName", "31");
        }
        else
        {
            companyId.Attr("schemeName", PhpIdentificationTypeCode).Attr("schemeVersionID", party.OrganizationTypeCode);
        }

        var scheme = new UblElement("cac:PartyTaxScheme")
            .Add("cbc:RegistrationName", party.Name)
            .Add(companyId);
        if (party.LiabilityCode is { Length: > 0 } liability)
        {
            scheme.Add(new UblElement("cbc:TaxLevelCode", liability).Attr("listName", party.RegimeCode));
        }

        return scheme.Add(new UblElement("cac:TaxScheme")
            .Add("cbc:ID", party.TaxSchemeCode)
            .Add("cbc:Name", party.TaxSchemeName));
    }

    private static UblElement DocumentResponse(RadianEvent e)
    {
        var responseCode = new UblElement("cbc:ResponseCode", e.Code);
        if (e.Rejection is { } rejection)
        {
            responseCode.Attr("listID", rejection.Code).Attr("name", rejection.Name);
        }

        var person = e.Person is { } given
            ? new UblElement("cac:Person")
                .Add(new UblElement("cbc:ID", given.IdentificationNumber).Attr("schemeID", "").Attr("schemeName", PhpIdentificationTypeCode))
                .Add("cbc:FirstName", given.FirstName)
                .Add("cbc:FamilyName", given.FamilyName)
                .Add("cbc:JobTitle", given.JobTitle)
                .Add("cbc:OrganizationDepartment", given.OrganizationDepartment)
            : new UblElement("cac:Person")
                .Add(new UblElement("cbc:ID", e.Sender.Nit).Attr("schemeID", e.Sender.Dv).Attr("schemeName", PhpIdentificationTypeCode))
                .Add("cbc:FirstName", e.Sender.Name)
                .Add("cbc:FamilyName", e.Sender.Name)
                .Add("cbc:JobTitle", DefaultJobTitle)
                .Add("cbc:OrganizationDepartment", DefaultDepartment);

        return new UblElement("cac:DocumentResponse")
            .Add(new UblElement("cac:Response")
                .Add(responseCode)
                .Add("cbc:Description", e.Description))
            .Add(new UblElement("cac:DocumentReference")
                .Add("cbc:ID", e.Reference.Number)
                .Add(new UblElement("cbc:UUID", e.Reference.Uuid).Attr("schemeName", e.Reference.UuidSchemeName))
                .Add("cbc:DocumentTypeCode", e.Reference.DocumentTypeCode))
            .Add(new UblElement("cac:IssuerParty").Add(person));
    }

    private static UblElement Dian(UblElement element) =>
        element.Attr("schemeAgencyID", UblParts.DianAgencyId).Attr("schemeAgencyName", UblParts.DianAgencyName);
}
