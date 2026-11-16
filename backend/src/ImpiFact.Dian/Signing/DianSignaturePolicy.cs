namespace ImpiFact.Dian.Signing;

/// <summary>
/// Datos de la política de firma v2 de la DIAN que se escriben dentro de la firma
/// (xades:SignaturePolicyIdentifier). La política es el documento PDF de la DIAN que fija las reglas de
/// la firma; la firma lo identifica por su URL y por su huella (hash).
/// </summary>
/// <remarks>
/// Fuente: Anexo Técnico 002, "Política de Firma de los Documentos XML de Facturación Electrónica -
/// Versión 2", sección 10.2. <see cref="Hash"/> es el SHA-256, en Base64, del PDF de la política publicado
/// en <see cref="Identifier"/>; se recalculó a partir del PDF descargado el 2026-11-15 y coincide con el
/// valor que envía ApiDian (la implementación de referencia en PHP).
/// </remarks>
public static class DianSignaturePolicy
{
    public const string Identifier = "https://facturaelectronica.dian.gov.co/politicadefirma/v2/politicadefirmav2.pdf";

    public const string Description = "Política de firma para facturas electrónicas de la República de Colombia.";

    public const string HashAlgorithm = "http://www.w3.org/2001/04/xmlenc#sha256";

    public const string Hash = "dMoMvtcG5aIzgYo0tIsSQeVJBDnUnfSOfBpxXrmor0Y=";

    /// <summary>
    /// Rol de quien firma (xades:ClaimedRole). Sección 12: "supplier" cuando firma el propio emisor de la factura.
    /// </summary>
    public const string SupplierRole = "supplier";
}
