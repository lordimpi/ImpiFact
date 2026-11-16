using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Companies;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Companies.CreateCompany;

namespace ImpiFact.Application.Features.Companies.UpdateCompany;

/// <summary>
/// Actualiza los datos de la empresa de la credencial con las mismas reglas y valores por defecto del alta. No toca
/// los ambientes ni crea llaves.
/// </summary>
internal sealed class UpdateCompanyHandler(ICompanyRepository companies, ITenantContext tenant)
    : IHandler<UpdateCompanyCommand, CompanySavedResponse>
{
    public async Task<Result<CompanySavedResponse>> HandleAsync(UpdateCompanyCommand request, CancellationToken ct)
    {
        var own = await CompanyAccess.OwnCompanyAsync(companies, tenant, request.Nit, ct);
        if (!own.IsSuccess)
        {
            return own.Error;
        }

        var company = own.Value;
        if (await companies.IsEmailTakenAsync(request.Email!, company.Id, ct))
        {
            return CompanyErrors.EmailTaken;
        }

        var liabilityIds = request.NormalizedLiabilityIds();
        CompanyWriter.Apply(company, request, CompanyWriter.TaxIdFor(request, company.TaxId));
        await CompanyWriter.SyncLiabilitiesAsync(companies, company.Id, liabilityIds, ct);
        var environments = await companies.GetEnvironmentsAsync(company.Id, ct);
        return new CompanySavedResponse { Created = false, Company = CompanyWriter.Response(company, liabilityIds, environments) };
    }
}
