using System.Text.Json.Nodes;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Storage;
using ImpiFact.Domain.Catalog;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Companies;
using ImpiFact.Domain.Documents;
using ImpiFact.Domain.Platform;
using ImpiFact.Infrastructure.Options;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Infrastructure.Documents;

// Adaptadores de la emisión de documentos sobre EF Core (Entity Framework, el mapeador de objetos a tablas). Las
// lecturas van sin rastreo, quitan solo el filtro de tenant (tenant: cada empresa cliente con sus datos aislados) y
// ponen la empresa en el WHERE; la empresa la pasa el caso de uso desde el contexto de la solicitud.

/// <summary>Contexto de emisión de la empresa leído de las tablas del módulo Company.</summary>
internal sealed class EfIssuerContextReads(ImpiFactDbContext db) : IIssuerContextReads
{
    /// <summary>Ambiente cuando la empresa no tiene fila para la familia, como el PHP (habilitación).</summary>
    private const int DefaultEnvironment = TypeEnvironment.Habilitation;

    public async Task<IssuerContext?> GetAsync(Guid companyId, int documentTypeId, DocumentKind kind, CancellationToken ct)
    {
        var company = await db.Set<Company>().AsNoTracking().FirstOrDefaultAsync(row => row.Id == companyId, ct);
        if (company is null)
        {
            return null;
        }

        var environment = await Of<CompanyEnvironment>(companyId)
            .Where(row => row.DocumentKind == kind)
            .Select(row => (int?)row.TypeEnvironmentId)
            .FirstOrDefaultAsync(ct) ?? DefaultEnvironment;
        var liabilities = await Of<CompanyLiability>(companyId).Select(row => row.TypeLiabilityId).OrderBy(id => id).ToListAsync(ct);
        var software = await Of<CompanySoftware>(companyId)
            .Where(row => row.DocumentKind == kind && row.TypeEnvironmentId == environment)
            .Select(row => new IssuerSoftware(row.SoftwareIdentifier, row.PinCipher, row.TestSetId))
            .FirstOrDefaultAsync(ct);
        var certificate = await Of<Certificate>(companyId)
            .Where(row => row.IsActive)
            .OrderByDescending(row => row.NotAfter)
            .Select(row => new IssuerCertificate(row.SecretRef, row.NotAfter))
            .FirstOrDefaultAsync(ct);
        var resolutions = await Of<Resolution>(companyId)
            .Where(row => row.DocumentTypeId == documentTypeId && row.TypeEnvironmentId == environment)
            .OrderBy(row => row.Id)
            .Select(row => new IssuerResolution(
                row.Id, row.Prefix, row.ResolutionNumber, row.ResolutionDate, row.TechnicalKey, row.RangeFrom, row.RangeTo, row.ValidFrom, row.ValidTo, row.IsActive))
            .ToListAsync(ct);

        return new IssuerContext(
            new IssuerCompany(
                company.Id, company.IdentificationNumber, company.Dv, company.Name, company.Email, company.Phone, company.Address,
                company.MerchantRegistration, company.LanguageId, company.CountryId, company.MunicipalityId, company.TaxId,
                company.TypeCurrencyId, company.TypeDocumentIdentificationId, company.TypeOrganizationId, company.TypeRegimeId,
                company.IsActive),
            liabilities,
            environment,
            software,
            certificate,
            resolutions);
    }

    private IQueryable<T> Of<T>(Guid companyId)
        where T : class, ITenantOwned =>
        db.Set<T>().AsNoTracking().IgnoreQueryFilters([QueryFilters.Tenant]).Where(row => row.CompanyId == companyId);
}

/// <summary>Catálogos DIAN sembrados; una consulta por catálogo pedido, solo con los ids pedidos.</summary>
internal sealed class EfDocumentCatalogs(ImpiFactDbContext db) : IDocumentCatalogs
{
    public async Task<IReadOnlyDictionary<CatalogKey, CatalogItem>> FindAsync(IReadOnlyCollection<CatalogKey> keys, CancellationToken ct)
    {
        var found = new Dictionary<CatalogKey, CatalogItem>();
        foreach (var group in keys.GroupBy(key => key.Catalog))
        {
            var ids = group.Select(key => key.Id).Distinct().ToList();
            var rows = group.Key switch
            {
                DianCatalog.DocumentType => await Simple<TypeDocument>(ids, ct),
                DianCatalog.OperationType => await Simple<TypeOperation>(ids, ct),
                DianCatalog.Currency => await Simple<TypeCurrency>(ids, ct),
                DianCatalog.OrganizationType => await Simple<TypeOrganization>(ids, ct),
                DianCatalog.IdentificationType => await Simple<TypeDocumentIdentification>(ids, ct),
                DianCatalog.Regime => await Simple<TypeRegime>(ids, ct),
                DianCatalog.Liability => await Simple<TypeLiability>(ids, ct),
                DianCatalog.Tax => await Simple<Tax>(ids, ct),
                DianCatalog.Municipality => await Municipalities(ids, ct),
                DianCatalog.Country => await Simple<Country>(ids, ct),
                DianCatalog.Language => await Simple<Language>(ids, ct),
                DianCatalog.PaymentForm => await Simple<PaymentForm>(ids, ct),
                DianCatalog.PaymentMethod => await Simple<PaymentMethod>(ids, ct),
                DianCatalog.Discount => await Simple<Discount>(ids, ct),
                DianCatalog.UnitMeasure => await Simple<UnitMeasure>(ids, ct),
                DianCatalog.ItemIdentificationType => await db.Set<TypeItemIdentification>().AsNoTracking()
                    .Where(row => ids.Contains(row.Id))
                    .Select(row => new KeyValuePair<int, CatalogItem>(row.Id, new CatalogItem(row.Code, row.Name, row.CodeAgency, null)))
                    .ToListAsync(ct),
                DianCatalog.ReferencePrice => await Simple<ReferencePrice>(ids, ct),
                DianCatalog.CreditNoteDiscrepancy => await Simple<CreditNoteDiscrepancyResponse>(ids, ct),
                DianCatalog.DebitNoteDiscrepancy => await Simple<DebitNoteDiscrepancyResponse>(ids, ct),
                _ => throw new ArgumentOutOfRangeException(nameof(keys), group.Key, "Unknown catalog."),
            };
            foreach (var (id, item) in rows)
            {
                found[new CatalogKey(group.Key, id)] = item;
            }
        }

        return found;
    }

    private Task<List<KeyValuePair<int, CatalogItem>>> Simple<T>(List<int> ids, CancellationToken ct)
        where T : CatalogEntry =>
        db.Set<T>().AsNoTracking()
            .Where(row => ids.Contains(row.Id))
            .Select(row => new KeyValuePair<int, CatalogItem>(row.Id, new CatalogItem(row.Code, row.Name, null, null)))
            .ToListAsync(ct);

    private async Task<List<KeyValuePair<int, CatalogItem>>> Municipalities(List<int> ids, CancellationToken ct)
    {
        var rows = await db.Set<Municipality>().AsNoTracking()
            .Where(municipality => ids.Contains(municipality.Id))
            .Join(db.Set<Department>().AsNoTracking(), municipality => municipality.DepartmentId, department => department.Id,
                (municipality, department) => new { municipality.Id, municipality.Code, municipality.Name, DepartmentCode = department.Code, DepartmentName = department.Name })
            .ToListAsync(ct);
        return rows.Select(row => new KeyValuePair<int, CatalogItem>(
            row.Id, new CatalogItem(row.Code, row.Name, null, new CatalogItem(row.DepartmentCode, row.DepartmentName)))).ToList();
    }
}

/// <summary>Documentos emitidos sobre el contexto de la solicitud; el decorador de transacción los guarda.</summary>
internal sealed class EfDocumentStore(ImpiFactDbContext db, IBlobStorage storage) : IDocumentStore
{
    public void Add(DocumentGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        db.Set<Document>().Add(graph.Document);
        db.Set<DocumentLine>().AddRange(graph.Lines);
        db.Set<DocumentTax>().AddRange(graph.Taxes);
        db.Set<DocumentLineTax>().AddRange(graph.LineTaxes);
        db.Set<DocumentAllowanceCharge>().AddRange(graph.AllowanceCharges);
        db.Set<DocumentPayment>().AddRange(graph.Payments);
        db.Set<DocumentReference>().AddRange(graph.References);
    }

    public Task<StoredDocument?> FindByNumberAsync(Guid companyId, DocumentNumberKey number, CancellationToken ct) =>
        Stored(Documents(companyId).Where(document => document.DocumentTypeId == number.DocumentTypeId
            && document.TypeEnvironmentId == number.TypeEnvironmentId
            && document.Prefix == number.Prefix
            && document.Number == number.Number), ct);

    public Task<StoredDocument?> FindByIdAsync(Guid companyId, Guid documentId, CancellationToken ct) =>
        Stored(Documents(companyId).Where(document => document.Id == documentId), ct);

    public Task<Guid?> FindIdByCufeAsync(Guid companyId, string cufe, CancellationToken ct) =>
        Documents(companyId).Where(document => document.Cufe == cufe).Select(document => (Guid?)document.Id).FirstOrDefaultAsync(ct);

    public async Task<byte[]?> ReadFileAsync(Guid companyId, Guid documentId, DocumentFileKind kind, CancellationToken ct)
    {
        var key = await Files(companyId)
            .Where(file => file.DocumentId == documentId && file.Kind == kind)
            .OrderBy(file => file.Revision)
            .Select(file => file.StorageKey)
            .FirstOrDefaultAsync(ct);
        if (key is null)
        {
            return null;
        }

        await using var stream = await storage.OpenReadAsync(key, ct);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }

    private IQueryable<Document> Documents(Guid companyId) =>
        db.Set<Document>().AsNoTracking().IgnoreQueryFilters([QueryFilters.Tenant]).Where(document => document.CompanyId == companyId);

    private IQueryable<DocumentFile> Files(Guid companyId) =>
        db.Set<DocumentFile>().AsNoTracking().IgnoreQueryFilters([QueryFilters.Tenant]).Where(file => file.CompanyId == companyId);

    private async Task<StoredDocument?> Stored(IQueryable<Document> query, CancellationToken ct)
    {
        var document = await query.FirstOrDefaultAsync(ct);
        if (document is null)
        {
            return null;
        }

        var requestHash = await Files(document.CompanyId)
            .Where(file => file.DocumentId == document.Id && file.Kind == DocumentFileKind.RequestJson)
            .OrderBy(file => file.Revision)
            .Select(file => file.Sha256)
            .FirstOrDefaultAsync(ct);
        return new StoredDocument(
            document.Id, document.DocumentTypeId, document.TypeEnvironmentId, document.ResolutionId, document.Prefix, document.Number,
            document.FullNumber, document.Status, document.Cufe, document.UuidAlgorithm, requestHash);
    }
}

/// <summary>Llaves de idempotencia en <c>idempotency_keys</c>; la respuesta guardada solo lleva el id del documento.</summary>
internal sealed class EfIdempotencyKeys(ImpiFactDbContext db, ITenantContext tenant) : IIdempotencyKeys
{
    private const string DocumentIdField = "document_id";

    public async Task<IdempotencyRecord?> FindAsync(Guid companyId, string key, CancellationToken ct)
    {
        var row = await db.Set<IdempotencyKey>().AsNoTracking().IgnoreQueryFilters([QueryFilters.Tenant])
            .FirstOrDefaultAsync(entry => entry.CompanyId == companyId && entry.Key == key, ct);
        if (row is null)
        {
            return null;
        }

        var documentId = row.ResponseBody?[DocumentIdField]?.GetValue<string>() is { } text && Guid.TryParse(text, out var id) ? id : (Guid?)null;
        return new IdempotencyRecord(row.RequestHash, documentId);
    }

    public void Add(Guid companyId, string key, string requestSha256, Guid documentId, DateTimeOffset expiresAt)
    {
        TenantWriteCheck.EnsureCanWrite(tenant, companyId, nameof(IdempotencyKey));
        db.Set<IdempotencyKey>().Add(new IdempotencyKey
        {
            CompanyId = companyId,
            Key = key,
            RequestHash = requestSha256,
            ResponseStatus = 200,
            ResponseBody = new JsonObject { [DocumentIdField] = documentId.ToString("D") },
            ExpiresAt = expiresAt,
        });
    }
}

/// <summary>URL del QR por ambiente, de la sección <c>Dian</c> de la configuración.</summary>
internal sealed class DianQrUrls(IOptionsService<DianOptions> options) : IDianQrUrls
{
    public string ForEnvironment(int typeEnvironmentId) =>
        typeEnvironmentId == TypeEnvironment.Production ? options.Value.ProduccionQrUrl : options.Value.HabilitacionQrUrl;
}
