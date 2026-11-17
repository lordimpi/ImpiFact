namespace ImpiFact.Application.Abstractions.Maintenance;

/// <summary>
/// Cuándo una resolución de numeración de la DIAN (Dirección de Impuestos y Aduanas Nacionales) está por agotarse: le
/// queda menos del 10 % de su rango o le faltan menos de 30 días para <c>valid_to</c> (fin de su vigencia), contados en
/// el calendario de Colombia. La usan la tarea diaria <c>ResolutionRangeWatcher</c>, que deja el aviso, y el tablero
/// del panel, que muestra las mismas resoluciones; vive aquí porque un módulo no puede usar las piezas internas de
/// otro (regla 4 de Arquitectura detallada, sección 1).
/// </summary>
public static class ResolutionAlertRule
{
    /// <summary>Alerta si los números que quedan son menos que este porcentaje del rango.</summary>
    public const int RemainingPercent = 10;

    /// <summary>Alerta si faltan menos de estos días para el fin de la vigencia.</summary>
    public const int ValidityDays = 30;

    /// <summary>Evalúa una resolución en la fecha <paramref name="today"/> del calendario de Colombia.</summary>
    /// <param name="rangeFrom">Primer número autorizado.</param>
    /// <param name="rangeTo">Último número autorizado.</param>
    /// <param name="nextNumber">Próximo número del consecutivo; nulo si todavía no se emitió ningún documento.</param>
    /// <param name="validTo">Fin de la vigencia; nulo si la resolución no tiene (las de notas).</param>
    public static ResolutionAlert Evaluate(long rangeFrom, long rangeTo, long? nextNumber, DateOnly? validTo, DateOnly today)
    {
        var total = rangeTo - rangeFrom + 1;
        var next = Math.Max(nextNumber ?? rangeFrom, rangeFrom);
        var remaining = Math.Max(0, rangeTo - next + 1);
        int? daysLeft = validTo is { } end ? end.DayNumber - today.DayNumber : null;
        return new ResolutionAlert(
            total,
            remaining,
            NumbersLow: total > 0 && remaining * 100 < total * RemainingPercent,
            daysLeft,
            Expiring: daysLeft < ValidityDays,
            Expired: validTo is { } date && date < today);
    }
}

/// <summary>Resultado de <see cref="ResolutionAlertRule.Evaluate"/>.</summary>
/// <param name="Total">Números autorizados en el rango.</param>
/// <param name="Remaining">Números que quedan por usar (0 si se agotaron).</param>
/// <param name="NumbersLow">Quedan menos del <see cref="ResolutionAlertRule.RemainingPercent"/> % del rango.</param>
/// <param name="DaysLeft">Días del calendario de Colombia hasta el fin de la vigencia (negativos si ya pasó); nulo sin vigencia.</param>
/// <param name="Expiring">Faltan menos de <see cref="ResolutionAlertRule.ValidityDays"/> días para el fin de la vigencia.</param>
/// <param name="Expired">La vigencia ya terminó.</param>
public readonly record struct ResolutionAlert(long Total, long Remaining, bool NumbersLow, int? DaysLeft, bool Expiring, bool Expired)
{
    /// <summary>Verdadero si se cumple alguna de las dos condiciones.</summary>
    public bool Any => NumbersLow || Expiring;
}
