namespace ImpiFact.Dian.Attached;

/// <summary>
/// Nombre del archivo del AttachedDocument (contenedor electrónico que se entrega al comprador).
/// </summary>
public static class AttachedDocumentFileName
{
    private static readonly string[] DocumentPrefixes = ["fv", "nc", "nd"];

    /// <summary>
    /// Devuelve el XmlFileName que respondió la DIAN con su prefijo <c>fv</c> (factura), <c>nc</c> (nota
    /// crédito) o <c>nd</c> (nota débito) cambiado por <c>ad</c>; por ejemplo
    /// <c>fv09000000010002600000001</c> queda <c>ad09000000010002600000001</c>. Lo demás, incluida la
    /// extensión si la trae, no cambia.
    /// </summary>
    /// <remarks>
    /// ApiDian (la implementación de referencia en PHP) reemplaza <c>fv</c>, <c>nc</c> y <c>nd</c> en
    /// cualquier parte del nombre (InvoiceController, línea 617). Los nombres de la DIAN solo los llevan al
    /// inicio y el resto son dígitos, así que el resultado es el mismo.
    /// </remarks>
    public static string FromDianXmlFileName(string dianXmlFileName)
    {
        ArgumentNullException.ThrowIfNull(dianXmlFileName);
        if (!DocumentPrefixes.Any(prefix => dianXmlFileName.StartsWith(prefix, StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                $"The DIAN file name '{dianXmlFileName}' does not start with fv, nc or nd.", nameof(dianXmlFileName));
        }

        return string.Concat("ad", dianXmlFileName.AsSpan(2));
    }
}
