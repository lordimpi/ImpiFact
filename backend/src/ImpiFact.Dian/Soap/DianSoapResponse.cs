namespace ImpiFact.Dian.Soap;

/// <summary>
/// Lo que respondió el servicio web de la DIAN (la autoridad tributaria de Colombia), ya interpretado por
/// <see cref="DianResponseReader"/>. Es una de cuatro posibilidades: <see cref="DianResultResponse"/>,
/// <see cref="DianSoapFaultResponse"/>, <see cref="DianServiceUnavailableResponse"/> o
/// <see cref="DianUnrecognizedResponse"/>.
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
/// <param name="ZipKey">Clave del envío asíncrono (SendTestSetAsync) para consultarlo con GetStatusZip.</param>
public sealed record DianResult(
    bool? IsValid,
    string? StatusCode,
    string? StatusDescription,
    string? StatusMessage,
    IReadOnlyList<string> ErrorMessages,
    string? XmlDocumentKey,
    string? XmlFileName,
    byte[]? XmlBase64Bytes,
    string? ZipKey);
