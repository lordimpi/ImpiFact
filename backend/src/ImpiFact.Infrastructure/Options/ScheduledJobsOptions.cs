using System.ComponentModel.DataAnnotations;

namespace ImpiFact.Infrastructure.Options;

/// <summary>
/// Tareas programadas diarias del Worker (sección «ScheduledJobs»; Arquitectura detallada, secciones 8 y 11): la
/// limpieza de tablas (<c>Housekeeping</c>) y los vigilantes de certificados y resoluciones. Los valores por defecto
/// son los del documento, así el Worker arranca aunque la sección no exista.
/// </summary>
public sealed class ScheduledJobsOptions : IValidatableObject
{
    public const string SectionName = "ScheduledJobs";

    /// <summary>
    /// Hora del día, en UTC (tiempo universal coordinado), en que corren las tareas, en horas y minutos. Por defecto
    /// las 08:00 UTC, las 03:00 en Colombia.
    /// </summary>
    public TimeOnly RunAtUtc { get; set; } = new(8, 0);

    /// <summary>
    /// Si las tareas corren también al arrancar el Worker. Así un Worker que estuvo apagado a la hora programada no se
    /// salta un día; las tareas toleran repetirse, porque cada aviso se guarda una sola vez.
    /// </summary>
    public bool RunOnStartup { get; set; } = true;

    /// <summary>Días que se guardan los mensajes hechos de la bandeja de salida antes de borrarlos.</summary>
    [Range(1, 3650)]
    public int OutboxRetentionDays { get; set; } = 30;

    /// <summary>
    /// Filas que borra cada sentencia de la limpieza. Se borra por tandas para que un atraso grande no bloquee las
    /// tablas mucho tiempo: cada tanda se confirma sola.
    /// </summary>
    [Range(1, 100000)]
    public int DeleteBatchSize { get; set; } = 1000;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (RunAtUtc.Second != 0 || RunAtUtc.Millisecond != 0 || RunAtUtc.Microsecond != 0)
        {
            yield return new ValidationResult($"{nameof(RunAtUtc)} must be a time in hours and minutes, such as 08:00.", [nameof(RunAtUtc)]);
        }
    }
}
