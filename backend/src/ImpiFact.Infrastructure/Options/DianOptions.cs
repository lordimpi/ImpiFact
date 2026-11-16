using System.ComponentModel.DataAnnotations;

namespace ImpiFact.Infrastructure.Options;

/// <summary>
/// Direcciones de la DIAN (Dirección de Impuestos y Aduanas Nacionales) por ambiente: habilitación (pruebas) y
/// producción (Arquitectura detallada, secciones 6.4 y 11). El núcleo <c>ImpiFact.Dian</c> no lee estas
/// opciones (ADR-26): Application le pasa la URL que corresponda en su modelo de entrada.
/// </summary>
public sealed class DianOptions
{
    public const string SectionName = "Dian";

    /// <summary>Servicio web SOAP (protocolo de mensajes XML) de la DIAN en habilitación.</summary>
    [Required]
    [Url]
    public string HabilitacionUrl { get; set; } = "";

    /// <summary>Servicio web SOAP de la DIAN en producción.</summary>
    [Required]
    [Url]
    public string ProduccionUrl { get; set; } = "";

    /// <summary>
    /// Inicio de la URL de consulta que va en el código QR del documento en habilitación; se le agrega al final el
    /// CUFE (código único de la factura electrónica).
    /// </summary>
    [Required]
    [Url]
    public string HabilitacionQrUrl { get; set; } = "";

    /// <summary>Inicio de la URL de consulta del QR en producción; se le agrega al final el CUFE.</summary>
    [Required]
    [Url]
    public string ProduccionQrUrl { get; set; } = "";

    /// <summary>Tiempo máximo de espera de cada llamada a la DIAN, en segundos.</summary>
    [Range(1, 600)]
    public int TimeoutSeconds { get; set; }
}
