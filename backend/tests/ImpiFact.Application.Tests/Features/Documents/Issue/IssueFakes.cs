using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Billing;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Application.Abstractions.Protection;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Documents.Issue;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Tests.Features.Documents.Issue;

/// <summary>
/// Base de datos de documentos en memoria: guarda lo que agrega el caso de uso (documentos, archivos, consecutivos,
/// llaves y mensajes) y responde las lecturas desde ahí, como si cada solicitud anterior ya se hubiera confirmado.
/// </summary>
public sealed class FakeDocumentDb : IDocumentStore, IDocumentFiles, INumberingSequences, IIdempotencyKeys, IOutbox
{
    public List<DocumentGraph> Graphs { get; } = [];

    public List<NewDocumentFile> Files { get; } = [];

    public Dictionary<string, long> Sequences { get; } = [];

    public Dictionary<(Guid Company, string Key), (string Hash, Guid DocumentId)> Keys { get; } = [];

    public List<(string Type, object? Payload, Guid? CompanyId)> Messages { get; } = [];

    public List<string> Calls { get; } = [];

    public void Add(DocumentGraph graph) => Graphs.Add(graph);

    public Task<StoredDocument?> FindByNumberAsync(Guid companyId, DocumentNumberKey number, CancellationToken ct) =>
        Task.FromResult(Find(document => document.CompanyId == companyId && document.DocumentTypeId == number.DocumentTypeId
            && document.TypeEnvironmentId == number.TypeEnvironmentId && document.Prefix == number.Prefix && document.Number == number.Number));

    public Task<StoredDocument?> FindByIdAsync(Guid companyId, Guid documentId, CancellationToken ct) =>
        Task.FromResult(Find(document => document.CompanyId == companyId && document.Id == documentId));

    public Task<Guid?> FindIdByCufeAsync(Guid companyId, string cufe, CancellationToken ct) =>
        Task.FromResult(Graphs.Select(graph => graph.Document).FirstOrDefault(document => document.CompanyId == companyId && document.Cufe == cufe)?.Id);

    public Task<byte[]?> ReadFileAsync(Guid companyId, Guid documentId, DocumentFileKind kind, CancellationToken ct) =>
        Task.FromResult(Files.FirstOrDefault(file => file.CompanyId == companyId && file.DocumentId == documentId && file.Kind == kind)?.Content);

    public Task<DocumentFile> AddAsync(NewDocumentFile file, CancellationToken ct)
    {
        Files.Add(file);
        return Task.FromResult(new DocumentFile
        {
            CompanyId = file.CompanyId,
            DocumentId = file.DocumentId,
            Kind = file.Kind,
            StorageKey = "fake",
            ContentType = file.ContentType,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(file.Content)),
        });
    }

    public Task OpenForResolutionAsync(Guid companyId, Guid resolutionId, long firstNumber, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task OpenAsync(Guid companyId, string scopeKey, long firstNumber, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<long?> TakeNextAsync(Guid companyId, string scopeKey, CancellationToken ct)
    {
        Calls.Add("take " + scopeKey);
        if (!Sequences.TryGetValue(scopeKey, out var next))
        {
            return Task.FromResult<long?>(null);
        }

        Sequences[scopeKey] = next + 1;
        return Task.FromResult<long?>(next);
    }

    public async Task<NumberReservation> ReserveAsync(Guid companyId, string scopeKey, DocumentNumberKey number, CancellationToken ct)
    {
        Calls.Add($"reserve {scopeKey} {number.Number}");
        if (!Sequences.TryGetValue(scopeKey, out var next))
        {
            return NumberReservation.SequenceNotFound;
        }

        Sequences[scopeKey] = Math.Max(next, number.Number + 1);
        return await FindByNumberAsync(companyId, number, ct) is null ? NumberReservation.Reserved : NumberReservation.AlreadyUsed;
    }

    public Task<IdempotencyRecord?> FindAsync(Guid companyId, string key, CancellationToken ct) =>
        Task.FromResult(Keys.TryGetValue((companyId, key), out var entry) ? new IdempotencyRecord(entry.Hash, entry.DocumentId) : null);

    public void Add(Guid companyId, string key, string requestSha256, Guid documentId, DateTimeOffset expiresAt) =>
        Keys[(companyId, key)] = (requestSha256, documentId);

    public Task<Guid> EnqueueAsync<TPayload>(string type, TPayload payload, Guid? companyId, DateTimeOffset? notBefore, CancellationToken ct)
    {
        Messages.Add((type, payload, companyId));
        return Task.FromResult(Guid.CreateVersion7());
    }

    private StoredDocument? Find(Func<Document, bool> match)
    {
        var document = Graphs.Select(graph => graph.Document).FirstOrDefault(match);
        if (document is null)
        {
            return null;
        }

        var request = Files.FirstOrDefault(file => file.DocumentId == document.Id && file.Kind == DocumentFileKind.RequestJson);
        return new StoredDocument(
            document.Id, document.DocumentTypeId, document.TypeEnvironmentId, document.ResolutionId, document.Prefix, document.Number,
            document.FullNumber, document.Status, document.Cufe, document.UuidAlgorithm,
            request is null ? null : Convert.ToHexStringLower(SHA256.HashData(request.Content)));
    }
}

/// <summary>Contexto de emisión preparado por la prueba.</summary>
public sealed class FakeIssuerContextReads : IIssuerContextReads
{
    public Dictionary<(Guid Company, int Type), IssuerContext> Contexts { get; } = [];

    public Task<IssuerContext?> GetAsync(Guid companyId, int documentTypeId, DocumentKind kind, CancellationToken ct) =>
        Task.FromResult(Contexts.GetValueOrDefault((companyId, documentTypeId)));
}

/// <summary>Catálogos del archivo de las pruebas golden (catalogs.json, copiado de los CSV del PHP).</summary>
public sealed class FakeCatalogs : IDocumentCatalogs
{
    private static readonly Dictionary<string, DianCatalog> Names = new(StringComparer.Ordinal)
    {
        ["documentTypes"] = DianCatalog.DocumentType,
        ["operationTypes"] = DianCatalog.OperationType,
        ["currencies"] = DianCatalog.Currency,
        ["organizationTypes"] = DianCatalog.OrganizationType,
        ["identificationTypes"] = DianCatalog.IdentificationType,
        ["regimes"] = DianCatalog.Regime,
        ["liabilities"] = DianCatalog.Liability,
        ["taxes"] = DianCatalog.Tax,
        ["municipalities"] = DianCatalog.Municipality,
        ["countries"] = DianCatalog.Country,
        ["languages"] = DianCatalog.Language,
        ["paymentForms"] = DianCatalog.PaymentForm,
        ["paymentMethods"] = DianCatalog.PaymentMethod,
        ["discounts"] = DianCatalog.Discount,
        ["unitMeasures"] = DianCatalog.UnitMeasure,
        ["itemIdentificationTypes"] = DianCatalog.ItemIdentificationType,
        ["referencePrices"] = DianCatalog.ReferencePrice,
        ["creditNoteDiscrepancies"] = DianCatalog.CreditNoteDiscrepancy,
        ["debitNoteDiscrepancies"] = DianCatalog.DebitNoteDiscrepancy,
    };

    public Dictionary<CatalogKey, CatalogItem> Items { get; } = Load();

    public Task<IReadOnlyDictionary<CatalogKey, CatalogItem>> FindAsync(IReadOnlyCollection<CatalogKey> keys, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<CatalogKey, CatalogItem>>(
            keys.Where(Items.ContainsKey).Distinct().ToDictionary(key => key, key => Items[key]));

    private static Dictionary<CatalogKey, CatalogItem> Load()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(GoldenFiles.Catalogs));
        var root = json.RootElement;
        var departments = root.GetProperty("departments").EnumerateObject()
            .ToDictionary(row => row.Name, row => new CatalogItem(row.Value.GetProperty("code").GetString()!, row.Value.GetProperty("name").GetString()!));
        var items = new Dictionary<CatalogKey, CatalogItem>();
        foreach (var catalog in root.EnumerateObject().Where(property => Names.ContainsKey(property.Name)))
        {
            foreach (var row in catalog.Value.EnumerateObject())
            {
                var value = row.Value;
                var department = value.TryGetProperty("departmentId", out var departmentId) ? departments[departmentId.GetString()!] : null;
                var agency = value.TryGetProperty("codeAgency", out var codeAgency) ? codeAgency.GetString() : null;
                items[new CatalogKey(Names[catalog.Name], int.Parse(row.Name, System.Globalization.CultureInfo.InvariantCulture))] =
                    new CatalogItem(value.GetProperty("code").GetString()!, value.GetProperty("name").GetString()!, agency, department);
            }
        }

        return items;
    }
}

/// <summary>Almacén de certificados con un certificado desechable por referencia.</summary>
public sealed class FakeCertificateStore : ICertificateStore, IDisposable
{
    private readonly X509Certificate2 _certificate = CreateCertificate();

    public X509Certificate2 Certificate => _certificate;

    public List<(Guid CompanyId, string SecretRef)> Loads { get; } = [];

    public Task<string> SaveAsync(Guid companyId, byte[] pfx, string password, CancellationToken ct) => throw new NotSupportedException();

    public Task<X509Certificate2> LoadAsync(Guid companyId, string secretRef, CancellationToken ct)
    {
        Loads.Add((companyId, secretRef));
        return Task.FromResult(X509CertificateLoader.LoadPkcs12(_certificate.Export(X509ContentType.Pkcs12, "x"), "x"));
    }

    public Task DeleteAsync(Guid companyId, string secretRef, CancellationToken ct) => throw new NotSupportedException();

    public void Dispose() => _certificate.Dispose();

    private static X509Certificate2 CreateCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=ImpiFact Issue Tests, C=CO", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2036, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }
}

/// <summary>«Cifra» anteponiendo el propósito, para comprobar que el caso de uso descifra con el propósito correcto.</summary>
public sealed class FakeSecretProtector : ISecretProtector
{
    public string Protect(string purpose, string plain) => purpose + ":" + plain;

    public string Unprotect(string purpose, string cipher) =>
        cipher.StartsWith(purpose + ":", StringComparison.Ordinal)
            ? cipher[(purpose.Length + 1)..]
            : throw new CryptographicException("Wrong purpose.");
}

public sealed class FakeQrUrls : IDianQrUrls
{
    public string ForEnvironment(int typeEnvironmentId) => typeEnvironmentId == 1
        ? "https://catalogo-vpfe.dian.gov.co/document/searchqr?documentkey="
        : "https://catalogo-vpfe-hab.dian.gov.co/document/searchqr?documentkey=";
}

/// <summary>Plan de la empresa preparado por la prueba: deja emitir salvo que tenga un rechazo; anota cada consulta.</summary>
public sealed class FakePlanGate : IPlanGate
{
    public Error? Refusal { get; set; }

    public List<(Guid CompanyId, DocumentKind Kind)> Checks { get; } = [];

    public Task<Error?> CheckIssueAsync(Guid companyId, DocumentKind kind, CancellationToken ct)
    {
        Checks.Add((companyId, kind));
        return Task.FromResult(Refusal);
    }

    public Task CountAcceptedAsync(Guid companyId, DocumentKind kind, CancellationToken ct) =>
        throw new NotSupportedException("Issuing a document never counts plan usage.");
}

public sealed class IssueClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}

public sealed class IssueTenant(Guid? companyId) : ITenantContext
{
    public Guid? CompanyId { get; } = companyId;

    public Guid? UserId => null;

    public ActorType Actor => ActorType.ApiKey;

    public IReadOnlySet<string> Scopes { get; } = new HashSet<string>();
}

/// <summary>Ubica los casos golden y el catálogo de sus pruebas subiendo desde la carpeta de salida.</summary>
public static class GoldenFiles
{
    public static string Root => Find(Path.Combine("tests", "golden"));

    public static string Catalogs => Path.Combine(Find(Path.Combine("tests", "ImpiFact.Golden.Tests")), "catalogs.json");

    public static IEnumerable<string> Cases(string type) =>
        Directory.EnumerateDirectories(Path.Combine(Root, type)).Order(StringComparer.Ordinal);

    private static string Find(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException(relative + " not found above " + AppContext.BaseDirectory);
    }
}
