using ImpiFact.Domain.Common;

namespace ImpiFact.Application.Features.Dashboard;

/// <summary>
/// Lecturas del tablero del panel (Arquitectura frontend, sección 16, punto 6): documentos por estado y mes, consumo del
/// plan, certificado de firma activo y resoluciones de numeración. Junta datos de varios módulos, así que el puerto
/// devuelve solo valores simples (textos y números) y no entidades de Documents, Billing ni Companies; lo implementa
/// Infrastructure, que sí puede leer esas tablas. Toda lectura recibe la empresa y solo devuelve sus datos.
/// </summary>
/// <remarks>
/// El puerto vive en el módulo y no en <c>Application/Abstractions</c> porque solo lo usa el tablero.
/// </remarks>
public interface IDashboardReads
{
    /// <summary>
    /// Cantidad de documentos emitidos entre <paramref name="from"/> (incluido) y <paramref name="until"/> (excluido),
    /// agrupados por mes del calendario de Colombia, tipo de documento y estado. Las combinaciones sin documentos no
    /// aparecen.
    /// </summary>
    Task<IReadOnlyList<DocumentCountRow>> CountDocumentsAsync(Guid companyId, DateTimeOffset from, DateTimeOffset until, CancellationToken ct);

    /// <summary>Plan vigente de la empresa con lo usado por familia; nulo si no tiene plan contratado.</summary>
    Task<DashboardPlan?> FindCurrentPlanAsync(Guid companyId, CancellationToken ct);

    /// <summary>Certificado de firma activo que vence más tarde; nulo si la empresa no tiene uno.</summary>
    Task<DashboardCertificate?> FindActiveCertificateAsync(Guid companyId, CancellationToken ct);

    /// <summary>Resoluciones de numeración activas, con el próximo número de su consecutivo.</summary>
    Task<IReadOnlyList<DashboardResolution>> ListActiveResolutionsAsync(Guid companyId, CancellationToken ct);
}

/// <summary>Documentos de un mes, un tipo y un estado.</summary>
/// <param name="Month">Mes del calendario de Colombia, <c>yyyy-MM</c>.</param>
/// <param name="Kind">
/// Familia del tipo de documento como la nombra el listado (<c>invoice</c>, <c>credit_note</c>, <c>debit_note</c>,
/// <c>support_document</c>, <c>support_document_adjustment_note</c>); nula para los demás tipos.
/// </param>
/// <param name="Status">Nombre del estado (<c>Accepted</c>, <c>Rejected</c>…).</param>
public sealed record DocumentCountRow(string Month, int DocumentTypeId, string? Kind, string Status, long Count);

/// <summary>Plan vigente de la empresa.</summary>
/// <param name="Period">Periodo: <c>unlimited</c> (sin límites), <c>monthly</c>, <c>yearly</c> o <c>package</c>.</param>
/// <param name="IsActive">Falso si la plataforma desactivó el plan: no deja emitir.</param>
/// <param name="PeriodEnd">Fin del periodo contratado; nulo si el plan no vence.</param>
/// <param name="Quotas">Familias que el plan incluye o que ya tienen consumo, en el orden de <see cref="DocumentKind"/>.</param>
public sealed record DashboardPlan(
    string Name, string Period, bool IsActive, DateTimeOffset StartsAt, DateTimeOffset? PeriodEnd, IReadOnlyList<DashboardQuota> Quotas);

/// <summary>Consumo de una familia de documentos en el plan contratado.</summary>
/// <param name="Limit">Documentos que incluye el plan; nulo si el plan no tiene límites.</param>
public sealed record DashboardQuota(DocumentKind Kind, long Used, long? Limit);

/// <summary>Certificado de firma activo.</summary>
/// <param name="Name">Nombre del archivo con que se subió.</param>
/// <param name="NotAfter">Fin de la vigencia.</param>
public sealed record DashboardCertificate(string Name, DateTimeOffset NotAfter);

/// <summary>Resolución de numeración activa.</summary>
/// <param name="Kind">Familia del tipo de documento, como en <see cref="DocumentCountRow.Kind"/>.</param>
/// <param name="NextNumber">Próximo número del consecutivo; nulo si todavía no se emitió ningún documento con ella.</param>
/// <param name="ValidTo">Fin de la vigencia; nulo si la resolución no tiene.</param>
public sealed record DashboardResolution(
    Guid Id, int DocumentTypeId, string? Kind, string? Prefix, string? ResolutionNumber, long RangeFrom, long RangeTo, long? NextNumber, DateOnly? ValidTo);
