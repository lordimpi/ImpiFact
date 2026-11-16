namespace ImpiFact.Dian.Signing;

/// <summary>
/// Política de firma v2 de la DIAN (Anexo Técnico 002, "Política de Firma de los Documentos XML de
/// Facturación Electrónica - Versión 2", sección 10.2). El hash es el SHA-256 del PDF de la política
/// publicado en <see cref="Identifier"/>; se recalculó a partir del PDF descargado el 2026-11-15
/// y coincide con el valor que envía ApiDian.
/// </summary>
public static class DianSignaturePolicy
{
    public const string Identifier = "https://facturaelectronica.dian.gov.co/politicadefirma/v2/politicadefirmav2.pdf";

    public const string Description = "Política de firma para facturas electrónicas de la República de Colombia.";

    public const string HashAlgorithm = "http://www.w3.org/2001/04/xmlenc#sha256";

    public const string Hash = "dMoMvtcG5aIzgYo0tIsSQeVJBDnUnfSOfBpxXrmor0Y=";

    /// <summary>Sección 12: "supplier" cuando firma el propio emisor.</summary>
    public const string SupplierRole = "supplier";
}
