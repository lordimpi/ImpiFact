using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Companies;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Protection;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Domain.Companies;

namespace ImpiFact.Application.Features.Companies.SetSoftware;

/// <summary>
/// Crea o actualiza el software de cada familia que trae la solicitud, en el ambiente actual de esa familia. El PIN
/// se cifra con <see cref="ISecretProtector"/> y el propósito del PIN de software; en la base solo queda
/// <c>pin_cipher</c>. Sin URL en la solicitud, un software nuevo toma la del servicio de la DIAN de su ambiente y uno
/// existente conserva la suya (el PHP la volvía a habilitación aunque la empresa estuviera en producción).
/// </summary>
internal sealed class SetSoftwareHandler(
    ICompanyRepository companies, ITenantContext tenant, ISecretProtector secrets, IDianServiceUrls dianUrls)
    : IHandler<SetSoftwareCommand, SoftwareSavedResponse>
{
    public async Task<Result<SoftwareSavedResponse>> HandleAsync(SetSoftwareCommand request, CancellationToken ct)
    {
        var own = await CompanyAccess.OwnCompanyAsync(companies, tenant, request.Nit, ct);
        if (!own.IsSuccess)
        {
            return own.Error;
        }

        var companyId = own.Value.Id;
        var environments = await companies.GetEnvironmentsAsync(companyId, ct);
        var software = (await companies.GetSoftwareAsync(companyId, ct)).ToList();
        var entries = request.Entries();

        // Un software nuevo necesita su identificador (el PIN ya lo exige el validador junto con él).
        var missing = entries
            .Where(entry => entry.Identifier is null && Find(software, entry, environments) is null)
            .Select(entry => new FieldError(entry.IdField, PhpMessages.Required(entry.IdField)))
            .ToList();
        if (missing.Count > 0)
        {
            return Error.Validation(missing);
        }

        var saved = new List<CompanySoftware>();
        foreach (var entry in entries)
        {
            var typeEnvironmentId = Environments.CurrentFor(environments, entry.Kind);
            var row = Find(software, entry, environments);
            if (row is null)
            {
                row = new CompanySoftware
                {
                    CompanyId = companyId,
                    DocumentKind = entry.Kind,
                    TypeEnvironmentId = typeEnvironmentId,
                    SoftwareIdentifier = entry.Identifier!,
                    PinCipher = secrets.Protect(SecretPurposes.SoftwarePin, entry.Pin!),
                    DianUrl = entry.Url ?? dianUrls.ForEnvironment(typeEnvironmentId),
                    TestSetId = entry.TestSetId,
                };
                companies.Add(row);
            }
            else
            {
                row.SoftwareIdentifier = entry.Identifier ?? row.SoftwareIdentifier;
                row.PinCipher = entry.Pin is null ? row.PinCipher : secrets.Protect(SecretPurposes.SoftwarePin, entry.Pin);
                row.DianUrl = entry.Url ?? row.DianUrl;
                row.TestSetId = entry.TestSetId ?? row.TestSetId;
            }

            saved.Add(row);
        }

        return new SoftwareSavedResponse { Software = saved.Select(CompanyMapper.Map<SoftwareResponse>).ToList() };
    }

    private static CompanySoftware? Find(List<CompanySoftware> software, SoftwareEntry entry, IReadOnlyList<CompanyEnvironment> environments)
    {
        var typeEnvironmentId = Environments.CurrentFor(environments, entry.Kind);
        return software.FirstOrDefault(row => row.DocumentKind == entry.Kind && row.TypeEnvironmentId == typeEnvironmentId);
    }
}
