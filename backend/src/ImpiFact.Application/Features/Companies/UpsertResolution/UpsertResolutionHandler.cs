using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Companies;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Domain.Companies;

namespace ImpiFact.Application.Features.Companies.UpsertResolution;

/// <summary>
/// Crea o actualiza la resolución en el ambiente actual de la familia de su tipo de documento (como
/// <c>updateOrCreate</c> del PHP, por tipo, número, prefijo y ambiente) y la deja activa. Con
/// <c>delete_all_type_resolutions</c> el PHP borraba las demás del mismo tipo y ambiente; aquí quedan inactivas,
/// porque los documentos ya emitidos pueden apuntar a ellas.
/// </summary>
/// <remarks>
/// Brecha del modelo: <c>resolution_number</c> y <c>resolution_date</c> son obligatorios en la tabla, pero el PHP
/// los deja vacíos en las resoluciones que no son de factura (por ejemplo las de notas crédito). Mientras el modelo
/// no los haga opcionales se guardan un número vacío y la fecha de hoy en Colombia.
/// </remarks>
internal sealed class UpsertResolutionHandler(ICompanyRepository companies, ITenantContext tenant, IClock clock)
    : IHandler<UpsertResolutionCommand, ResolutionSavedResponse>
{
    public async Task<Result<ResolutionSavedResponse>> HandleAsync(UpsertResolutionCommand request, CancellationToken ct)
    {
        var own = await CompanyAccess.OwnCompanyAsync(companies, tenant, request.Nit, ct);
        if (!own.IsSuccess)
        {
            return own.Error;
        }

        var companyId = own.Value.Id;
        var documentTypeId = request.TypeDocumentId!.Value;
        var environments = await companies.GetEnvironmentsAsync(companyId, ct);
        var typeEnvironmentId = Environments.CurrentFor(environments, Environments.KindOf(documentTypeId));
        var number = request.Resolution ?? "";
        var prefix = string.IsNullOrEmpty(request.Prefix) ? null : request.Prefix;

        var existing = await companies.GetResolutionsAsync(companyId, documentTypeId, typeEnvironmentId, ct);
        var resolution = existing.FirstOrDefault(row => row.ResolutionNumber == number && row.Prefix == prefix);
        if (resolution is null)
        {
            resolution = new Resolution
            {
                CompanyId = companyId,
                DocumentTypeId = documentTypeId,
                TypeEnvironmentId = typeEnvironmentId,
                Prefix = prefix,
                ResolutionNumber = number,
            };
            companies.Add(resolution);
        }

        if (request.DeleteAllTypeResolutions == true)
        {
            foreach (var other in existing.Where(row => !ReferenceEquals(row, resolution)))
            {
                other.IsActive = false;
            }
        }

        resolution.ResolutionDate = UpsertResolutionValidator.ParseDate(request.ResolutionDate) ?? CertificateDays.ColombiaDate(clock.UtcNow);
        resolution.TechnicalKey = request.TechnicalKey;
        resolution.RangeFrom = request.From!.Value;
        resolution.RangeTo = request.To!.Value;
        resolution.ValidFrom = UpsertResolutionValidator.ParseDate(request.DateFrom);
        resolution.ValidTo = UpsertResolutionValidator.ParseDate(request.DateTo);
        resolution.IsActive = true;

        return new ResolutionSavedResponse { Resolution = CompanyMapper.Map<ResolutionResponse>(resolution) };
    }
}
