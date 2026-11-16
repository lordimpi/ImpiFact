using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Tests.Features.Documents;

/// <summary>Consultas en memoria: guarda lo que pidió el caso de uso y devuelve las filas preparadas.</summary>
public sealed class FakeDocumentReads : IDocumentReads
{
    public List<DocumentCompany> Companies { get; } = [];

    public List<(int TypeId, IssuedRange? Range, PageWindow? Page, IReadOnlyCollection<DocumentStatus> Statuses)> ListByTypeCalls { get; } = [];

    public List<(IReadOnlyCollection<int> TypeIds, IssuedRange Range, IReadOnlyCollection<DocumentStatus> Statuses)> CountCalls { get; } = [];

    public List<DocumentListFilter> ListCalls { get; } = [];

    public Dictionary<int, List<DocumentSummary>> ByType { get; } = [];

    public Dictionary<int, long> Counts { get; } = [];

    public List<DocumentSummary> Listed { get; } = [];

    public List<DocumentDetail> Details { get; } = [];

    public Task<DocumentCompany?> FindCompanyAsync(Guid companyId, CancellationToken ct) =>
        Task.FromResult(Companies.SingleOrDefault(company => company.Id == companyId));

    public Task<IReadOnlyList<DocumentSummary>> ListByTypeAsync(
        Guid companyId, int documentTypeId, IReadOnlyCollection<DocumentStatus> statuses, IssuedRange? range, PageWindow? page, CancellationToken ct)
    {
        ListByTypeCalls.Add((documentTypeId, range, page, statuses));
        return Task.FromResult<IReadOnlyList<DocumentSummary>>(ByType.GetValueOrDefault(documentTypeId, []));
    }

    public Task<IReadOnlyDictionary<int, long>> CountByTypeAsync(
        Guid companyId, IReadOnlyCollection<int> documentTypeIds, IReadOnlyCollection<DocumentStatus> statuses, IssuedRange range, CancellationToken ct)
    {
        CountCalls.Add((documentTypeIds, range, statuses));
        return Task.FromResult<IReadOnlyDictionary<int, long>>(Counts);
    }

    public Task<IReadOnlyList<DocumentSummary>> ListAsync(DocumentListFilter filter, CancellationToken ct)
    {
        ListCalls.Add(filter);
        return Task.FromResult<IReadOnlyList<DocumentSummary>>(Listed.Take(filter.Take).ToList());
    }

    public Task<DocumentDetail?> FindByIdAsync(Guid companyId, Guid documentId, CancellationToken ct) =>
        Task.FromResult(Details.SingleOrDefault(detail => detail.Header.Id == documentId));

    public Task<DocumentDetail?> FindByCufeAsync(Guid companyId, string cufe, CancellationToken ct) =>
        Task.FromResult(Details.SingleOrDefault(detail => detail.Header.Cufe == cufe));
}

public sealed class FakeDocumentTenant(Guid? companyId) : ITenantContext
{
    public Guid? CompanyId { get; } = companyId;

    public Guid? UserId => null;

    public ActorType Actor => ActorType.ApiKey;

    public IReadOnlySet<string> Scopes { get; } = new HashSet<string>();
}

public static class DocumentSamples
{
    public static DocumentSummary Summary(
        int typeId = 1, long number = 990000001, DocumentStatus status = DocumentStatus.Accepted, DateTimeOffset? issuedAt = null, Guid? id = null)
    {
        var at = issuedAt ?? new DateTimeOffset(2026, 1, 15, 15, 30, 45, TimeSpan.Zero);
        var colombia = at.ToOffset(TimeSpan.FromHours(-5));
        return new DocumentSummary(
            id ?? Guid.CreateVersion7(),
            typeId,
            2,
            "SETP",
            number,
            "SETP" + number,
            status,
            "cufe" + number,
            DateOnly.FromDateTime(colombia.DateTime),
            TimeOnly.FromDateTime(colombia.DateTime),
            at,
            35,
            new DocumentCustomer(3, "222222222222", null, "Consumidor final", "3000000000", "Calle 1", "cliente@example.com", 149, 2, 2, 117, null),
            new DocumentTotals(100m, 100m, 119m, 0m, 0m, 0m, 119m));
    }
}
