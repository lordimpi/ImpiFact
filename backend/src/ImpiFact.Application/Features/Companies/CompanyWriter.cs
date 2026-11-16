using ImpiFact.Application.Abstractions.Companies;
using ImpiFact.Domain.Companies;

namespace ImpiFact.Application.Features.Companies;

/// <summary>
/// Pasos comunes del alta y la actualización de la empresa, con los valores por defecto del PHP
/// («php-rules» 5.2.2; <c>ConfigurationController.php</c>, <c>store</c>).
/// </summary>
internal static class CompanyWriter
{
    /// <summary>Español (catálogo <c>languages</c>).</summary>
    public const int DefaultLanguageId = 79;

    /// <summary>Colombia (catálogo <c>countries</c>).</summary>
    public const int DefaultCountryId = 46;

    /// <summary>Peso colombiano (catálogo <c>type_currencies</c>).</summary>
    public const int DefaultCurrencyId = 35;

    /// <summary>Operación estándar (catálogo <c>type_operations</c>).</summary>
    public const int DefaultOperationId = 10;

    /// <summary>Régimen 2 del catálogo <c>type_regimes</c>; con él el impuesto por defecto es el 15.</summary>
    public const int RegimeWithTax15 = 2;

    /// <summary>
    /// Impuesto de la empresa: el que llega; si no llega, 15 cuando el régimen es 2 y si no el que ya tenía, o 1 en
    /// una empresa nueva (el PHP hace lo mismo al crear y al actualizar).
    /// </summary>
    public static int TaxIdFor(CompanyData data, int? currentTaxId) =>
        data.TaxId ?? (data.TypeRegimeId == RegimeWithTax15 ? 15 : currentTaxId ?? 1);

    public static Company NewCompany(CompanyData data, int taxId)
    {
        var company = new Company
        {
            Id = Guid.CreateVersion7(),
            IdentificationNumber = data.Nit!,
            Dv = data.Dv!,
            Name = data.BusinessName!,
            Email = data.Email!,
            Phone = data.Phone!,
            Address = data.Address!,
        };
        Apply(company, data, taxId);
        return company;
    }

    /// <summary>
    /// Copia los datos a la empresa. Como el PHP, los catálogos opcionales que no llegan vuelven a su valor por
    /// defecto también al actualizar.
    /// </summary>
    public static void Apply(Company company, CompanyData data, int taxId)
    {
        company.Dv = data.Dv!;
        company.Name = data.BusinessName!;
        company.Email = data.Email!;
        company.Phone = data.Phone!;
        company.Address = data.Address!;
        company.MerchantRegistration = data.MerchantRegistration;
        company.MunicipalityId = data.MunicipalityId!.Value;
        company.TypeDocumentIdentificationId = data.TypeDocumentIdentificationId!.Value;
        company.TypeOrganizationId = data.TypeOrganizationId!.Value;
        company.TypeRegimeId = data.TypeRegimeId!.Value;
        company.TaxId = taxId;
        company.LanguageId = data.LanguageId ?? DefaultLanguageId;
        company.CountryId = data.CountryId ?? DefaultCountryId;
        company.TypeCurrencyId = data.TypeCurrencyId ?? DefaultCurrencyId;
        company.TypeOperationId = data.TypeOperationId ?? DefaultOperationId;
    }

    /// <summary>Deja en <c>company_liabilities</c> exactamente las responsabilidades pedidas.</summary>
    public static async Task SyncLiabilitiesAsync(
        ICompanyRepository companies, Guid companyId, IReadOnlyList<int> wanted, CancellationToken ct)
    {
        var current = await companies.GetLiabilitiesAsync(companyId, ct);
        foreach (var liability in current.Where(liability => !wanted.Contains(liability.TypeLiabilityId)))
        {
            companies.Remove(liability);
        }

        foreach (var id in wanted.Where(id => current.All(liability => liability.TypeLiabilityId != id)))
        {
            companies.Add(new CompanyLiability { CompanyId = companyId, TypeLiabilityId = id });
        }
    }

    public static CompanyResponse Response(Company company, IReadOnlyList<int> liabilityIds, IEnumerable<CompanyEnvironment> environments) =>
        CompanyMapper.Map<CompanyResponse>(company) with
        {
            TypeLiabilityIds = liabilityIds,
            Environments = environments
                .OrderBy(environment => environment.DocumentKind)
                .Select(CompanyMapper.Map<EnvironmentResponse>)
                .ToList(),
        };
}
