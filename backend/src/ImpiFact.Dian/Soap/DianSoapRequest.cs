namespace ImpiFact.Dian.Soap;

/// <summary>
/// Datos de una operación del servicio web de la DIAN (la autoridad tributaria de Colombia), que se envían
/// dentro del cuerpo de un sobre SOAP (protocolo de mensajes XML de servicios web) armado por
/// <see cref="DianSoapEnvelope"/>.
/// </summary>
/// <remarks>
/// El servicio de la DIAN está hecho con WCF (el marco de servicios web de Microsoft). Cada operación es un
/// elemento <c>wcf:&lt;Operación&gt;</c> del cuerpo, y sus parámetros son elementos hijos con los nombres
/// exactos del contrato del servicio, en el orden de ese contrato.
/// </remarks>
public abstract record DianSoapRequest
{
    /// <summary>Espacio de nombres XML de las operaciones del servicio de la DIAN.</summary>
    public const string ServiceNamespace = "http://wcf.dian.colombia";

    private const string ActionPrefix = ServiceNamespace + "/IWcfDianCustomerServices/";

    private protected DianSoapRequest()
    {
    }

    /// <summary>Nombre de la operación en el contrato del servicio, por ejemplo <c>SendBillSync</c>.</summary>
    public abstract string OperationName { get; }

    /// <summary>
    /// Acción de WS-Addressing (estándar que pone en el encabezado del sobre la acción y el destino):
    /// identifica la operación que se invoca.
    /// </summary>
    public string Action => ActionPrefix + OperationName;

    /// <summary>Parámetros de la operación (nombre del elemento y texto) en el orden del contrato.</summary>
    internal abstract IReadOnlyList<KeyValuePair<string, string>> Parameters { get; }

    private protected static KeyValuePair<string, string> Parameter(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(value, name);
        return new(name, value);
    }
}

/// <summary>
/// Envío de un documento en producción con respuesta inmediata (SendBillSync). <paramref name="FileName"/> es
/// el nombre del documento (ver <see cref="DianFileNames.SoapFileName"/>) y <paramref name="ContentFile"/> el
/// ZIP (archivo comprimido) con el XML firmado, en Base64 (texto que representa bytes; ver <see cref="DianZip"/>).
/// </summary>
public sealed record SendBillSyncRequest(string FileName, string ContentFile) : DianSoapRequest
{
    public override string OperationName => "SendBillSync";

    internal override IReadOnlyList<KeyValuePair<string, string>> Parameters =>
        [Parameter("fileName", FileName), Parameter("contentFile", ContentFile)];
}

/// <summary>
/// Envío de un documento del set de pruebas de habilitación (SendTestSetAsync). La DIAN no responde el
/// resultado sino una clave (ZipKey) para consultarlo después con <see cref="GetStatusZipRequest"/>.
/// <paramref name="TestSetId"/> es el identificador del set de pruebas que asigna la DIAN.
/// </summary>
public sealed record SendTestSetAsyncRequest(string FileName, string ContentFile, string TestSetId) : DianSoapRequest
{
    public override string OperationName => "SendTestSetAsync";

    internal override IReadOnlyList<KeyValuePair<string, string>> Parameters =>
        [Parameter("fileName", FileName), Parameter("contentFile", ContentFile), Parameter("testSetId", TestSetId)];
}

/// <summary>
/// Sending of a batch (SendBillAsync, annex 1.9, section 7.8): <paramref name="FileName"/> is the name of the ZIP
/// (<see cref="DianFileNames.Zip"/>, at most 100 characters) and <paramref name="ContentFile"/> the ZIP with up to 50 signed
/// documents, in Base64 (at most 50 MB). The DIAN answers a ZipKey; the verdicts come later from
/// <see cref="GetStatusZipRequest"/>.
/// </summary>
public sealed record SendBillAsyncRequest(string FileName, string ContentFile) : DianSoapRequest
{
    public override string OperationName => "SendBillAsync";

    internal override IReadOnlyList<KeyValuePair<string, string>> Parameters =>
        [Parameter("fileName", FileName), Parameter("contentFile", ContentFile)];
}

/// <summary>
/// Consulta del estado de un documento (GetStatus) por su CUFE (código único de factura electrónica) o su
/// CUDE (código único de documento electrónico, el de las notas).
/// </summary>
public sealed record GetStatusRequest(string TrackId) : DianSoapRequest
{
    public override string OperationName => "GetStatus";

    internal override IReadOnlyList<KeyValuePair<string, string>> Parameters => [Parameter("trackId", TrackId)];
}

/// <summary>Consulta del estado de un envío asíncrono por la ZipKey que devolvió la DIAN (GetStatusZip).</summary>
public sealed record GetStatusZipRequest(string TrackId) : DianSoapRequest
{
    public override string OperationName => "GetStatusZip";

    internal override IReadOnlyList<KeyValuePair<string, string>> Parameters => [Parameter("trackId", TrackId)];
}

/// <summary>
/// Consulta de los rangos de numeración autorizados (GetNumberingRange): NIT (número de identificación
/// tributaria) del emisor
/// (<paramref name="AccountCode"/>), NIT del proveedor tecnológico (<paramref name="AccountCodeT"/>) y el
/// identificador del software ante la DIAN (<paramref name="SoftwareCode"/>).
/// </summary>
public sealed record GetNumberingRangeRequest(string AccountCode, string AccountCodeT, string SoftwareCode) : DianSoapRequest
{
    public override string OperationName => "GetNumberingRange";

    internal override IReadOnlyList<KeyValuePair<string, string>> Parameters =>
        [Parameter("accountCode", AccountCode), Parameter("accountCodeT", AccountCodeT), Parameter("softwareCode", SoftwareCode)];
}

/// <summary>
/// Consulta de un adquiriente (GetAcquirer): con el tipo y el número de identificación de un comprador, la DIAN devuelve
/// el nombre y el correo de recepción de facturas que tiene registrados. <paramref name="IdentificationType"/> es el
/// código DIAN del tipo de identificación (por ejemplo <c>31</c> para NIT o <c>13</c> para cédula de ciudadanía), no el id
/// del catálogo; <paramref name="IdentificationNumber"/> va sin dígito de verificación. El sobre es el de
/// <c>GetAdquirerRequest.php</c> del PHP.
/// </summary>
public sealed record GetAcquirerRequest(string IdentificationType, string IdentificationNumber) : DianSoapRequest
{
    public override string OperationName => "GetAcquirer";

    internal override IReadOnlyList<KeyValuePair<string, string>> Parameters =>
        [Parameter("identificationType", IdentificationType), Parameter("identificationNumber", IdentificationNumber)];
}

/// <summary>
/// Registration of a RADIAN event (SendEventUpdateStatus, Anexo Técnico 1.9, section 7.13): the ZIP with exactly one
/// signed ApplicationResponse, in Base64. The operation has no <c>fileName</c>; the name only travels inside the ZIP (see
/// <see cref="DianFileNames.EventXmlEntry"/>). The DIAN answers synchronously with a <see cref="DianResult"/>.
/// </summary>
public sealed record SendEventUpdateStatusRequest(string ContentFile) : DianSoapRequest
{
    public override string OperationName => "SendEventUpdateStatus";

    internal override IReadOnlyList<KeyValuePair<string, string>> Parameters => [Parameter("contentFile", ContentFile)];
}

/// <summary>
/// Download of an electronic document by its CUFE (GetXmlByDocumentKey, Anexo Técnico 1.9, section 7.14). The DIAN only
/// answers to the issuer or the receiver of the document, identified by the certificate; the answer is a
/// <see cref="DianXmlDocumentResponse"/>.
/// </summary>
public sealed record GetXmlByDocumentKeyRequest(string TrackId) : DianSoapRequest
{
    public override string OperationName => "GetXmlByDocumentKey";

    internal override IReadOnlyList<KeyValuePair<string, string>> Parameters => [Parameter("trackId", TrackId)];
}

/// <summary>
/// The events registered on an invoice (GetStatusEvent, Anexo Técnico 1.9, section 7.17), by the CUFE of the invoice. The
/// answer is a <see cref="DianResult"/> whose ApplicationResponse lists the events (see
/// <see cref="Radian.RadianEventStatusReader"/>).
/// </summary>
public sealed record GetStatusEventRequest(string TrackId) : DianSoapRequest
{
    public override string OperationName => "GetStatusEvent";

    internal override IReadOnlyList<KeyValuePair<string, string>> Parameters => [Parameter("trackId", TrackId)];
}
