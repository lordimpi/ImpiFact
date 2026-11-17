using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Domain.Common;

namespace ImpiFact.Application.Abstractions.Billing;

/// <summary>
/// El plan de la empresa visto desde la emisión y el envío a la DIAN (la autoridad tributaria de Colombia): el módulo
/// Documents pregunta si puede emitir y avisa cuando un documento quedó aceptado, sin conocer las entidades de Billing
/// (Arquitectura detallada, sección 1, regla de módulos). Lo implementa el módulo Billing.
/// </summary>
/// <remarks>
/// El plan vigente de una empresa es su plan contratado activo (<c>company_plans.is_active</c>); una empresa sin plan
/// contratado no tiene límites, como el plan por defecto «Default - No Plan - Unlimited» que el PHP asignaba a toda
/// empresa nueva.
/// </remarks>
public interface IPlanGate
{
    /// <summary>
    /// Revisa, antes de emitir, que el plan vigente esté activo, dentro de su periodo y con documentos disponibles de
    /// la familia <paramref name="kind"/>.
    /// </summary>
    /// <returns>Nulo si se puede emitir; si no, el error con el mensaje del PHP.</returns>
    Task<Error?> CheckIssueAsync(Guid companyId, DocumentKind kind, CancellationToken ct);

    /// <summary>
    /// Cuenta un documento aceptado por la DIAN en el plan vigente (<c>plan_usage</c>). La cuenta se escribe con el
    /// próximo guardado de la unidad de trabajo, en la misma transacción que la aceptación: si ese guardado falla (por
    /// ejemplo, porque otro proceso ya aceptó el mismo documento), la cuenta tampoco queda. Sin plan vigente no hace nada.
    /// </summary>
    Task CountAcceptedAsync(Guid companyId, DocumentKind kind, CancellationToken ct);
}
