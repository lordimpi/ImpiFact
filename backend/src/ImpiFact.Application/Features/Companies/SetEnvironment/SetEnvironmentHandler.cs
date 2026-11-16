using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Companies;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Companies;

namespace ImpiFact.Application.Features.Companies.SetEnvironment;

/// <summary>
/// Cambia el ambiente de cada familia pedida. Como el PHP, exige que la empresa ya tenga software. El PHP además
/// cambiaba la URL de su única fila de software; aquí hay una fila por ambiente, así que al pasar a un ambiente sin
/// software se copia el de la misma familia (identificador y PIN cifrado) con la URL del nuevo ambiente y sin set de
/// pruebas, que solo es de habilitación. La fila del ambiente anterior se conserva.
/// </summary>
internal sealed class SetEnvironmentHandler(ICompanyRepository companies, ITenantContext tenant, IDianServiceUrls dianUrls)
    : IHandler<SetEnvironmentCommand, EnvironmentsResponse>
{
    public async Task<Result<EnvironmentsResponse>> HandleAsync(SetEnvironmentCommand request, CancellationToken ct)
    {
        var own = await CompanyAccess.OwnCompanyAsync(companies, tenant, request.Nit, ct);
        if (!own.IsSuccess)
        {
            return own.Error;
        }

        var companyId = own.Value.Id;
        var software = (await companies.GetSoftwareAsync(companyId, ct)).ToList();
        if (software.Count == 0)
        {
            return CompanyErrors.SoftwareMissing;
        }

        var environments = (await companies.GetEnvironmentsAsync(companyId, ct)).ToList();
        foreach (var (kind, typeEnvironmentId) in request.Requested())
        {
            var environment = environments.FirstOrDefault(row => row.DocumentKind == kind);
            if (environment is null)
            {
                environment = new CompanyEnvironment { CompanyId = companyId, DocumentKind = kind, TypeEnvironmentId = typeEnvironmentId };
                companies.Add(environment);
                environments.Add(environment);
            }
            else
            {
                environment.TypeEnvironmentId = typeEnvironmentId;
            }

            CarrySoftware(software, companyId, kind, typeEnvironmentId);
        }

        return new EnvironmentsResponse
        {
            Environments = environments.OrderBy(row => row.DocumentKind).Select(CompanyMapper.Map<EnvironmentResponse>).ToList(),
        };
    }

    private void CarrySoftware(List<CompanySoftware> software, Guid companyId, DocumentKind kind, int typeEnvironmentId)
    {
        if (software.Any(row => row.DocumentKind == kind && row.TypeEnvironmentId == typeEnvironmentId)
            || software.FirstOrDefault(row => row.DocumentKind == kind) is not { } source)
        {
            return;
        }

        var copy = new CompanySoftware
        {
            CompanyId = companyId,
            DocumentKind = kind,
            TypeEnvironmentId = typeEnvironmentId,
            SoftwareIdentifier = source.SoftwareIdentifier,
            PinCipher = source.PinCipher,
            DianUrl = dianUrls.ForEnvironment(typeEnvironmentId),
        };
        companies.Add(copy);
        software.Add(copy);
    }
}
