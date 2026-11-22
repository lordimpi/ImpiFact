using System.Globalization;

namespace ImpiFact.Dian.Soap;

/// <summary>Tipo de documento electrónico, para el prefijo del nombre de su archivo XML.</summary>
public enum DianFileKind
{
    /// <summary>Factura electrónica de venta (prefijo <c>fv</c>).</summary>
    Invoice,

    /// <summary>Nota crédito (prefijo <c>nc</c>).</summary>
    CreditNote,

    /// <summary>Nota débito (prefijo <c>nd</c>).</summary>
    DebitNote,

    /// <summary>Support document (prefix <c>dse</c>, type_documents 11).</summary>
    SupportDocument,

    /// <summary>Adjustment note to a support document (prefix <c>nds</c>, type_documents 13).</summary>
    SupportDocumentAdjustmentNote,
}

/// <summary>
/// Nombres de archivo que exige la DIAN (la autoridad tributaria de Colombia) para los documentos que se le
/// envían: el XML firmado, el ZIP (archivo comprimido) que lo contiene y el nombre que va en el parámetro
/// <c>fileName</c> de la operación SOAP (protocolo de mensajes XML del servicio web).
/// </summary>
/// <remarks>
/// El nombre del XML y del ZIP es: prefijo, NIT (número de identificación tributaria del emisor, sin dígito
/// de verificación) con ceros a la izquierda hasta 10 dígitos, el código fijo <c>000</c>, los dos últimos
/// dígitos del año y un consecutivo de archivos con ceros a la izquierda hasta 8 dígitos. Por ejemplo
/// <c>fv09018462800002500000166.xml</c>: NIT 901846280, año 2025, consecutivo 166. ApiDian (la implementación
/// de referencia en PHP) agrega al ZIP un sufijo aleatorio; aquí no, para que el nombre sea predecible.
/// </remarks>
public static class DianFileNames
{
    private const int NitLength = 10;
    private const long MaxCounter = 99_999_999;

    /// <summary>
    /// Nombre del XML firmado dentro del ZIP, por ejemplo <c>fv09018462800002500000166.xml</c>.
    /// </summary>
    /// <param name="kind">Tipo de documento: define el prefijo <c>fv</c>, <c>nc</c> o <c>nd</c>.</param>
    /// <param name="nit">NIT del emisor, solo dígitos y sin dígito de verificación (hasta 10 dígitos).</param>
    /// <param name="year">Año del envío; se escriben sus dos últimos dígitos.</param>
    /// <param name="counter">Consecutivo de archivos del emisor (0 a 99.999.999).</param>
    public static string XmlEntry(DianFileKind kind, string nit, int year, long counter) =>
        Compose(Prefix(kind), nit, year, counter) + ".xml";

    /// <summary>
    /// Prefix of the XML name of each kind (<c>type_documents.prefix</c>): <c>fv</c>, <c>nc</c>, <c>nd</c>, <c>dse</c> or
    /// <c>nds</c>. The file name counter is kept per prefix, so each kind numbers its files on its own.
    /// </summary>
    public static string Prefix(DianFileKind kind) => kind switch
    {
        DianFileKind.Invoice => "fv",
        DianFileKind.CreditNote => "nc",
        DianFileKind.DebitNote => "nd",
        DianFileKind.SupportDocument => "dse",
        DianFileKind.SupportDocumentAdjustmentNote => "nds",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown DIAN file kind."),
    };

    /// <summary>
    /// Nombre del ZIP que contiene el XML: la misma regla con el prefijo <c>z</c>, por ejemplo
    /// <c>z09018462800002500000166.zip</c>.
    /// </summary>
    public static string Zip(string nit, int year, long counter) => Compose("z", nit, year, counter) + ".zip";

    /// <summary>
    /// Nombre que va en el parámetro <c>fileName</c> de la operación SOAP: el prefijo de la resolución de
    /// numeración y el número del documento, por ejemplo <c>SETP990000001.xml</c>, como lo envía ApiDian.
    /// </summary>
    /// <param name="documentPrefix">Prefijo de la resolución; puede ser vacío o nulo.</param>
    /// <param name="number">Número del documento.</param>
    public static string SoapFileName(string? documentPrefix, long number) =>
        (documentPrefix ?? string.Empty) + number.ToString(CultureInfo.InvariantCulture) + ".xml";

    /// <summary>
    /// Name of the XML of a RADIAN event inside its ZIP, as ApiDian's <c>getFileNameSendEvent</c> (php-rules 16.6, item 2):
    /// <c>ar</c>, the event code, the NIT of the sender padded to 10 digits, <c>000</c>, the two last digits of the year and
    /// the number of the referenced document padded to 8 characters, for example <c>ar030080000000200026FPRU1001.xml</c>.
    /// The PHP writes the prefix twice for a received invoice (its number already carries it); here the number is written
    /// once, and only its letters and digits.
    /// </summary>
    public static string EventXmlEntry(string eventCode, string nit, int year, string documentNumber) =>
        "ar" + EventStem(eventCode, nit, year, documentNumber) + ".xml";

    /// <summary>Name of the ZIP of a RADIAN event: the same rule with the prefix <c>z</c>.</summary>
    public static string EventZip(string eventCode, string nit, int year, string documentNumber) =>
        "z" + EventStem(eventCode, nit, year, documentNumber) + ".zip";

    private static string EventStem(string eventCode, string nit, int year, string documentNumber)
    {
        ArgumentException.ThrowIfNullOrEmpty(eventCode);
        ArgumentNullException.ThrowIfNull(documentNumber);
        var number = new string(documentNumber.Where(char.IsAsciiLetterOrDigit).ToArray());
        // Compose checks the NIT and the year; its counter slot is unused here (0), so it is cut and the number goes in.
        var head = Compose(string.Empty, nit, year, 0)[..^8];
        return eventCode + head + number.PadLeft(8, '0');
    }

    private static string Compose(string prefix, string nit, int year, long counter)
    {
        ArgumentNullException.ThrowIfNull(nit);
        if (nit.Length is 0 or > NitLength || !nit.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("The NIT must have between 1 and 10 digits, without check digit.", nameof(nit));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(year);
        ArgumentOutOfRangeException.ThrowIfNegative(counter);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(counter, MaxCounter);

        return string.Create(CultureInfo.InvariantCulture,
            $"{prefix}{nit.PadLeft(NitLength, '0')}000{year % 100:D2}{counter:D8}");
    }
}
