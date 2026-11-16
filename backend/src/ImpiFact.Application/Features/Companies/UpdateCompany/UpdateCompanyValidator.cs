using ImpiFact.Application.Abstractions.Companies;

namespace ImpiFact.Application.Features.Companies.UpdateCompany;

/// <summary>Las mismas reglas de los datos de la empresa que el alta.</summary>
public sealed class UpdateCompanyValidator(ICatalogLookup catalogs) : CompanyDataValidator<UpdateCompanyCommand>(catalogs);
