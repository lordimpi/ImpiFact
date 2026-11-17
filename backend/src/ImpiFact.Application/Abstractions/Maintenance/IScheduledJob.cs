namespace ImpiFact.Application.Abstractions.Maintenance;

/// <summary>
/// Tarea programada diaria del Worker (Arquitectura detallada, sección 8): la limpieza de tablas
/// (<c>Housekeeping</c>) y los avisos de certificados y resoluciones por vencer. El programador de Infrastructure la
/// corre una vez al día, en su propio ámbito de servicios, como proceso del sistema y sin empresa fija.
/// </summary>
/// <remarks>
/// El programador garantiza que dos instancias del Worker no corran la misma tarea a la vez (un bloqueo en la base),
/// pero una tarea sí puede correr dos veces seguidas (por ejemplo, al reiniciar el Worker el mismo día): debe tolerar
/// repetirse sin duplicar efectos.
/// </remarks>
public interface IScheduledJob
{
    /// <summary>Nombre estable de la tarea, en inglés (por ejemplo <c>Housekeeping</c>); identifica su bloqueo.</summary>
    string Name { get; }

    Task RunAsync(CancellationToken ct);
}
