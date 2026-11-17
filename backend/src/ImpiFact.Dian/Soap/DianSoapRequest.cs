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
