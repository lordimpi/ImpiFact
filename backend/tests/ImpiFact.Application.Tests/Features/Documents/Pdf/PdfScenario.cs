using System.Text;
using ImpiFact.Application.Abstractions.Companies;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Pdf;
using ImpiFact.Application.Features.Documents.Pdf;
using ImpiFact.Application.Tests.Features.Documents.Issue;
using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Tests.Features.Documents.Pdf;

/// <summary>
/// Un documento ya emitido en memoria, armado con un caso golden (la solicitud JSON y el XML firmado que generó el
/// PHP): su detalle en las consultas, la solicitud y el XML en los archivos, la empresa con su resolución y los
/// catálogos reales de las pruebas golden.
/// </summary>
internal sealed class PdfScenario
{
    public static readonly Guid CompanyId = Guid.Parse("0194a000-0000-7000-8000-000000000001");

    public static readonly Guid ResolutionId = Guid.Parse("0194a000-0000-7000-8000-0000000000aa");

    public FakeDocumentReads Reads { get; } = new();

    public FakeDocumentDb Db { get; } = new();

    public FakeIssuerContextReads Issuers { get; } = new();

    public FakeCatalogs Catalogs { get; } = new();

    public FakeQrUrls QrUrls { get; } = new();

    public FakeCompanyLogos Logos { get; } = new();

    public Guid DocumentId { get; private set; }

    public DocumentPdfModelBuilder Builder() => new(Reads, Db, Issuers, Catalogs, QrUrls, Logos);

    /// <summary>Guarda el caso golden <paramref name="goldenCase"/> (por ejemplo <c>invoice/single-line-iva19</c>) como documento aceptado.</summary>
    public PdfScenario WithGolden(
        string goldenCase,
        int documentTypeId,
        string prefix,
        long number,
        DocumentTotals totals,
        DocumentCustomer? customer = null,
        string? notes = null,
        bool withSignedXml = true,
        DocumentStatus status = DocumentStatus.Accepted,
        DateTimeOffset? acceptedAt = null,
        Func<string, string>? editRequest = null)
    {
        var folder = Path.Combine(GoldenFiles.Root, goldenCase.Replace('/', Path.DirectorySeparatorChar));
        var request = File.ReadAllText(Path.Combine(folder, "input.json"), Encoding.UTF8);
        return WithRequest(
            editRequest is null ? request : editRequest(request),
            withSignedXml ? File.ReadAllText(Path.Combine(folder, "expected.xml"), Encoding.UTF8) : null,
            documentTypeId, prefix, number, totals, customer, notes, status, acceptedAt);
    }

    public PdfScenario WithRequest(
        string request,
        string? signedXml,
        int documentTypeId,
        string prefix,
        long number,
        DocumentTotals totals,
        DocumentCustomer? customer = null,
        string? notes = null,
        DocumentStatus status = DocumentStatus.Accepted,
        DateTimeOffset? acceptedAt = null)
    {
        DocumentId = Guid.CreateVersion7();
        var cufe = "cufe-" + number;
        var header = new DocumentSummary(
            DocumentId,
            documentTypeId,
            2,
            prefix,
            number,
            prefix + number,
            status,
            cufe,
            new DateOnly(2026, 11, 14),
            new TimeOnly(10, 20, 0),
            new DateTimeOffset(2026, 11, 14, 15, 20, 0, TimeSpan.Zero),
            35,
            customer ?? new DocumentCustomer(6, "800000001", "1", "CLIENTE DE PRUEBA GOLDEN SAS", "3000000001", "CARRERA 2 # 2-02",
                "cliente.golden@example.com", 149, 1, 1, 117, "0000000-00"),
            totals);
        Reads.Details.Add(new DocumentDetail(
            header,
            documentTypeId switch { 1 or 12 => "CUFE-SHA384", 11 or 13 => "CUDS-SHA384", _ => "CUDE-SHA384" },
            documentTypeId is 1 or 11 or 12 ? ResolutionId : null,
            null,
            10,
            null,
            null,
            null,
            notes,
            null,
            null,
            null,
            "00",
            "Procesado Correctamente.",
            acceptedAt,
            acceptedAt,
            [],
            []));
        Db.Graphs.Add(new DocumentGraph(
            new Document
            {
                Id = DocumentId,
                CompanyId = CompanyId,
                DocumentTypeId = documentTypeId,
                TypeEnvironmentId = 2,
                Prefix = prefix,
                Number = number,
                FullNumber = prefix + number,
                Status = status,
                Cufe = cufe,
                IssueDate = header.IssueDate,
                IssueTime = header.IssueTime,
                IssuedAt = header.IssuedAt,
                CustomerSnapshot = new CustomerSnapshot { IdentificationNumber = header.Customer.IdentificationNumber, Name = header.Customer.Name },
            },
            [], [], [], [], [], []));
        Db.Files.Add(new NewDocumentFile(CompanyId, DocumentId, DocumentFileKind.RequestJson, Encoding.UTF8.GetBytes(request), "application/json"));
        if (signedXml is not null)
        {
            Db.Files.Add(new NewDocumentFile(CompanyId, DocumentId, DocumentFileKind.SignedXml, Encoding.UTF8.GetBytes(signedXml), "application/xml"));
        }

        // The invoice type 04 (type 12) reads the type 1 resolutions that number it.
        Issuers.Contexts[(CompanyId, DocumentFamilies.ResolutionTypeOf(documentTypeId))] = new IssuerContext(
            new IssuerCompany(CompanyId, "900123456", "7", "EMPRESA EMISORA SAS", "facturacion@emisora.example", "6015550000", "CALLE 1 # 2-03",
                null, 79, 46, 149, 1, 35, 6, 1, 1, true),
            [14, 117],
            2,
            null,
            null,
            [new IssuerResolution(ResolutionId, "SETP", "18760000001", new DateOnly(2019, 1, 19), "fc8eac422eba16e22ffd8c6f94b3f40a6e38162c",
                990000000, 995000000, new DateOnly(2019, 1, 19), new DateOnly(2030, 1, 19), true)]);
        return this;
    }

    public async Task<DocumentPdfModel> BuildAsync() =>
        await Builder().BuildAsync(CompanyId, DocumentId, CancellationToken.None)
        ?? throw new AssertionException("The builder found no document.");
}

/// <summary>Logos de empresa en memoria.</summary>
internal sealed class FakeCompanyLogos : ICompanyLogos
{
    public Dictionary<Guid, CompanyLogoImage> Logos { get; } = [];

    public Task<CompanyLogoImage?> FindAsync(Guid companyId, CancellationToken ct) => Task.FromResult(Logos.GetValueOrDefault(companyId));

    public Task SaveAsync(Guid companyId, CompanyLogoImage logo, CancellationToken ct)
    {
        Logos[companyId] = logo;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid companyId, CancellationToken ct)
    {
        Logos.Remove(companyId);
        return Task.CompletedTask;
    }
}
