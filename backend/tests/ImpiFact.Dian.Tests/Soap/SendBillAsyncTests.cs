using System.IO.Compression;
using System.Text;
using System.Xml;
using ImpiFact.Dian.Soap;
using ImpiFact.Dian.Tests.Signing;

namespace ImpiFact.Dian.Tests.Soap;

/// <summary>
/// The batch pieces of the DIAN layer (phase 7a, T8): the SendBillAsync request (annex 1.9, section 7.8.2: <c>fileName</c>,
/// the ZIP name, and <c>contentFile</c>), its answer with the ZipKey and the initial-validation messages per file (7.8.3)
/// and the ZIP with several signed XML (section 6.8: «la cantidad de documentos electrónicos será inferior a 51»).
/// </summary>
public sealed class SendBillAsyncTests
{
    private const string ZipKey = "8b1849d4-884b-4245-9094-08e76be927a3";

    private static readonly DateTimeOffset ModifiedAt = new(2026, 3, 2, 8, 0, 0, TimeSpan.FromHours(-5));

    /// <summary>The answer as the annex writes it (7.8.3): lowercase element names in the UploadDocumentResponse contract.</summary>
    private static readonly string AnnexAnswer = Envelope($"""
        <SendBillAsyncResponse xmlns="http://wcf.dian.colombia"><SendBillAsyncResult xmlns:b="http://schemas.datacontract.org/2004/07/UploadDocumentResponse" xmlns:i="http://www.w3.org/2001/XMLSchema-instance"><b:errorMessageList><b:XmlParamsResponseTrackId><b:processedMessage>Tipo de documento 2 no implementado.</b:processedMessage><b:xmlFileName>nc09000000010002600000001</b:xmlFileName></b:XmlParamsResponseTrackId></b:errorMessageList><b:zipKey>{ZipKey}</b:zipKey></SendBillAsyncResult></SendBillAsyncResponse>
        """);

    /// <summary>The same contract as SendTestSetAsync answers it (annex 7.9.3): capitalized names and an empty list.</summary>
    private static readonly string CapitalizedAnswer = Envelope($"""
        <SendBillAsyncResponse xmlns="http://wcf.dian.colombia"><SendBillAsyncResult xmlns:b="http://schemas.datacontract.org/2004/07/UploadDocumentResponse" xmlns:i="http://www.w3.org/2001/XMLSchema-instance"><b:ErrorMessageList i:nil="true" xmlns:c="http://schemas.datacontract.org/2004/07/XmlParamsResponseTrackId"/><b:ZipKey>{ZipKey}</b:ZipKey></SendBillAsyncResult></SendBillAsyncResponse>
        """);

    [Test]
    public void The_request_sends_the_zip_name_and_its_content_in_the_order_of_the_annex()
    {
        var request = new SendBillAsyncRequest("z09000000010002600000001.zip", "UEsDBAo=");

        var envelope = new XmlDocument();
        envelope.LoadXml(DianSoapEnvelope.Build(request, "https://vpfe-hab.dian.gov.co/WcfDianCustomerServices.svc", TestCertificates.Signer, ModifiedAt));
        var operation = envelope.GetElementsByTagName("SendBillAsync", DianSoapRequest.ServiceNamespace).Cast<XmlElement>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(request.Action, Is.EqualTo("http://wcf.dian.colombia/IWcfDianCustomerServices/SendBillAsync"));
            Assert.That(operation.ChildNodes.OfType<XmlElement>().Select(child => (child.LocalName, child.InnerText)), Is.EqualTo(new[]
            {
                ("fileName", "z09000000010002600000001.zip"), ("contentFile", "UEsDBAo="),
            }));
        });
    }

    [Test]
    public void The_annex_answer_gives_the_zip_key_and_the_message_of_each_file()
    {
        var result = Single(AnnexAnswer);

        Assert.Multiple(() =>
        {
            Assert.That(result.ZipKey, Is.EqualTo(ZipKey));
            Assert.That(result.ErrorMessages, Is.EqualTo(new[] { "Tipo de documento 2 no implementado." }));
            Assert.That(result.FileMessages, Is.EqualTo(new[] { new DianFileMessage("nc09000000010002600000001", "Tipo de documento 2 no implementado.") }));
            Assert.That(result.IsValid, Is.Null);
        });
    }

    [Test]
    public void The_capitalized_answer_gives_the_zip_key_without_messages()
    {
        var result = Single(CapitalizedAnswer);

        Assert.Multiple(() =>
        {
            Assert.That(result.ZipKey, Is.EqualTo(ZipKey));
            Assert.That(result.ErrorMessages, Is.Empty);
            Assert.That(result.FileMessages, Is.Empty);
        });
    }

    [Test]
    public void The_zip_holds_every_entry_with_its_name_and_its_exact_bytes_in_order()
    {
        byte[] invoice = Encoding.UTF8.GetBytes("<Invoice>ñ</Invoice>");
        byte[] note = Encoding.UTF8.GetBytes("<CreditNote/>");

        var zip = DianZip.Create([new DianZipEntry("fv09000000010002600000001.xml", invoice), new DianZipEntry("nc09000000010002600000001.xml", note)], ModifiedAt);

        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        Assert.Multiple(() =>
        {
            Assert.That(archive.Entries.Select(entry => entry.FullName), Is.EqualTo(new[] { "fv09000000010002600000001.xml", "nc09000000010002600000001.xml" }));
            Assert.That(Read(archive.Entries[0]), Is.EqualTo(invoice));
            Assert.That(Read(archive.Entries[1]), Is.EqualTo(note));
            Assert.That(DianZip.Create([new DianZipEntry("fv09000000010002600000001.xml", invoice), new DianZipEntry("nc09000000010002600000001.xml", note)], ModifiedAt),
                Is.EqualTo(zip), "The same entries give the same bytes.");
        });
    }

    [Test]
    public void A_zip_without_entries_or_with_a_repeated_name_is_refused()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => DianZip.Create([], ModifiedAt));
            Assert.Throws<ArgumentException>(() => DianZip.Create([new DianZipEntry("fv.xml", [1]), new DianZipEntry("fv.xml", [2])], ModifiedAt));
        });
    }

    private static DianResult Single(string body)
    {
        var response = DianResponseReader.Read(body);
        Assert.That(response, Is.InstanceOf<DianResultResponse>());
        var results = (DianResultResponse)response;
        Assert.That(results.OperationName, Is.EqualTo("SendBillAsync"));
        return results.Results.Single();
    }

    private static byte[] Read(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static string Envelope(string body) => $"""
        <s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope" xmlns:a="http://www.w3.org/2005/08/addressing"><s:Header><a:Action s:mustUnderstand="1">http://wcf.dian.colombia/IWcfDianCustomerServices/SendBillAsyncResponse</a:Action></s:Header><s:Body>{body}</s:Body></s:Envelope>
        """;
}
