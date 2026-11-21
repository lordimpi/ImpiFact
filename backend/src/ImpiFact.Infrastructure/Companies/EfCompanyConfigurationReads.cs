using ImpiFact.Application.Abstractions.Companies;
using ImpiFact.Application.Abstractions.Dian;
using ImpiFact.Domain.Companies;
using ImpiFact.Domain.Documents;
using ImpiFact.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Infrastructure.Companies;

/// <summary>
/// Lecturas de la configuración de la empresa para el panel, con EF Core (Entity Framework) sin rastreo. Quitan el
/// filtro global de empresa y la ponen en el WHERE, como los demás adaptadores que reciben <c>companyId</c>. Una
/// empresa tiene pocas resoluciones, así que el consecutivo de cada una se busca en una segunda consulta por su llave.
/// </summary>
internal sealed class EfCompanyConfigurationReads(ImpiFactDbContext db) : ICompanyConfigurationReads
{
    public async Task<IReadOnlyList<ResolutionRecord>> ListResolutionsAsync(Guid companyId, CancellationToken ct)
    {
        // La clave técnica no sale de la base: solo si existe.
        var resolutions = await db.Set<Resolution>().AsNoTracking()
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(resolution => resolution.CompanyId == companyId)
            .OrderBy(resolution => resolution.DocumentTypeId)
            .ThenByDescending(resolution => resolution.IsActive)
            .ThenBy(resolution => resolution.TypeEnvironmentId)
            .ThenBy(resolution => resolution.Prefix)
            .ThenBy(resolution => resolution.RangeFrom)
            .ThenBy(resolution => resolution.Id)
            .Select(resolution => new
            {
                resolution.Id,
                resolution.DocumentTypeId,
                resolution.TypeEnvironmentId,
                resolution.Prefix,
                resolution.ResolutionNumber,
                resolution.ResolutionDate,
                HasTechnicalKey = resolution.TechnicalKey != null && resolution.TechnicalKey != "",
                resolution.RangeFrom,
                resolution.RangeTo,
                resolution.ValidFrom,
                resolution.ValidTo,
                resolution.IsActive,
            })
            .ToListAsync(ct);

        var keys = resolutions.Select(resolution => NumberingScope.ForResolution(resolution.Id)).ToList();
        var sequences = await db.Set<NumberingSequence>().AsNoTracking()
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(sequence => sequence.CompanyId == companyId && keys.Contains(sequence.ScopeKey))
            .ToDictionaryAsync(sequence => sequence.ScopeKey, sequence => sequence.NextNumber, ct);

        return resolutions
            .Select(resolution => new ResolutionRecord(
                resolution.Id,
                resolution.DocumentTypeId,
                resolution.TypeEnvironmentId,
                resolution.Prefix,
                resolution.ResolutionNumber,
                resolution.ResolutionDate,
                resolution.HasTechnicalKey,
                resolution.RangeFrom,
                resolution.RangeTo,
                resolution.ValidFrom,
                resolution.ValidTo,
                resolution.IsActive,
                sequences.TryGetValue(NumberingScope.ForResolution(resolution.Id), out var next) ? next : null))
            .ToList();
    }

    public async Task<bool?> FindTestSetResultAsync(Guid companyId, CancellationToken ct)
    {
        // Solo las respuestas de GetStatusZip con veredicto: un envío sin respuesta todavía no dice nada, y tampoco las
        // que dicen que la DIAN sigue procesando («Batch en proceso de validación.», «se encuentra en proceso»), aunque
        // lleguen con IsValid falso. Un documento validado, o la descripción «Set de prueba con identificador … se
        // encuentra Aceptado» (la DIAN ya aprobó el set y no valida más documentos), dicen que el set está aceptado. Las
        // descripciones se comparan en memoria, igual en todos los motores.
        var verdicts = await db.Set<DianSubmission>().AsNoTracking()
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(submission => submission.CompanyId == companyId
                && submission.Operation == DianOperation.GetStatusZip
                && submission.IsValid != null)
            .Select(submission => new { IsValid = submission.IsValid!.Value, submission.DianStatusDescription })
            .Distinct()
            .ToListAsync(ct);
        verdicts.RemoveAll(verdict => !verdict.IsValid
            && !DianTestSetAnswers.IsAlreadyAccepted(verdict.DianStatusDescription)
            && DianTestSetAnswers.IsInProcess(verdict.DianStatusDescription));
        return verdicts.Count == 0
            ? null
            : verdicts.Any(verdict => verdict.IsValid || DianTestSetAnswers.IsAlreadyAccepted(verdict.DianStatusDescription));
    }
}
