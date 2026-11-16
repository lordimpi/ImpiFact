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

    public IssueClock Clock { get; } = new(Now);

    public IssuerResolution InvoiceResolution { get; } = new(
        Guid.CreateVersion7(), "SETP", "18760000001", new DateOnly(2019, 1, 19), TechnicalKey, 990000000, 995000000,
        new DateOnly(2019, 1, 19), new DateOnly(2030, 1, 19), true);

    public IssuerResolution CreditResolution { get; } = new(Guid.CreateVersion7(), "NC", null, null, null, 1, 99999999, null, null, true);

    public IssuerResolution DebitResolution { get; } = new(Guid.CreateVersion7(), "ND", null, null, null, 1, 99999999, null, null, true);

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
        foreach (var resolution in new[] { InvoiceResolution, CreditResolution, DebitResolution })
        {
            Db.Sequences[NumberingScope.ForResolution(resolution.Id)] = resolution.From;
        }
    }

    /// <summary>Cambia el contexto de la empresa para los tres tipos de documento.</summary>
    public void SetContext(Func<IssuerContext, IssuerContext> change)
    {
        Context = change(Context);
        Issuers.Contexts[(CompanyId, 1)] = Context with { Resolutions = Resolutions(1) };
        Issuers.Contexts[(CompanyId, 4)] = Context with { Resolutions = Resolutions(4) };
        Issuers.Contexts[(CompanyId, 5)] = Context with { Resolutions = Resolutions(5) };
    }

    public Func<int, IReadOnlyList<IssuerResolution>> Resolutions { get; set; } = null!;

    public IssueHarness WithDefaultResolutions()
    {
        Resolutions = type => type switch
        {
            1 => [InvoiceResolution],
            4 => [CreditResolution],
            _ => [DebitResolution],
        };
        SetContext(context => context);
        return this;
    }

    public static IssueHarness Create() => new IssueHarness().WithDefaultResolutions();

    /// <summary>Corre la validación y el handler del tipo, como el decorador de validación.</summary>
    public async Task<Result<IssueDocumentResponse>> IssueAsync(string kind, byte[] json, string? idempotencyKey = null, Guid? companyId = null)
    {
        Assert.That(IssueDocumentJson.TryParse(json, out var body), Is.True, "The test body must parse.");
        var tenant = new IssueTenant(companyId ?? CompanyId);
        var qr = new FakeQrUrls();
        var secrets = new FakeSecretProtector();
        return kind switch
        {
            "invoice" => await Run(new IssueInvoiceCommandValidator(), new IssueInvoiceCommand(body!, json, idempotencyKey),
                new IssueInvoiceHandler(tenant, Issuers, Catalogs, Db, Db, Db, Db, Db, Certificates, secrets, qr, Clock).HandleAsync),
            "credit-note" => await Run(new IssueCreditNoteCommandValidator(), new IssueCreditNoteCommand(body!, json, idempotencyKey),
                new IssueCreditNoteHandler(tenant, Issuers, Catalogs, Db, Db, Db, Db, Db, Certificates, secrets, qr, Clock).HandleAsync),
            _ => await Run(new IssueDebitNoteCommandValidator(), new IssueDebitNoteCommand(body!, json, idempotencyKey),
                new IssueDebitNoteHandler(tenant, Issuers, Catalogs, Db, Db, Db, Db, Db, Certificates, secrets, qr, Clock).HandleAsync),
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
