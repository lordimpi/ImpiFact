// Prototipo de la fase 1: construye una factura nacional de ejemplo, la firma con XAdES-EPES (el formato de
// firma digital XML que exige la DIAN) y la empaca en el ZIP que espera la DIAN. La fase 1a llega hasta
// aquí; el envío a la DIAN llega en la fase 1b.
//
// Uso (desde la carpeta backend/):
//   dotnet run --project tools/ImpiFact.Prototype -- [--out <dir>] [--cert <file.p12> --password <pwd>]
//   dotnet run --project tools/ImpiFact.Prototype -- sign-samples [--out <dir>]
//
// Sin --cert se crea en memoria un certificado autofirmado desechable. Basta para verificar la firma
// localmente, pero la DIAN no lo aceptará.
//
// sign-samples firma todos los casos de referencia de tests/golden (el XML que generó ApiDian, el PHP
// original) y los escribe en <dir>/signed/, junto con el certificado público en <dir>/signer.pem, para que
// tools/signature-check los verifique con xmlsec1, un verificador de firmas XML independiente de .NET.
// Sin --out escribe en una carpeta temporal nueva; nunca escribe dentro del repositorio.

using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using ImpiFact.Dian.Cufe;
using ImpiFact.Dian.Signing;
using ImpiFact.Dian.Ubl;

if (args is ["sign-samples", ..])
{
    return SignSamples(ParseArguments(args[1..]));
}

var options = ParseArguments(args);
var outputDirectory = options.GetValueOrDefault("--out")
    ?? Path.Combine(Path.GetTempPath(), "impifact-prototype-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
Directory.CreateDirectory(outputDirectory);

using var certificate = options.TryGetValue("--cert", out var certPath)
    ? X509CertificateLoader.LoadPkcs12FromFile(certPath, options.GetValueOrDefault("--password"))
    : CreateThrowawayCertificate();

var invoice = SampleInvoice();
var built = InvoiceUblBuilder.Build(invoice);
var signed = XmlSigner.Sign(built.Xml, certificate, DateTimeOffset.Now);

var signatureValid = VerifySignature(signed);
var xmlName = DianFileName("fv", invoice.Supplier.IdentificationNumber, invoice.IssueDate, sequence: 1) + ".xml";
var zipName = DianFileName("z", invoice.Supplier.IdentificationNumber, invoice.IssueDate, sequence: 1) + ".zip";

File.WriteAllText(Path.Combine(outputDirectory, "unsigned.xml"), built.Xml, new UTF8Encoding(false));
// Solo el certificado público (nunca la clave privada), para que tools/signature-check pueda verificar la
// salida con xmlsec1 (una herramienta externa que verifica firmas XML).
File.WriteAllText(Path.Combine(outputDirectory, "signer.pem"), certificate.ExportCertificatePem());
File.WriteAllText(Path.Combine(outputDirectory, xmlName), signed, new UTF8Encoding(false));
using (var zip = ZipFile.Open(Path.Combine(outputDirectory, zipName), ZipArchiveMode.Create))
{
    var entry = zip.CreateEntry(xmlName, CompressionLevel.Optimal);
    using var stream = entry.Open();
    stream.Write(new UTF8Encoding(false).GetBytes(signed));
}

Console.WriteLine($"Invoice      {invoice.DocumentNumber}");
Console.WriteLine($"CUFE         {built.Cufe}");
Console.WriteLine($"Certificate  {certificate.Subject}{(certPath is null ? " (throwaway, not valid for the DIAN)" : "")}");
Console.WriteLine($"Signature    {(signatureValid ? "valid" : "INVALID")}");
Console.WriteLine($"Output       {outputDirectory}");
Console.WriteLine($"             unsigned.xml, {xmlName}, {zipName}, signer.pem");
return signatureValid ? 0 : 1;

static int SignSamples(Dictionary<string, string> options)
{
    var directory = Path.GetFullPath(options.GetValueOrDefault("--out")
        ?? Path.Combine(Path.GetTempPath(), "impifact-signature-samples-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)));
    var repositoryRoot = FindAbove(AppContext.BaseDirectory, dir =>
        Directory.Exists(Path.Combine(dir, ".git")) || File.Exists(Path.Combine(dir, ".git")));
    if (repositoryRoot is not null && IsInside(directory, repositoryRoot))
    {
        Console.Error.WriteLine($"Refusing to write inside the repository: {directory}");
        return 2;
    }

    var goldenRoot = FindAbove(AppContext.BaseDirectory, dir => Directory.Exists(Path.Combine(dir, "tests", "golden")))
        ?? throw new DirectoryNotFoundException("tests/golden not found above " + AppContext.BaseDirectory);
    goldenRoot = Path.Combine(goldenRoot, "tests", "golden");

    using var certificate = CreateThrowawayCertificate();
    var signedDirectory = Path.Combine(directory, "signed");
    Directory.CreateDirectory(signedDirectory);
    var signingTime = DateTimeOffset.Now;
    var count = 0;
    var allValid = true;

    // Un archivo por caso de referencia (facturas, notas crédito, nota débito), llamado <tipo>.<caso>.xml.
    foreach (var caseDirectory in Directory.EnumerateDirectories(goldenRoot)
        .SelectMany(Directory.EnumerateDirectories)
        .Where(dir => File.Exists(Path.Combine(dir, "expected.xml")))
        .Order(StringComparer.Ordinal))
    {
        var documentType = Path.GetFileName(Path.GetDirectoryName(caseDirectory)!);
        var signed = XmlSigner.Sign(File.ReadAllText(Path.Combine(caseDirectory, "expected.xml")), certificate, signingTime);
        allValid &= VerifySignature(signed);
        File.WriteAllText(Path.Combine(signedDirectory, $"{documentType}.{Path.GetFileName(caseDirectory)}.xml"), signed, new UTF8Encoding(false));
        count++;
    }

    // Solo el certificado público: la clave privada se queda en memoria y nunca se escribe.
    File.WriteAllText(Path.Combine(directory, "signer.pem"), certificate.ExportCertificatePem(), new UTF8Encoding(false));

    Console.WriteLine($"Signed       {count} reference documents");
    Console.WriteLine($"Signature    {(allValid ? "valid (.NET check)" : "INVALID")}");
    Console.WriteLine($"Output       {directory}");
    Console.WriteLine($"Next         bash tools/signature-check/verify.sh \"{directory}\"");
    return allValid ? 0 : 1;
}

// Sube desde start hasta la raíz del disco y devuelve la primera carpeta que cumple la condición.
static string? FindAbove(string start, Func<string, bool> matches)
{
    for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
    {
        if (matches(dir.FullName))
        {
            return dir.FullName;
        }
    }

    return null;
}

static bool IsInside(string path, string root)
{
    var relative = Path.GetRelativePath(root, path);
    return relative == "." || !(relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative));
}

static Dictionary<string, string> ParseArguments(string[] args)
{
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 0; i + 1 < args.Length; i += 2)
    {
        result[args[i]] = args[i + 1];
    }

    return result;
}

static X509Certificate2 CreateThrowawayCertificate()
{
    using var rsa = RSA.Create(2048);
    var request = new CertificateRequest(
        "CN=900000001, O=EMPRESA DE PRUEBA PROTOTIPO SAS, C=CO", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    return request.CreateSelfSigned(DateTimeOffset.Now.AddMinutes(-5), DateTimeOffset.Now.AddYears(1));
}

// Control rápido local: verifica la firma con el verificador de firmas XML de .NET (SignedXml).
static bool VerifySignature(string signedXml)
{
    var document = new XmlDocument { PreserveWhitespace = true };
    document.LoadXml(signedXml);
    var signatureElement = (XmlElement)document.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl)[0]!;
    var signature = new SignedXml(document);
    signature.LoadXml(signatureElement);
    return signature.CheckSignature();
}

// Nombres de archivo de la DIAN: prefijo ("fv" para el XML de la factura, "z" para el ZIP) + NIT del
// proveedor (número de identificación tributaria, 10 dígitos) + código del proveedor tecnológico 000 +
// año de 2 dígitos + consecutivo de 8 dígitos.
static string DianFileName(string prefix, string nit, DateOnly issueDate, int sequence) =>
    string.Create(CultureInfo.InvariantCulture,
        $"{prefix}{nit.PadLeft(10, '0')}000{issueDate:yy}{sequence:D8}");

static SalesInvoice SampleInvoice()
{
    var bogota = new PartyAddress(
        MunicipalityCode: "11001", CityName: "Bogotá, D.c. ", DepartmentName: "Bogotá", DepartmentCode: "11",
        AddressLine: "CALLE 1 # 1-01", CountryCode: "CO", CountryName: "Colombia", LanguageCode: "es");
    var iva19 = new TaxSubtotal { SchemeCode = "01", SchemeName = "IVA", TaxableAmount = 1_000_000m, TaxAmount = 190_000m, Percent = 19m };

    return new SalesInvoice
    {
        Environment = DianEnvironment.Testing,
        // En la API este valor sale de DianOptions; el prototipo usa la URL de habilitación.
        QrCodeBaseUrl = "https://catalogo-vpfe-hab.dian.gov.co/document/searchqr?documentkey=",
        OperationTypeCode = "10",
        InvoiceTypeCode = "01",
        Resolution = new NumberingResolution(
            "18760000001", new DateOnly(2019, 1, 19), new DateOnly(2030, 1, 19), "SETP", 990_000_000, 995_000_000,
            "fc8eac422eba16e22ffd8c6f94b3f40a6e38162c"),
        Number = 990_000_100,
        IssueDate = DateOnly.FromDateTime(DateTime.Now),
        IssueTime = new TimeOnly(DateTime.Now.Hour, DateTime.Now.Minute, DateTime.Now.Second),
        Software = new SoftwareCredentials("3f9d8c2e-5b1a-4e7f-9c3d-2a6b8e1f0d47", "12345"),
        LocalCurrencyCode = "COP",
        Supplier = new InvoiceParty
        {
            OrganizationTypeCode = "1", IdentificationNumber = "900000001", CheckDigit = "2", IdentificationTypeCode = "31",
            Name = "EMPRESA DE PRUEBA PROTOTIPO SAS", Address = bogota, RegimeCode = "48", LiabilityCode = "O-23",
            TaxSchemeCode = "01", TaxSchemeName = "IVA", MerchantRegistration = "0000000-00",
            Telephone = "3000000000", Email = "prototipo@example.com",
        },
        Customer = new InvoiceParty
        {
            OrganizationTypeCode = "1", IdentificationNumber = "800000001", CheckDigit = "1", IdentificationTypeCode = "31",
            Name = "CLIENTE DE PRUEBA SAS", Address = bogota with { AddressLine = "CARRERA 2 # 2-02" }, RegimeCode = "48",
            LiabilityCode = "R-99-PN", TaxSchemeCode = "01", TaxSchemeName = "IVA", MerchantRegistration = "0000000-00",
            Telephone = "3000000001", Email = "cliente@example.com",
        },
        Payment = new PaymentTerms("1", "10", DateOnly.FromDateTime(DateTime.Now), 0),
        TaxSubtotals = [iva19],
        Totals = new MonetaryTotals
        {
            LineExtensionAmount = 1_000_000m, TaxExclusiveAmount = 1_000_000m, TaxInclusiveAmount = 1_190_000m,
            PayableAmount = 1_190_000m,
        },
        Lines =
        [
            new InvoiceLine
            {
                UnitCode = "94", Quantity = 1m, LineExtensionAmount = 1_000_000m, IsFreeOfCharge = false,
                Description = "Servicio de prueba del prototipo", ItemCode = "PROTO01",
                ItemScheme = new ItemIdentificationScheme("999", "Estándar de adopción del contribuyente", ""),
                PriceAmount = 1_000_000m, BaseQuantity = 1m, TaxSubtotals = [iva19],
            },
        ],
    };
}
