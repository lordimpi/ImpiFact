using System.Text;

namespace ImpiFact.Integration.Tests.DianSend;

/// <summary>
/// SOAP answers of the simulated DIAN for the batches (phase 7a, T8), with the shape of annex 1.9: SendBillAsync answers the
/// UploadDocumentResponse contract (section 7.8.3: the ZipKey and one XmlParamsResponseTrackId per file that failed the
/// initial validations) and GetStatusZip one DianResponse per document of the ZIP (section 7.12.3), each with its code
/// (XmlDocumentKey), its file name (without extension) and its own ApplicationResponse. The real DIAN is never called.
/// </summary>
public static class BatchReplies
{
    public const string InProcessDescription = "Batch en proceso de validación.";

    public static string SendBillAsync(string zipKey, params (string File, string Message)[] errors) =>
        UploadAnswer($"<b:ZipKey>{zipKey}</b:ZipKey>", errors);

    /// <summary>SendBillAsync refused the whole ZIP: messages and no ZipKey.</summary>
    public static string SendBillAsyncWithoutZipKey(params (string File, string Message)[] errors) =>
        UploadAnswer("<b:ZipKey i:nil=\"true\"/>", errors);

    public static string GetStatusZip(params BatchVerdict[] verdicts) => Envelope("GetStatusZip", $"""
        <GetStatusZipResponse xmlns="http://wcf.dian.colombia"><GetStatusZipResult xmlns:b="http://schemas.datacontract.org/2004/07/DianResponse" xmlns:i="http://www.w3.org/2001/XMLSchema-instance">{string.Concat(verdicts.Select(Response))}</GetStatusZipResult></GetStatusZipResponse>
        """);

    /// <summary>The DIAN still validates the ZIP: one DianResponse without a document.</summary>
    public static string GetStatusZipInProcess() => GetStatusZip(new BatchVerdict(null, null, false, "98") { Description = InProcessDescription });

    private static string UploadAnswer(string zipKey, (string File, string Message)[] errors)
    {
        var list = errors.Length == 0
            ? "<b:ErrorMessageList xmlns:c=\"http://schemas.datacontract.org/2004/07/XmlParamsResponseTrackId\"/>"
            : "<b:ErrorMessageList xmlns:c=\"http://schemas.datacontract.org/2004/07/XmlParamsResponseTrackId\">" + string.Concat(errors.Select(error =>
                $"<c:XmlParamsResponseTrackId><c:DocumentKey i:nil=\"true\"/><c:ProcessedMessage>{error.Message}</c:ProcessedMessage><c:Success>false</c:Success><c:XmlFileName>{error.File}</c:XmlFileName></c:XmlParamsResponseTrackId>")) +
              "</b:ErrorMessageList>";
        return Envelope("SendBillAsync", $"""
            <SendBillAsyncResponse xmlns="http://wcf.dian.colombia"><SendBillAsyncResult xmlns:b="http://schemas.datacontract.org/2004/07/UploadDocumentResponse" xmlns:i="http://www.w3.org/2001/XMLSchema-instance">{list}{zipKey}</SendBillAsyncResult></SendBillAsyncResponse>
            """);
    }

    private static string Response(BatchVerdict verdict)
    {
        var messages = string.Concat(verdict.Errors.Select(error => $"<c:string>{error}</c:string>"));
        var applicationResponse = verdict.StatusCode != "98"
            ? $"<b:XmlBase64Bytes>{Convert.ToBase64String(Encoding.UTF8.GetBytes(DianReplies.ApplicationResponseXml))}</b:XmlBase64Bytes>"
            : "<b:XmlBase64Bytes i:nil=\"true\"/>";
        var description = verdict.Description ?? verdict.StatusCode switch
        {
            "00" => "Procesado Correctamente.",
            _ => "Validación contiene errores en campos mandatorios.",
        };
        var key = verdict.DocumentKey is null ? "<b:XmlDocumentKey i:nil=\"true\"/>" : $"<b:XmlDocumentKey>{verdict.DocumentKey}</b:XmlDocumentKey>";
        var file = verdict.FileName is null ? "<b:XmlFileName i:nil=\"true\"/>" : $"<b:XmlFileName>{verdict.FileName}</b:XmlFileName>";
        return $"""<b:DianResponse><b:ErrorMessage xmlns:c="http://schemas.microsoft.com/2003/10/Serialization/Arrays">{messages}</b:ErrorMessage><b:IsValid>{(verdict.IsValid ? "true" : "false")}</b:IsValid><b:StatusCode>{verdict.StatusCode}</b:StatusCode><b:StatusDescription>{description}</b:StatusDescription><b:StatusMessage i:nil="true"/>{applicationResponse}<b:XmlBytes i:nil="true"/>{key}{file}</b:DianResponse>""";
    }

    private static string Envelope(string operation, string body) => $"""
        <s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope" xmlns:a="http://www.w3.org/2005/08/addressing"><s:Header><a:Action s:mustUnderstand="1">http://wcf.dian.colombia/IWcfDianCustomerServices/{operation}Response</a:Action></s:Header><s:Body>{body}</s:Body></s:Envelope>
        """;
}

/// <summary>The verdict of one document in a GetStatusZip answer.</summary>
/// <param name="DocumentKey">Its CUFE or CUDE (XmlDocumentKey); null to name it only by its file.</param>
/// <param name="FileName">Its XML inside the ZIP without extension (XmlFileName).</param>
public sealed record BatchVerdict(string? DocumentKey, string? FileName, bool IsValid, string StatusCode, params string[] Errors)
{
    public string? Description { get; init; }
}
