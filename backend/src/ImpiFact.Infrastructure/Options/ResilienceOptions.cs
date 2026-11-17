using System.ComponentModel.DataAnnotations;

namespace ImpiFact.Infrastructure.Options;

/// <summary>
/// Resiliencia de las llamadas al servicio web de la DIAN (sección «Resilience»; Arquitectura detallada, secciones 6.4
/// y 11). El tiempo de espera de cada intento es <see cref="DianOptions.TimeoutSeconds"/> (60 s). Los valores por
/// defecto son los del documento, así la aplicación arranca aunque la sección no exista.
/// </summary>
/// <remarks>
/// Solo se reintentan los errores transitorios: tiempo agotado, HTTP 5xx, 408 o 429 y fallas de red. Un SOAP Fault
/// (la respuesta de error de la DIAN, que llega con HTTP 500) es una respuesta de negocio: no se reintenta ni cuenta
/// para el <i>circuit breaker</i> (el corte que deja de llamar a la DIAN un rato cuando viene fallando).
/// </remarks>
public sealed class ResilienceOptions
{
    public const string SectionName = "Resilience";

    /// <summary>Reintentos después del primer intento, con retroceso exponencial y jitter (una espera al azar).</summary>
    [Range(0, 10)]
    public int Retries { get; set; } = 3;

    /// <summary>Espera base del retroceso, en segundos: cerca de 2, 4 y 8 segundos con el valor por defecto.</summary>
    [Range(0.0, 60.0)]
    public double BaseDelaySeconds { get; set; } = 2;

    /// <summary>
    /// Fallos dentro de <see cref="BreakerSeconds"/> que abren el <i>circuit breaker</i>; se abre cuando todas las
    /// llamadas de la ventana fallaron y fueron al menos esta cantidad.
    /// </summary>
    [Range(2, 100)]
    public int BreakerFailures { get; set; } = 5;

    /// <summary>Ventana, en segundos, en que se cuentan los fallos del <i>circuit breaker</i>.</summary>
    [Range(1, 600)]
    public int BreakerSeconds { get; set; } = 30;

    /// <summary>Segundos que el <i>circuit breaker</i> queda abierto: en ese lapso las llamadas fallan sin enviarse.</summary>
    [Range(1, 3600)]
    public int BreakDurationSeconds { get; set; } = 30;

    /// <summary>
    /// Plazo total de una llamada con todos sus intentos y esperas, en segundos. Con 120 s caben un envío y la consulta
    /// GetStatus de la regla 90 dentro del plazo de un handler del Worker (<c>Outbox:HandlerTimeoutSeconds</c>, 270 s),
    /// que el Worker exige al arrancar; por eso, con 60 s por intento, en la práctica caben dos intentos de una llamada
    /// que se agota, no los cuatro.
    /// </summary>
    [Range(1, 3600)]
    public int TotalSeconds { get; set; } = 120;
}
