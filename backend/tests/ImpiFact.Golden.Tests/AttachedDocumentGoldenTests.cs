using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ImpiFact.Dian.Attached;
using ImpiFact.Dian.Cufe;
using ImpiFact.Dian.Signing;
using static ImpiFact.Golden.Tests.ApiDianJson;

namespace ImpiFact.Golden.Tests;

/// <summary>
/// Casos de referencia del AttachedDocument (contenedor electrónico que el emisor entrega al comprador con el
/// documento firmado y el ApplicationResponse, la respuesta de validación de la DIAN). ApiDian (el PHP
/// original) los generó con tools/golden-capture/attached_document.php a partir de signed-document.xml, de un
/// application-response.xml inventado y de input.json; ImpiFact debe producir los mismos bytes que
/// expected-attached-document.xml, que es el XML firmado por ApiDian sin su ds:Signature.
/// </summary>
/// <remarks>
/// Ajustes al comparar, y solo en esos puntos: el lugar de la firma vacío se escribe
/// <c>&lt;ext:ExtensionContent/&gt;</c> antes de firmar (ver <see cref="InvoiceGoldenTests"/>), y en las
/// notas cbc:UUID usa schemeName="CUDE-SHA384" como pide el anexo técnico, donde el PHP escribe CUFE-SHA384.
/// </remarks>
public sealed class AttachedDocumentGoldenTests
{
    private static readonly ApiDianCatalog Catalog = ApiDianCatalog.Load();

    // Hora de Colombia (UTC-5), la zona de ApiDian (America/Bogota), en la que está escrito "now" en input.json.
    private static readonly TimeSpan ColombiaUtcOffset = TimeSpan.FromHours(-5);

    private static readonly Regex SignatureRegex = new("<ds:Signature\\b.*?</ds:Signature>", RegexOptions.Singleline);

    public static IEnumerable<TestCaseData> Cases() =>
        GoldenCases.AttachedDocuments().Select(dir => new TestCaseData(dir).SetArgDisplayNames("attached-document/" + Path.GetFileName(dir)));

    [TestCaseSource(nameof(Cases))]
    public void Builder_matches_ApiDian_xml(string caseDir)
    {
        var (input, kind) = Map(caseDir);

        var xml = AttachedDocumentBuilder.Build(input);

        // El lugar de la firma del contenedor es el primer ext:ExtensionContent; los demás están dentro de los
        // documentos embebidos y no cambian.
        var expected = Expected(caseDir, kind, input.DocumentKey);
        var slot = expected.IndexOf(InvoiceGoldenTests.SignatureSlotInFixture, StringComparison.Ordinal);
        Assert.That(slot, Is.GreaterThan(0));
        expected = string.Concat(expected.AsSpan(0, slot), InvoiceGoldenTests.SignatureSlotBeforeSigning,
            expected.AsSpan(slot + InvoiceGoldenTests.SignatureSlotInFixture.Length));
        AssertSameBytes(expected, xml);
    }

    // Firmado y sin su ds:Signature, el XML de ImpiFact es el mismo que el que firmó ApiDian sin la suya: la
    // firma no cambia ningún otro byte.
    [TestCaseSource(nameof(Cases))]
    public void Signed_builder_output_matches_ApiDian_outside_the_signature(string caseDir)
    {
        var (input, kind) = Map(caseDir);
        using var certificate = CreateThrowawayCertificate();

        var signed = XmlSigner.Sign(AttachedDocumentBuilder.Build(input), certificate, input.GeneratedAt.AddSeconds(1));

        Assert.That(SignatureRegex.Matches(signed), Has.Count.EqualTo(1));
        AssertSameBytes(Expected(caseDir, kind, input.DocumentKey), SignatureRegex.Replace(signed, ""));
    }

    private static (AttachedDocumentInput Input, AttachedDocumentKind Kind) Map(string caseDir)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(caseDir, "input.json"), Encoding.UTF8));
        var request = json.RootElement;
        var adapter = new ApiDianInvoiceAdapter(Catalog);
        var kind = RequiredText(request, "kind") switch
        {
            "invoice" => AttachedDocumentKind.Invoice,
            "credit-note" => AttachedDocumentKind.CreditNote,
            "debit-note" => AttachedDocumentKind.DebitNote,
            var other => throw new InvalidDataException($"Unknown kind '{other}'"),
        };
        var now = DateTime.ParseExact(RequiredText(request, "now"), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        var input = new AttachedDocumentInput
        {
            Kind = kind,
            Environment = (DianEnvironment)GoldenCaptureContext.EnvironmentId,
            DocumentKey = RequiredText(request, "cufe"),
            DocumentNumber = RequiredText(request, "prefix") + RequiredText(request, "number"),
            GeneratedAt = new DateTimeOffset(now, ColombiaUtcOffset),
            Sender = adapter.MapSupplier(),
            Receiver = adapter.MapCustomer(request.GetProperty("customer")),
            SignedDocumentXml = new UTF8Encoding(false, true).GetString(File.ReadAllBytes(Path.Combine(caseDir, "signed-document.xml"))),
            ApplicationResponseXml = File.ReadAllBytes(Path.Combine(caseDir, "application-response.xml")),
        };
        return (input, kind);
    }

    private static string Expected(string caseDir, AttachedDocumentKind kind, string documentKey)
    {
        var text = new UTF8Encoding(false, true).GetString(File.ReadAllBytes(Path.Combine(caseDir, GoldenCases.AttachedDocumentExpectedFile)));
        if (kind == AttachedDocumentKind.Invoice)
        {
            return text;
        }

        var php = $"<cbc:UUID schemeName=\"CUFE-SHA384\">{documentKey}</cbc:UUID>";
        var first = text.IndexOf(php, StringComparison.Ordinal);
        if (first < 0 || text.IndexOf(php, first + 1, StringComparison.Ordinal) >= 0)
        {
            Assert.Fail($"{GoldenCases.AttachedDocumentExpectedFile} must contain exactly one {php}");
        }

        return string.Concat(text.AsSpan(0, first), php.Replace("CUFE-SHA384", "CUDE-SHA384", StringComparison.Ordinal),
            text.AsSpan(first + php.Length));
    }

    private static void AssertSameBytes(string expected, string actual)
    {
        var encoding = new UTF8Encoding(false);
        if (!encoding.GetBytes(expected).AsSpan().SequenceEqual(encoding.GetBytes(actual)))
        {
            Assert.Fail(InvoiceGoldenTests.DescribeFirstDifference(expected, actual));
        }
    }

    private static X509Certificate2 CreateThrowawayCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=ImpiFact Golden Attached", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var notBefore = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return request.CreateSelfSigned(notBefore, notBefore.AddYears(1));
    }
}
