namespace ImpiFact.Domain.Billing;

/// <summary>Resultado de revisar si el plan de una empresa le deja emitir otro documento.</summary>
public enum PlanAllowance
{
    Allowed,

    /// <summary>El plan está desactivado (<c>plans.is_active</c> falso).</summary>
    PlanInactive,

    /// <summary>Plan mensual vencido o sin documentos disponibles.</summary>
    MonthlyLimitReached,

    /// <summary>Plan anual vencido o sin documentos disponibles.</summary>
    YearlyLimitReached,

    /// <summary>Paquete sin documentos disponibles (o vencido, si se le fijó un fin).</summary>
    PackageLimitReached,
}

/// <summary>
/// Reglas del plan al emitir, las mismas de ApiDian (la implementación de referencia en PHP;
/// <c>InvoiceController::store</c> y las notas): un plan inactivo no deja emitir; el plan por defecto (periodo 0) no
/// tiene límites; el mensual vence al mes de empezar y el anual al año; los tres periodos con límite se agotan cuando
/// los documentos usados llegan a la cantidad del plan. Una familia sin cantidad en el plan tiene cantidad 0, como las
/// columnas <c>qty_docs_*</c> del PHP, que valen 0 por defecto.
/// </summary>
/// <remarks>
/// El PHP no tenía fin explícito del plan; aquí <c>company_plans.ends_at</c>, si existe, reemplaza el fin calculado y
/// también vence un paquete. Las fechas se calculan en la hora de Colombia (UTC-5 todo el año), la del PHP.
/// </remarks>
public static class PlanRules
{
    private static readonly TimeSpan ColombiaOffset = TimeSpan.FromHours(-5);

    /// <summary>Fin del periodo contratado: el fin explícito o, si no hay, un mes o un año después del inicio.</summary>
    /// <returns>Nulo cuando el plan no vence (plan por defecto o paquete sin fin explícito).</returns>
    public static DateTimeOffset? PeriodEnd(PlanPeriod period, DateTimeOffset startsAt, DateTimeOffset? endsAt)
    {
        if (period == PlanPeriod.Default)
        {
            return null;
        }

        return endsAt ?? period switch
        {
            PlanPeriod.Monthly => startsAt.ToOffset(ColombiaOffset).AddMonths(1),
            PlanPeriod.Yearly => startsAt.ToOffset(ColombiaOffset).AddYears(1),
            _ => null,
        };
    }

    /// <param name="quantity">Documentos de la familia que incluye el plan (0 si el plan no la incluye).</param>
    /// <param name="used">Documentos de la familia ya aceptados en este plan contratado.</param>
    public static PlanAllowance Check(
        bool planIsActive, PlanPeriod period, DateTimeOffset startsAt, DateTimeOffset? endsAt, long quantity, long used, DateTimeOffset now)
    {
        if (!planIsActive)
        {
            return PlanAllowance.PlanInactive;
        }

        if (period == PlanPeriod.Default)
        {
            return PlanAllowance.Allowed;
        }

        var expired = PeriodEnd(period, startsAt, endsAt) is { } end && now >= end;
        if (!expired && used < quantity)
        {
            return PlanAllowance.Allowed;
        }

        return period switch
        {
            PlanPeriod.Monthly => PlanAllowance.MonthlyLimitReached,
            PlanPeriod.Yearly => PlanAllowance.YearlyLimitReached,
            _ => PlanAllowance.PackageLimitReached,
        };
    }
}
