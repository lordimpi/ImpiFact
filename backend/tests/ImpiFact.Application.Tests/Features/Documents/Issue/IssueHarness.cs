using System.Text;
using System.Text.Json.Nodes;
using FluentValidation;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Protection;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Documents.Issue;
using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Tests.Features.Documents.Issue;

/// <summary>
/// Los handlers de emisión con puertos en memoria y la empresa del contexto fijo de captura de los casos golden
/// (NIT 900000001, resolución SETP 990000000–995000000 con su clave técnica, PIN 12345, habilitación).
/// </summary>
public sealed class IssueHarness : IDisposable
{
    public static readonly DateTimeOffset Now = new(2026, 11, 16, 15, 0, 0, TimeSpan.Zero);

    public const string TechnicalKey = "fc8eac422eba16e22ffd8c6f94b3f40a6e38162c";

    public Guid CompanyId { get; } = Guid.CreateVersion7();

    public FakeDocumentDb Db { get; } = new();

    public FakeIssuerContextReads Issuers { get; } = new();

    public FakeCatalogs Catalogs { get; } = new();

    public FakeCertificateStore Certificates { get; } = new();

    public FakePlanGate Plans { get; } = new();

    public IssueClock Clock { get; } = new(Now);

    public IssuerResolution InvoiceResolution { get; } = new(
        Guid.CreateVersion7(), "SETP", "18760000001", new DateOnly(2019, 1, 19), TechnicalKey, 990000000, 995000000,
        new DateOnly(2019, 1, 19), new DateOnly(2030, 1, 19), true);

    /// <summary>Export invoice resolution (type 2) of the golden capture: the same SETP data as the invoice one.</summary>
    public IssuerResolution ExportResolution { get; } = new(
        Guid.CreateVersion7(), "SETP", "18760000001", new DateOnly(2019, 1, 19), TechnicalKey, 990000000, 995000000,
        new DateOnly(2019, 1, 19), new DateOnly(2030, 1, 19), true);

    public IssuerResolution CreditResolution { get; } = new(Guid.CreateVersion7(), "NC", null, null, null, 1, 99999999, null, null, true);

    public IssuerResolution DebitResolution { get; } = new(Guid.CreateVersion7(), "ND", null, null, null, 1, 99999999, null, null, true);

    /// <summary>Support-document resolution (type 11) of the golden capture: SEDS 984000000-985000000, without technical key.</summary>
    public IssuerResolution SupportDocumentResolution { get; } = new(
        Guid.CreateVersion7(), "SEDS", "18760000001", new DateOnly(2022, 1, 1), null, 984000000, 985000000,
        new DateOnly(2022, 1, 1), new DateOnly(2030, 1, 19), true);

    /// <summary>Adjustment-note resolution (type 13) of the golden capture: NDS without dates.</summary>
    public IssuerResolution AdjustmentNoteResolution { get; } = new(Guid.CreateVersion7(), "NDS", null, null, null, 1, 99999999, null, null, true);

    /// <summary>
    /// Support-document software of the golden capture (SD_SOFTWARE_ID, PIN 54321), the one the support-document family
    /// must use; the invoice context keeps the invoice software.
    /// </summary>
    public IssuerSoftware SupportDocumentSoftware { get; } = new("7c1e5a90-2b4d-4f3a-8e6c-1d9b0a2f3c58", SecretPurposes.SoftwarePin + ":54321", null);

    /// <summary>Environment of the support-document family; habilitation like the capture unless a test changes it.</summary>
    public int SupportDocumentEnvironment { get; set; } = 2;

    public IssuerContext Context { get; private set; } = null!;

    public IssueHarness()
    {
        var company = new IssuerCompany(
            CompanyId, "900000001", "2", "EMPRESA DE PRUEBA GOLDEN SAS", "golden@example.com", "3000000000", "CALLE 1 # 1-01", "0000000-00",
            LanguageId: 79, CountryId: 46, MunicipalityId: 149, TaxId: 1, TypeCurrencyId: 35, TypeDocumentIdentificationId: 6,
            TypeOrganizationId: 1, TypeRegimeId: 1, IsActive: true);
        Context = new IssuerContext(
            company, [14], 2,
            new IssuerSoftware("3f9d8c2e-5b1a-4e7f-9c3d-2a6b8e1f0d47", SecretPurposes.SoftwarePin + ":12345", null),
            new IssuerCertificate("cert-ref", new DateTimeOffset(2036, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            []);
        foreach (var resolution in new[] { InvoiceResolution, ExportResolution, CreditResolution, DebitResolution, SupportDocumentResolution, AdjustmentNoteResolution })
        {
            Db.Sequences[NumberingScope.ForResolution(resolution.Id)] = resolution.From;
        }
    }

    /// <summary>Cambia el contexto de la empresa para los tres tipos de documento.</summary>
    public void SetContext(Func<IssuerContext, IssuerContext> change)
    {
        Context = change(Context);
        Issuers.Contexts[(CompanyId, 1)] = Context with { Resolutions = Resolutions(1) };
        Issuers.Contexts[(CompanyId, 2)] = Context with { Resolutions = Resolutions(2) };
        Issuers.Contexts[(CompanyId, 4)] = Context with { Resolutions = Resolutions(4) };
        Issuers.Contexts[(CompanyId, 5)] = Context with { Resolutions = Resolutions(5) };

        // The support-document family has its own software and environment (php-rules 13.1, item 6).
        foreach (var type in new[] { 11, 13 })
        {
            Issuers.Contexts[(CompanyId, type)] = Context with
            {
                Software = Context.Software is null ? null : SupportDocumentSoftware,
                TypeEnvironmentId = SupportDocumentEnvironment,
                Resolutions = Resolutions(type),
            };
        }
    }

    public Func<int, IReadOnlyList<IssuerResolution>> Resolutions { get; set; } = null!;

    public IssueHarness WithDefaultResolutions()
    {
        Resolutions = type => type switch
        {
            1 => [InvoiceResolution],
            2 => [ExportResolution],
            4 => [CreditResolution],
            11 => [SupportDocumentResolution],
            13 => [AdjustmentNoteResolution],
            _ => [DebitResolution],
        };
        SetContext(context => context);
        return this;
    }

    public static IssueHarness Create() => new IssueHarness().WithDefaultResolutions();

    /// <summary>Corre la validación y el handler del tipo, como el decorador de validación.</summary>
    public async Task<Result<IssueDocumentResponse>> IssueAsync(
        string kind, byte[] json, string? idempotencyKey = null, Guid? companyId = null, string? testSetId = null)
    {
        Assert.That(IssueDocumentJson.TryParse(json, out var body), Is.True, "The test body must parse.");
        var tenant = new IssueTenant(companyId ?? CompanyId);
        var qr = new FakeQrUrls();
        var secrets = new FakeSecretProtector();
        return kind switch
        {
            "invoice" => await Run(new IssueInvoiceCommandValidator(), new IssueInvoiceCommand(body!, json, idempotencyKey, testSetId),
                new IssueInvoiceHandler(tenant, Issuers, Catalogs, Db, Db, Db, Db, Db, Certificates, secrets, qr, Plans, Clock).HandleAsync),
            "invoice-aiu" => await Run(new IssueAiuInvoiceCommandValidator(), new IssueAiuInvoiceCommand(body!, json, idempotencyKey, testSetId),
                new IssueAiuInvoiceHandler(tenant, Issuers, Catalogs, Db, Db, Db, Db, Db, Certificates, secrets, qr, Plans, Clock).HandleAsync),
            "invoice-mandate" => await Run(new IssueMandateInvoiceCommandValidator(), new IssueMandateInvoiceCommand(body!, json, idempotencyKey, testSetId),
                new IssueMandateInvoiceHandler(tenant, Issuers, Catalogs, Db, Db, Db, Db, Db, Certificates, secrets, qr, Plans, Clock).HandleAsync),
            "invoice-transport" => await Run(new IssueTransportInvoiceCommandValidator(), new IssueTransportInvoiceCommand(body!, json, idempotencyKey, testSetId),
                new IssueTransportInvoiceHandler(tenant, Issuers, Catalogs, Db, Db, Db, Db, Db, Certificates, secrets, qr, Plans, Clock).HandleAsync),
            "invoice-export" => await Run(new IssueExportInvoiceCommandValidator(), new IssueExportInvoiceCommand(body!, json, idempotencyKey, testSetId),
                new IssueExportInvoiceHandler(tenant, Issuers, Catalogs, Db, Db, Db, Db, Db, Certificates, secrets, qr, Plans, Clock).HandleAsync),
            "support-document" => await Run(new IssueSupportDocumentCommandValidator(),
                new IssueSupportDocumentCommand(body!, json, idempotencyKey, testSetId),
                new IssueSupportDocumentHandler(tenant, Issuers, Catalogs, Db, Db, Db, Db, Db, Certificates, secrets, qr, Plans, Clock).HandleAsync),
            "sd-credit-note" => await Run(new IssueSupportDocumentAdjustmentNoteCommandValidator(),
                new IssueSupportDocumentAdjustmentNoteCommand(body!, json, idempotencyKey, testSetId),
                new IssueSupportDocumentAdjustmentNoteHandler(tenant, Issuers, Catalogs, Db, Db, Db, Db, Db, Certificates, secrets, qr, Plans, Clock).HandleAsync),
            "credit-note" => await Run(new IssueCreditNoteCommandValidator(), new IssueCreditNoteCommand(body!, json, idempotencyKey, testSetId),
                new IssueCreditNoteHandler(tenant, Issuers, Catalogs, Db, Db, Db, Db, Db, Certificates, secrets, qr, Plans, Clock).HandleAsync),
            _ => await Run(new IssueDebitNoteCommandValidator(), new IssueDebitNoteCommand(body!, json, idempotencyKey, testSetId),
                new IssueDebitNoteHandler(tenant, Issuers, Catalogs, Db, Db, Db, Db, Db, Certificates, secrets, qr, Plans, Clock).HandleAsync),
        };
    }

    public static async Task<Result<IssueDocumentResponse>> Run<TCommand>(
        IValidator<TCommand> validator, TCommand command, Func<TCommand, CancellationToken, Task<Result<IssueDocumentResponse>>> handle)
    {
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
        {
            return Error.Validation(validation.Errors.Select(failure => new FieldError(failure.PropertyName, failure.ErrorMessage)).ToList());
        }

        return await handle(command, CancellationToken.None);
    }

    public static byte[] GoldenInput(string caseDir) => File.ReadAllBytes(Path.Combine(caseDir, "input.json"));

    /// <summary>input.json de un caso golden con un cambio aplicado.</summary>
    public static byte[] Edited(string caseDir, Action<JsonObject> change)
    {
        var node = JsonNode.Parse(File.ReadAllBytes(Path.Combine(caseDir, "input.json")))!.AsObject();
        change(node);
        return Encoding.UTF8.GetBytes(node.ToJsonString());
    }

    public static string FirstInvoice => GoldenFiles.Cases("invoice").Single(dir => dir.EndsWith("single-line-iva19", StringComparison.Ordinal));

    public static string FirstCreditNote => GoldenFiles.Cases("credit-note").First();

    public static string FirstDebitNote => GoldenFiles.Cases("debit-note").First();

    public DocumentGraph SingleGraph => Db.Graphs.Single();

    public void Dispose() => Certificates.Dispose();
}
