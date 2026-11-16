using ImpiFact.Application.Abstractions.Companies;
using ImpiFact.Domain.Catalog;
using ImpiFact.Domain.Companies;
using ImpiFact.Infrastructure.Options;
using ImpiFact.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Infrastructure.Companies;

/// <summary>Revisa en la base que un identificador exista en un catálogo DIAN.</summary>
internal sealed class EfCatalogLookup(ImpiFactDbContext db) : ICatalogLookup
{
    public Task<bool> ExistsAsync<TCatalog>(int id, CancellationToken ct)
        where TCatalog : CatalogEntry => db.Set<TCatalog>().AnyAsync(entry => entry.Id == id, ct);
}

/// <summary>
/// Consultas del módulo Company sin rastreo de cambios. Van con EF Core y no con Dapper porque Infrastructure
/// todavía no referencia Dapper; llevan la empresa en el WHERE como toda consulta de lectura.
/// </summary>
internal sealed class EfCompanyReads(ImpiFactDbContext db) : ICompanyReads
{
    public async Task<IReadOnlyList<CertificateMetadata>> ListCertificatesAsync(Guid companyId, CancellationToken ct) =>
        await db.Set<Certificate>()
            .AsNoTracking()
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(certificate => certificate.CompanyId == companyId)
            .OrderByDescending(certificate => certificate.IsActive)
            .ThenByDescending(certificate => certificate.CreatedAt)
            .Select(certificate => new CertificateMetadata(
                certificate.Id, certificate.Name, certificate.Thumbprint, certificate.Subject, certificate.Issuer,
                certificate.NotBefore, certificate.NotAfter, certificate.IsActive, certificate.CreatedAt))
            .ToListAsync(ct);
}

/// <summary>URL del servicio de la DIAN por ambiente, tomada de <see cref="DianOptions"/>.</summary>
internal sealed class DianServiceUrls(IOptionsService<DianOptions> options) : IDianServiceUrls
{
    public string ForEnvironment(int typeEnvironmentId) =>
        typeEnvironmentId == 1 ? options.Value.ProduccionUrl : options.Value.HabilitacionUrl;
}
