using ImpiFact.Application.Features.Catalogs;
using ImpiFact.Domain.Catalog;
using ImpiFact.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ImpiFact.Infrastructure.Catalogs;

/// <summary>
/// Filas de los catálogos DIAN (Dirección de Impuestos y Aduanas Nacionales) sembrados, con EF Core y sin seguimiento:
/// una consulta por solicitud, solo con las columnas que publica la API.
/// </summary>
internal sealed class EfCatalogRows(ImpiFactDbContext db) : ICatalogRows
{
    public async Task<IReadOnlyList<CatalogRow>> ListAsync(CatalogKind kind, CancellationToken ct) => kind switch
    {
        CatalogKind.IdentificationTypes => await Simple<TypeDocumentIdentification>(ct),
        CatalogKind.OrganizationTypes => await Simple<TypeOrganization>(ct),
        CatalogKind.Regimes => await Simple<TypeRegime>(ct),
        CatalogKind.Liabilities => await Simple<TypeLiability>(ct),
        CatalogKind.Municipalities => await db.Set<Municipality>().AsNoTracking().OrderBy(row => row.Id)
            .Select(row => new CatalogRow(row.Id, row.Code, row.Name, row.IsActive, row.DepartmentId, null, null))
            .ToListAsync(ct),
        CatalogKind.Departments => await db.Set<Department>().AsNoTracking().OrderBy(row => row.Id)
            .Select(row => new CatalogRow(row.Id, row.Code, row.Name, row.IsActive, null, row.CountryId, null))
            .ToListAsync(ct),
        CatalogKind.Countries => await Simple<Country>(ct),
        CatalogKind.UnitMeasures => await Simple<UnitMeasure>(ct),
        CatalogKind.Taxes => await db.Set<Tax>().AsNoTracking().OrderBy(row => row.Id)
            .Select(row => new CatalogRow(row.Id, row.Code, row.Name, row.IsActive, null, null, row.Description))
            .ToListAsync(ct),
        CatalogKind.PaymentForms => await Simple<PaymentForm>(ct),
        CatalogKind.PaymentMethods => await Simple<PaymentMethod>(ct),
        CatalogKind.DocumentTypes => await Simple<TypeDocument>(ct),
        CatalogKind.Currencies => await Simple<TypeCurrency>(ct),
        CatalogKind.ItemIdentificationTypes => await Simple<TypeItemIdentification>(ct),
        CatalogKind.CreditNoteDiscrepancyResponses => await Simple<CreditNoteDiscrepancyResponse>(ct),
        CatalogKind.DebitNoteDiscrepancyResponses => await Simple<DebitNoteDiscrepancyResponse>(ct),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown catalog."),
    };

    private Task<List<CatalogRow>> Simple<T>(CancellationToken ct)
        where T : CatalogEntry =>
        db.Set<T>().AsNoTracking().OrderBy(row => row.Id)
            .Select(row => new CatalogRow(row.Id, row.Code, row.Name, row.IsActive, null, null, null))
            .ToListAsync(ct);
}

public static class CatalogsServiceCollectionExtensions
{
    /// <summary>Registra la lectura de los catálogos DIAN que publica la API (<c>GET /api/v1/catalogs/{name}</c>).</summary>
    public static IServiceCollection AddCatalogReads(this IServiceCollection services)
    {
        services.TryAddScoped<ICatalogRows, EfCatalogRows>();
        return services;
    }
}
