using System.Data.Common;
using ImpiFact.Domain.Common;

namespace ImpiFact.Infrastructure.Billing;

/// <summary>
/// SQL del consumo del plan propio de cada motor (Modelo de datos detallado, sección 3.6): suma uno a
/// <c>plan_usage.used</c> del plan contratado y la familia, o crea la fila en 1 si no existe, en una sola sentencia
/// atómica. Cada proyecto <c>ImpiFact.Infrastructure.&lt;Motor&gt;</c> trae su implementación y la registra con su
/// <c>IDatabaseProvider</c>.
/// </summary>
/// <remarks>
/// Es la misma excepción documentada que el consecutivo (<c>INumberingSql</c>) a la regla «Escrituras (16-nov)»
/// (Arquitectura detallada, sección 16). La sentencia sube <c>row_version</c> en uno, así una copia de la fila cargada
/// antes en EF Core queda vieja y su guardado falla por concurrencia en lugar de pisar la cuenta. Quien la llama revisa
/// antes que la solicitud pueda escribir en la empresa del plan contratado.
/// </remarks>
public interface IPlanUsageSql
{
    Task IncrementAsync(
        DbConnection connection, DbTransaction transaction, Guid companyPlanId, DocumentKind kind, CancellationToken ct);
}
