using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Companies;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Abstractions.Protection;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Companies.CreateCompany;
using ImpiFact.Domain.Catalog;
using ImpiFact.Domain.Companies;

namespace ImpiFact.Application.Tests.Features.Companies;

/// <summary>
/// Repositorio en memoria: <see cref="Stored"/> son las filas «ya guardadas» y <see cref="Added"/> /
/// <see cref="Removed"/> lo que el caso de uso pidió agregar o borrar, como lo vería el decorador de transacción.
/// </summary>
public sealed class FakeCompanyRepository : ICompanyRepository
{
    public List<object> Stored { get; } = [];

    public List<object> Added { get; } = [];

    public List<object> Removed { get; } = [];

    public IEnumerable<T> AddedOf<T>() => Added.OfType<T>();

    public Task<Company?> FindByIdAsync(Guid companyId, CancellationToken ct) =>
        Task.FromResult(Stored.OfType<Company>().SingleOrDefault(company => company.Id == companyId));

    public Task<Company?> FindByNitAsync(string nit, CancellationToken ct) =>
        Task.FromResult(Stored.OfType<Company>().SingleOrDefault(company => company.IdentificationNumber == nit));

    public Task<bool> IsEmailTakenAsync(string email, Guid? exceptCompanyId, CancellationToken ct) =>
        Task.FromResult(Stored.OfType<Company>().Any(company =>
            company.Id != exceptCompanyId && string.Equals(company.Email, email, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<CompanyLiability>> GetLiabilitiesAsync(Guid companyId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CompanyLiability>>(Stored.OfType<CompanyLiability>().Where(row => row.CompanyId == companyId).ToList());

    public Task<IReadOnlyList<CompanyEnvironment>> GetEnvironmentsAsync(Guid companyId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CompanyEnvironment>>(Stored.OfType<CompanyEnvironment>().Where(row => row.CompanyId == companyId).ToList());

    public Task<IReadOnlyList<CompanySoftware>> GetSoftwareAsync(Guid companyId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CompanySoftware>>(Stored.OfType<CompanySoftware>().Where(row => row.CompanyId == companyId).ToList());

    public Task<Certificate?> GetActiveCertificateAsync(Guid companyId, CancellationToken ct) =>
        Task.FromResult(Stored.OfType<Certificate>().SingleOrDefault(row => row.CompanyId == companyId && row.IsActive));

    public Task<IReadOnlyList<Resolution>> GetResolutionsAsync(Guid companyId, int documentTypeId, int typeEnvironmentId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Resolution>>(Stored.OfType<Resolution>()
            .Where(row => row.CompanyId == companyId && row.DocumentTypeId == documentTypeId && row.TypeEnvironmentId == typeEnvironmentId)
            .ToList());

    public void Add<TEntity>(TEntity entity)
        where TEntity : class => Added.Add(entity);

    public void Remove<TEntity>(TEntity entity)
        where TEntity : class => Removed.Add(entity);
}

/// <summary>Catálogos donde todo identificador existe salvo los marcados como inexistentes.</summary>
public sealed class FakeCatalogLookup : ICatalogLookup
{
    private readonly HashSet<(Type, int)> _missing = [];

    public FakeCatalogLookup Missing<TCatalog>(int id)
        where TCatalog : CatalogEntry
    {
        _missing.Add((typeof(TCatalog), id));
        return this;
    }

    public Task<bool> ExistsAsync<TCatalog>(int id, CancellationToken ct)
        where TCatalog : CatalogEntry => Task.FromResult(!_missing.Contains((typeof(TCatalog), id)));
}

public sealed class FakeApiKeyGenerator : IApiKeyGenerator
{
    public int Generated { get; private set; }

    public GeneratedApiKey Generate()
    {
        Generated++;
        return new GeneratedApiKey($"apk_plain_{Generated}", "apk_plain_" + Generated, new string('a', 64));
    }
}

/// <summary>Cifrado de mentira que deja ver el propósito usado; nunca devuelve el texto claro tal cual.</summary>
public sealed class FakeSecretProtector : ISecretProtector
{
    public string Protect(string purpose, string plain) => $"cipher({purpose}):{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plain))}";

    public string Unprotect(string purpose, string cipher) => throw new NotSupportedException();
}

public sealed class FakeClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}

public sealed class FakeDianUrls : IDianServiceUrls
{
    public const string Production = "https://prod.example/dian";

    public const string Habilitation = "https://hab.example/dian";

    public string ForEnvironment(int typeEnvironmentId) => typeEnvironmentId == 1 ? Production : Habilitation;
}

public sealed class FakeCertificateInspector : ICertificateInspector
{
    public Result<CertificateDetails> Outcome { get; set; } = new CertificateDetails(
        new string('F', 64), "CN=Empresa, C=CO", "CN=CA, C=CO", "01",
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), 46);

    public List<(byte[] Pfx, string Password)> Inspected { get; } = [];

    public Result<CertificateDetails> Inspect(byte[] pfx, string password)
    {
        Inspected.Add((pfx, password));
        return Outcome;
    }
}

public sealed class FakeCertificateStore : ICertificateStore
{
    public List<(Guid CompanyId, byte[] Pfx, string Password)> Saved { get; } = [];

    public Task<string> SaveAsync(Guid companyId, byte[] pfx, string password, CancellationToken ct)
    {
        Saved.Add((companyId, pfx, password));
        return Task.FromResult($"local:{companyId:N}/{Saved.Count}");
    }

    public Task<System.Security.Cryptography.X509Certificates.X509Certificate2> LoadAsync(Guid companyId, string secretRef, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task DeleteAsync(Guid companyId, string secretRef, CancellationToken ct) => throw new NotSupportedException();
}

/// <summary>Datos de prueba válidos.</summary>
public static class CompanySamples
{
    /// <summary>NIT de prueba con su dígito de verificación (calculado con el algoritmo de la DIAN).</summary>
    public const string Nit = "900123456";

    public const string Dv = "8";

    public static CreateCompanyCommand Create(string nit = Nit, string dv = Dv, string email = "empresa@example.com") => new()
    {
        Nit = nit,
        Dv = dv,
        TypeDocumentIdentificationId = 6,
        TypeOrganizationId = 1,
        TypeRegimeId = 2,
        TypeLiabilityId = 14,
        BusinessName = "EMPRESA DE PRUEBAS",
        MerchantRegistration = "1234567-12",
        MunicipalityId = 1006,
        Address = "CALLE 1 1C 1",
        Phone = "3216547",
        Email = email,
    };

    public static Company Company(string nit = Nit, string email = "empresa@example.com") => new()
    {
        Id = Guid.CreateVersion7(),
        IdentificationNumber = nit,
        Dv = Dv,
        Name = "EMPRESA ANTERIOR",
        Email = email,
        Phone = "3000000",
        Address = "CALLE 2",
        LanguageId = 1,
        CountryId = 1,
        MunicipalityId = 1,
        TaxId = 4,
        TypeCurrencyId = 1,
        TypeOperationId = 1,
        TypeDocumentIdentificationId = 6,
        TypeOrganizationId = 1,
        TypeRegimeId = 1,
    };

    public static FakeTenant Platform() => new() { Actor = ActorType.Platform };

    public static FakeTenant CompanyKey(Guid companyId) => new() { Actor = ActorType.ApiKey, CompanyId = companyId };

    public static FakeTenant PanelUser(Guid companyId, Guid userId) => new() { Actor = ActorType.User, CompanyId = companyId, UserId = userId };
}

public sealed class FakeCompanyReads : ICompanyReads
{
    public List<(Guid CompanyId, CertificateMetadata Certificate)> Certificates { get; } = [];

    public Task<IReadOnlyList<CertificateMetadata>> ListCertificatesAsync(Guid companyId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CertificateMetadata>>(
            Certificates.Where(row => row.CompanyId == companyId).Select(row => row.Certificate).ToList());
}

public sealed class FakeTenant : ITenantContext
{
    public Guid? CompanyId { get; set; }

    public Guid? UserId { get; set; }

    public ActorType Actor { get; set; }
}
