using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Companies;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Domain.Companies;

namespace ImpiFact.Application.Features.Companies.UpsertResolution;

/// <summary>
/// Crea o actualiza la resolución en el ambiente actual de la familia de su tipo de documento (como
/// <c>updateOrCreate</c> del PHP, por tipo, número, prefijo y ambiente) y la deja activa. Con
/// <c>delete_all_type_resolutions</c> el PHP borraba las demás del mismo tipo y ambiente; aquí quedan inactivas,
/// porque los documentos ya emitidos pueden apuntar a ellas. Además deja listo el consecutivo de la resolución
/// (<c>numbering_sequences</c>): empieza en el <c>from</c> del rango y, si la resolución ya existía y su rango
/// empieza ahora más arriba, avanza hasta ahí; nunca baja.
/// </summary>
/// <remarks>
/// Brecha del modelo: <c>resolution_number</c> y <c>resolution_date</c> son obligatorios en la tabla, pero el PHP
/// los deja vacíos en las resoluciones que no son de factura (por ejemplo las de notas crédito). Mientras el modelo
/// no los haga opcionales se guardan un número vacío y la fecha de hoy en Colombia.
/// </remarks>
internal sealed class UpsertResolutionHandler(ICompanyRepository companies, INumberingSequences numbering, ITenantContext tenant)
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
        var number = string.IsNullOrEmpty(request.Resolution) ? null : request.Resolution;
        var prefix = string.IsNullOrEmpty(request.Prefix) ? null : request.Prefix;

        var existing = await companies.GetResolutionsAsync(companyId, documentTypeId, typeEnvironmentId, ct);
        var resolution = existing.FirstOrDefault(row => row.ResolutionNumber == number && row.Prefix == prefix);
        if (resolution is null)
        {
            // El id se fija aquí, antes de guardar, porque la clave del consecutivo lo incluye.
            resolution = new Resolution
            {
                Id = Guid.CreateVersion7(),
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

        resolution.ResolutionDate = UpsertResolutionValidator.ParseDate(request.ResolutionDate);
        resolution.TechnicalKey = request.TechnicalKey;
        resolution.RangeFrom = request.From!.Value;
        resolution.RangeTo = request.To!.Value;
        resolution.ValidFrom = UpsertResolutionValidator.ParseDate(request.DateFrom);
        resolution.ValidTo = UpsertResolutionValidator.ParseDate(request.DateTo);
        resolution.IsActive = true;
        await numbering.OpenForResolutionAsync(companyId, resolution.Id, resolution.RangeFrom, ct);

        return new ResolutionSavedResponse { Resolution = CompanyMapper.Map<ResolutionResponse>(resolution) };
    }
}
