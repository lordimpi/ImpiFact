using ImpiFact.Application.Abstractions.Companies;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Companies;
using ImpiFact.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Infrastructure.Companies;

/// <summary>
/// Repositorio del módulo Company sobre EF Core (Entity Framework, el mapeador de objetos a tablas). Las filas que
/// devuelve quedan rastreadas: el caso de uso las cambia y el decorador de transacción las guarda.
/// </summary>
/// <remarks>
/// Las búsquedas por <c>companyId</c> quitan solo el filtro de tenant y ponen la empresa en el WHERE: la plataforma no
/// tiene empresa en el contexto y con el filtro no vería las filas de la empresa que administra. La empresa la pasa
/// siempre el caso de uso desde una fuente confiable, y la guardia de escritura sigue impidiendo que una empresa
/// escriba filas de otra.
/// </remarks>
internal sealed class EfCompanyRepository(ImpiFactDbContext db) : ICompanyRepository
{
    public Task<Company?> FindByIdAsync(Guid companyId, CancellationToken ct) =>
        db.Set<Company>().FirstOrDefaultAsync(company => company.Id == companyId, ct);

    public Task<Company?> FindByNitAsync(string nit, CancellationToken ct) =>
        db.Set<Company>().FirstOrDefaultAsync(company => company.IdentificationNumber == nit, ct);

    public Task<bool> IsEmailTakenAsync(string email, Guid? exceptCompanyId, CancellationToken ct)
    {
        var normalized = email.ToLowerInvariant();
        return db.Set<Company>().AnyAsync(company => company.Email.ToLower() == normalized && company.Id != exceptCompanyId, ct);
    }

    public async Task<IReadOnlyList<CompanyLiability>> GetLiabilitiesAsync(Guid companyId, CancellationToken ct) =>
        await OfCompany<CompanyLiability>(companyId).ToListAsync(ct);

    public async Task<IReadOnlyList<CompanyEnvironment>> GetEnvironmentsAsync(Guid companyId, CancellationToken ct) =>
        await OfCompany<CompanyEnvironment>(companyId).ToListAsync(ct);

    public async Task<IReadOnlyList<CompanySoftware>> GetSoftwareAsync(Guid companyId, CancellationToken ct) =>
        await OfCompany<CompanySoftware>(companyId).ToListAsync(ct);

    public Task<Certificate?> GetActiveCertificateAsync(Guid companyId, CancellationToken ct) =>
        OfCompany<Certificate>(companyId).FirstOrDefaultAsync(certificate => certificate.IsActive, ct);

    public async Task<IReadOnlyList<Resolution>> GetResolutionsAsync(
        Guid companyId, int documentTypeId, int typeEnvironmentId, CancellationToken ct) =>
        await OfCompany<Resolution>(companyId)
            .Where(resolution => resolution.DocumentTypeId == documentTypeId && resolution.TypeEnvironmentId == typeEnvironmentId)
            .ToListAsync(ct);

    public void Add<TEntity>(TEntity entity)
        where TEntity : class => db.Set<TEntity>().Add(entity);

    public void Remove<TEntity>(TEntity entity)
        where TEntity : class => db.Set<TEntity>().Remove(entity);

    private IQueryable<TEntity> OfCompany<TEntity>(Guid companyId)
        where TEntity : class, ITenantOwned =>
        db.Set<TEntity>().IgnoreQueryFilters([QueryFilters.Tenant]).Where(entity => entity.CompanyId == companyId);
}
