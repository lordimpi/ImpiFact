using ImpiFact.Dian.Cufe;
using ImpiFact.Dian.Ubl;

namespace ImpiFact.Dian.Radian;

/// <summary>
/// A RADIAN event (the register of the electronic invoice as a negotiable instrument): the ApplicationResponse document
/// of Anexo Técnico 1.9, section 6.5, that a party sends to the DIAN about an invoice. ImpiFact writes it like
/// ApiDian's template 88 (php-rules 16.4).
/// </summary>
public sealed record RadianEvent
{
    /// <summary>Event code, cbc:ResponseCode: <c>030</c> to <c>034</c>.</summary>
    public required string Code { get; init; }

    /// <summary>Event name, cbc:Description (the <c>events</c> catalog).</summary>
    public required string Description { get; init; }

    /// <summary>Claim reason (<c>type_rejections</c>) of a 031, written as listID and name of cbc:ResponseCode.</summary>
    public EventRejection? Rejection { get; init; }

    /// <summary>Environment of cbc:ProfileExecutionID and cbc:UUID/@schemeID.</summary>
    public required DianEnvironment Environment { get; init; }

    public required DateOnly IssueDate { get; init; }

    public required TimeOnly IssueTime { get; init; }

    /// <summary>cbc:Note; only the tacit acceptance (034) carries one (php-rules 16.4, item 3).</summary>
    public string? Note { get; init; }

    /// <summary>Who sends the event: the company of the credential.</summary>
    public required EventParty Sender { get; init; }

    /// <summary>
    /// Who receives it: the seller of the invoice for 030 to 033, the DIAN for 034. Without <see cref="EventParty.LiabilityCode"/>
    /// cbc:TaxLevelCode is not written (the PHP omits it for the 034).
    /// </summary>
    public required EventParty Receiver { get; init; }

    /// <summary>The invoice the event is about.</summary>
    public required EventDocumentReference Reference { get; init; }

    /// <summary>The person who receives or decides (cac:IssuerParty/cac:Person); null writes the company's defaults.</summary>
    public EventPerson? Person { get; init; }

    /// <summary>Invoice software: its PIN is the secret of the event CUDE (php-rules 16.5).</summary>
    public required SoftwareCredentials Software { get; init; }
}

/// <summary>Claim reason of a 031: code and name of the <c>type_rejections</c> catalog.</summary>
public sealed record EventRejection(string Code, string Name);

/// <summary>A party of the event (cac:SenderParty or cac:ReceiverParty/cac:PartyTaxScheme).</summary>
public sealed record EventParty
{
    public required string Name { get; init; }

    /// <summary>NIT without check digit.</summary>
    public required string Nit { get; init; }

    public required string Dv { get; init; }

    /// <summary>Organization type code (1 legal, 2 natural person), written as schemeVersionID.</summary>
    public required string OrganizationTypeCode { get; init; }

    /// <summary>Regime code (48, 49), the listName of cbc:TaxLevelCode.</summary>
    public required string RegimeCode { get; init; }

    /// <summary>Fiscal liability codes joined by <c>;</c>; null omits cbc:TaxLevelCode.</summary>
    public string? LiabilityCode { get; init; }

    public required string TaxSchemeCode { get; init; }

    public required string TaxSchemeName { get; init; }
}

/// <summary>
/// The referenced document (cac:DocumentResponse/cac:DocumentReference): its number with prefix (the cbc:ID of the
/// invoice), its CUFE, the algorithm of that code and its type code (01, 02, 03 or 04).
/// </summary>
public sealed record EventDocumentReference(string Number, string Uuid, string UuidSchemeName, string DocumentTypeCode);

/// <summary>cac:IssuerParty/cac:Person: the person who receives the invoice or the goods, or who decides.</summary>
public sealed record EventPerson(
    string IdentificationNumber, string FirstName, string FamilyName, string JobTitle, string OrganizationDepartment);

/// <summary>The unsigned ApplicationResponse, its CUDE and its number (cbc:ID).</summary>
public sealed record RadianEventResult(string Xml, string Cude, string Number);
