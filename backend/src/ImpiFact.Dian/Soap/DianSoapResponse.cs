namespace ImpiFact.Dian.Soap;

/// <summary>
/// Lo que respondió el servicio web de la DIAN (la autoridad tributaria de Colombia), ya interpretado por
/// <see cref="DianResponseReader"/>. Es una de siete posibilidades: <see cref="DianResultResponse"/>, <see cref="DianXmlDocumentResponse"/>,
/// <see cref="DianNumberingRangeResponse"/>, <see cref="DianAcquirerResponse"/>, <see cref="DianSoapFaultResponse"/>,
/// <see cref="DianServiceUnavailableResponse"/> o <see cref="DianUnrecognizedResponse"/>.
/// </summary>
public abstract record DianSoapResponse
{
    private protected DianSoapResponse()
    {
    }
}

/// <summary>
/// Respuesta normal de una operación: el nombre de la operación (por ejemplo <c>SendBillSync</c>) y sus
/// resultados. SendBillSync, SendTestSetAsync y GetStatus traen un resultado; GetStatusZip trae uno por
/// documento del ZIP (archivo comprimido) enviado.
/// </summary>
public sealed record DianResultResponse(string OperationName, IReadOnlyList<DianResult> Results) : DianSoapResponse;

/// <summary>
/// Respuesta de GetNumberingRange: los rangos de numeración que la DIAN tiene autorizados para el emisor y el software
/// consultados. <paramref name="OperationCode"/> es <c>100</c> cuando la consulta se completó; con otro código
/// (por ejemplo, un NIT o un software no autorizados) <paramref name="Ranges"/> viene vacía y
/// <paramref name="OperationDescription"/> explica el motivo.
/// </summary>
public sealed record DianNumberingRangeResponse(
    string? OperationCode, string? OperationDescription, IReadOnlyList<DianNumberingRange> Ranges) : DianSoapResponse;

/// <summary>
/// Un rango de numeración autorizado por la DIAN. Los campos que la respuesta no trae, o trae vacíos o nulos, quedan
/// en <see langword="null"/>.
/// </summary>
/// <param name="ResolutionNumber">Número de la resolución de la DIAN que autoriza el rango.</param>
/// <param name="Prefix">Prefijo de los documentos del rango.</param>
/// <param name="FromNumber">Primer número autorizado.</param>
/// <param name="ToNumber">Último número autorizado.</param>
/// <param name="ValidDateFrom">Primer día de vigencia.</param>
/// <param name="ValidDateTo">Último día de vigencia.</param>
/// <param name="TechnicalKey">
/// Clave técnica del rango (entra en el CUFE, el código único de la factura electrónica); nula en los rangos que no
/// la tienen, como los de notas.
/// </param>
public sealed record DianNumberingRange(
    string? ResolutionNumber,
    DateOnly? ResolutionDate,
    string? Prefix,
    long? FromNumber,
    long? ToNumber,
    DateOnly? ValidDateFrom,
    DateOnly? ValidDateTo,
    string? TechnicalKey);

/// <summary>
/// Respuesta de GetAcquirer: lo que la DIAN tiene registrado del adquiriente consultado. <paramref name="StatusCode"/> es
/// <c>200</c> cuando lo encontró y <c>404</c> cuando no (el PHP solo distingue esos dos); <paramref name="Message"/>
/// explica el resultado. <paramref name="ReceiverName"/> y <paramref name="ReceiverEmail"/> son el nombre y el correo de
/// recepción de facturas; quedan en <see langword="null"/> si no vienen o vienen vacíos.
/// </summary>
public sealed record DianAcquirerResponse(string? StatusCode, string? Message, string? ReceiverName, string? ReceiverEmail) : DianSoapResponse;

/// <summary>
/// Answer of GetXmlByDocumentKey (Anexo Técnico 1.9, section 7.14.3): <paramref name="Code"/> <c>100</c> when the document
/// was found, <c>205</c> when it was not, <c>206</c> for an XML with errors in the issuer or receiver number and <c>401</c> when
/// the certificate is not the issuer's or the receiver's. <paramref name="Xml"/> holds the document bytes, already decoded
/// from Base64; null when the answer brings none.
/// </summary>
public sealed record DianXmlDocumentResponse(string? Code, string? Message, byte[]? Xml) : DianSoapResponse;

/// <summary>
/// Error SOAP (soap:Fault): el servicio no procesó la operación, por ejemplo porque la firma del sobre no es
/// válida. <paramref name="Code"/> y <paramref name="Subcode"/> son los códigos tal como vienen (por ejemplo
/// <c>s:Sender</c> y <c>a:InvalidSecurity</c>) y <paramref name="Reason"/> el texto del motivo.
/// </summary>
public sealed record DianSoapFaultResponse(string Code, string? Subcode, string Reason) : DianSoapResponse;

/// <summary>
/// El cuerpo de la respuesta no es XML (por ejemplo una página HTML de error) o está vacío: el servicio de la
/// DIAN no está disponible. ApiDian (la implementación de referencia en PHP) responde en este caso «El servicio
/// DIAN no se encuentra disponible en el momento, reintente mas tarde».
/// </summary>
public sealed record DianServiceUnavailableResponse(string Body) : DianSoapResponse;

/// <summary>
/// XML bien formado pero sin un resultado conocido: no es un sobre SOAP, es una operación que este lector no
/// interpreta, o algún valor no se puede leer (por ejemplo un Base64 inválido).
/// </summary>
public sealed record DianUnrecognizedResponse(string Body) : DianSoapResponse;

/// <summary>
/// Un resultado de la DIAN sobre un documento o un envío. Los campos que la respuesta no trae, o trae vacíos
/// o nulos (<c>i:nil="true"</c>), quedan en <see langword="null"/>.
/// </summary>
/// <param name="IsValid">Si la DIAN aceptó el documento.</param>
/// <param name="StatusCode">Código de estado, por ejemplo <c>00</c> (procesado), <c>99</c> (con errores) o <c>98</c> (en proceso).</param>
/// <param name="StatusDescription">Descripción del estado.</param>
/// <param name="StatusMessage">Mensaje del estado.</param>
/// <param name="ErrorMessages">
/// Reglas de validación rechazadas o notificadas (ErrorMessage), o los mensajes procesados de la lista de
/// errores de SendTestSetAsync (ErrorMessageList); vacía si no hay.
/// </param>
/// <param name="XmlDocumentKey">CUFE o CUDE del documento (código único de la factura o de la nota).</param>
/// <param name="XmlFileName">Nombre del archivo del documento, sin extensión.</param>
/// <param name="XmlBase64Bytes">
/// Bytes del ApplicationResponse (el XML de respuesta firmado por la DIAN), ya decodificados del Base64.
/// </param>
/// <param name="ZipKey">Clave del envío asíncrono (SendTestSetAsync o SendBillAsync) para consultarlo con GetStatusZip.</param>
/// <param name="FileMessages">
/// The initial-validation messages of an asynchronous send (SendTestSetAsync, SendBillAsync) with the file each one names
/// (<c>XmlParamsResponseTrackId</c>, annex 1.9, section 7.8.3); empty when there are none and null in the answers of the
/// other operations. Their texts are also in
/// <paramref name="ErrorMessages"/>.
/// </param>
public sealed record DianResult(
    bool? IsValid,
    string? StatusCode,
    string? StatusDescription,
    string? StatusMessage,
    IReadOnlyList<string> ErrorMessages,
    string? XmlDocumentKey,
    string? XmlFileName,
    byte[]? XmlBase64Bytes,
    string? ZipKey,
    IReadOnlyList<DianFileMessage>? FileMessages = null);

/// <summary>An initial-validation message of an asynchronous send and the XML file it names (without extension, as the DIAN writes it).</summary>
public sealed record DianFileMessage(string? FileName, string Message);
