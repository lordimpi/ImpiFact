namespace ImpiFact.Dian.Signing;

/// <summary>
/// DIAN signature policy v2 (Anexo Técnico 002, "Política de Firma de los Documentos XML de
/// Facturación Electrónica - Versión 2", section 10.2). The hash is the SHA-256 of the policy PDF
/// published at <see cref="Identifier"/>; it was recomputed from the downloaded PDF on 2026-11-15
/// and matches the value ApiDian sends.
/// </summary>
public static class DianSignaturePolicy
{
    public const string Identifier = "https://facturaelectronica.dian.gov.co/politicadefirma/v2/politicadefirmav2.pdf";

    public const string Description = "Política de firma para facturas electrónicas de la República de Colombia.";

    public const string HashAlgorithm = "http://www.w3.org/2001/04/xmlenc#sha256";

    public const string Hash = "dMoMvtcG5aIzgYo0tIsSQeVJBDnUnfSOfBpxXrmor0Y=";

    /// <summary>Section 12: "supplier" when the issuer itself signs.</summary>
    public const string SupplierRole = "supplier";
}
