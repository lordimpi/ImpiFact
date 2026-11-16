using System.ComponentModel.DataAnnotations;

namespace ImpiFact.Infrastructure.Options;

/// <summary>
/// Procesador de la bandeja de salida del Worker (sección «Outbox»; outbox: la tabla <c>outbox_messages</c> con el
/// trabajo pendiente; Arquitectura detallada, secciones 8 y 11). Los valores por defecto son los del documento, así
/// el Worker arranca aunque la sección no exista.
/// </summary>
public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>Segundos de espera entre consultas cuando la última tanda no vino llena.</summary>
    [Range(1, 60)]
    public int PollSeconds { get; set; } = 2;

    /// <summary>
    /// Minutos que un mensaje queda reservado para la instancia que lo tomó. Si la instancia se cae, otra lo vuelve
    /// a tomar cuando vence este plazo, por eso debe ser mayor que lo que tarda el handler más lento.
    /// </summary>
    [Range(1, 120)]
    public int LockMinutes { get; set; } = 5;

    /// <summary>Intentos antes de marcar el mensaje como fallido y avisar a la empresa.</summary>
    [Range(1, 20)]
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Mensajes que se toman en cada consulta; se procesan a la vez, cada uno en su propio ámbito.</summary>
    [Range(1, 200)]
    public int BatchSize { get; set; } = 20;
}
