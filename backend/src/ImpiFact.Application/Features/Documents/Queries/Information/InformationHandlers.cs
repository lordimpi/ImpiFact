using System.Globalization;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Results;

namespace ImpiFact.Application.Features.Documents.Queries.Information;

/// <summary>
/// Documentos validados (<c>state_document_id = 1</c>) por tipo en el rango de fechas. Como el PHP, devuelve todos los
/// del rango sin paginar; para volúmenes grandes está la variante por página o el listado canónico.
/// </summary>
internal sealed class GetInformationHandler(IDocumentReads reads, ITenantContext tenant) : IHandler<GetInformationQuery, InformationResponse>
{
    public async Task<Result<InformationResponse>> HandleAsync(GetInformationQuery request, CancellationToken ct)
    {
        var own = await DocumentAccess.OwnCompanyAsync(reads, tenant, request.Nit, ct);
        if (!own.IsSuccess)
        {
            return own.Error;
        }

        var (from, to) = LegacyDateRange.Resolve(request.From, request.To);
        var range = ColombiaDates.Range(from, to);
        var groups = new List<InformationGroup>();
        foreach (var (typeId, name) in LegacyTypes.Information)
        {
            var documents = await reads.ListByTypeAsync(own.Value.Id, typeId, LegacyDocumentState.Validated, range, page: null, ct);
            groups.Add(LegacyMapping.Group(own.Value, typeId, name, documents));
        }

        return new InformationResponse { Data = groups, Company = own.Value.Name };
    }
}

/// <summary>
/// Variante por página: 100 documentos validados por tipo, del más antiguo al más nuevo y sin filtro de fechas. Una
/// página menor que 1 se toma como la primera, como el PHP.
/// </summary>
internal sealed class GetInformationPageHandler(IDocumentReads reads, ITenantContext tenant) : IHandler<GetInformationPageQuery, InformationResponse>
{
    public async Task<Result<InformationResponse>> HandleAsync(GetInformationPageQuery request, CancellationToken ct)
    {
        var own = await DocumentAccess.OwnCompanyAsync(reads, tenant, request.Nit, ct);
        if (!own.IsSuccess)
        {
            return own.Error;
        }

        var page = Math.Max(1, int.Parse(request.Page, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture));
        var window = new PageWindow((int)Math.Min(int.MaxValue, (page - 1L) * LegacyTypes.PageSize), LegacyTypes.PageSize);
        var groups = new List<InformationGroup>();
        foreach (var (typeId, name) in LegacyTypes.Information)
        {
            var documents = await reads.ListByTypeAsync(own.Value.Id, typeId, LegacyDocumentState.Validated, range: null, window, ct);
            groups.Add(LegacyMapping.Group(own.Value, typeId: null, name, documents));
        }

        return new InformationResponse { Data = groups, Company = own.Value.Name };
    }
}

/// <summary>Cantidad de documentos validados de cada uno de los 8 tipos del PHP en el rango de fechas.</summary>
internal sealed class GetTotalDocumentsHandler(IDocumentReads reads, ITenantContext tenant) : IHandler<GetTotalDocumentsQuery, TotalDocumentsResponse>
{
    public async Task<Result<TotalDocumentsResponse>> HandleAsync(GetTotalDocumentsQuery request, CancellationToken ct)
    {
        var own = await DocumentAccess.OwnCompanyAsync(reads, tenant, request.Nit, ct);
        if (!own.IsSuccess)
        {
            return own.Error;
        }

        var (from, to) = LegacyDateRange.Resolve(request.From, request.To);
        var counts = await reads.CountByTypeAsync(
            own.Value.Id, LegacyTypes.Totals.Select(type => type.Id).ToList(), LegacyDocumentState.Validated, ColombiaDates.Range(from, to), ct);
        return new TotalDocumentsResponse
        {
            Data = LegacyTypes.Totals.Select(type => new DocumentTypeTotal { Name = type.Name, Count = counts.GetValueOrDefault(type.Id) }).ToList(),
            Company = own.Value.Name,
        };
    }
}

/// <summary>Copia los documentos leídos a la forma heredada.</summary>
internal static class LegacyMapping
{
    public static InformationGroup Group(DocumentCompany company, int? typeId, string name, IReadOnlyList<DocumentSummary> documents) => new()
    {
        TypeDocumentId = typeId,
        Name = name,
        Count = documents.Count,
        Documents = documents.Select(document => Document(company, document)).ToList(),
    };

    private static LegacyDocument Document(DocumentCompany company, DocumentSummary document) => new()
    {
        Id = document.Id,
        IdentificationNumber = company.IdentificationNumber,
        StateDocumentId = LegacyDocumentState.Of(document.Status),
        TypeDocumentId = document.DocumentTypeId,
        Prefix = document.Prefix,
        Number = document.Number,
        Cufe = document.Cufe,
        Customer = document.Customer.IdentificationNumber,
        Client = new LegacyClient
        {
            IdentificationNumber = document.Customer.IdentificationNumber,
            Dv = document.Customer.Dv,
            Name = document.Customer.Name,
            Phone = document.Customer.Phone,
            Address = document.Customer.Address,
            Email = document.Customer.Email,
            MerchantRegistration = document.Customer.MerchantRegistration,
            TypeDocumentIdentificationId = document.Customer.TypeDocumentIdentificationId,
            TypeOrganizationId = document.Customer.TypeOrganizationId,
            TypeLiabilityId = document.Customer.TypeLiabilityId,
            TypeRegimeId = document.Customer.TypeRegimeId,
            MunicipalityId = document.Customer.MunicipalityId,
        },
        CurrencyId = document.CurrencyId,
        DateIssue = document.IssueDate.ToDateTime(document.IssueTime).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        Sale = document.Totals.PayableAmount,
        TotalDiscount = document.Totals.AllowanceTotalAmount,
        TotalTax = document.Totals.TaxInclusiveAmount - document.Totals.TaxExclusiveAmount,
        Subtotal = document.Totals.LineExtensionAmount,
        Total = document.Totals.PayableAmount,
        AmbientId = document.TypeEnvironmentId,
    };
}
