using System.Security.Cryptography.X509Certificates;
using ImpiFact.Dian.Soap;

namespace ImpiFact.Application.Abstractions.Dian;

/// <summary>
/// Servicio web de la DIAN (la autoridad tributaria de Colombia; Arquitectura detallada, sección 6.4): arma el sobre
/// SOAP (protocolo de mensajes XML de servicios web) firmado de una operación, lo envía y devuelve lo que pasó, sin
/// decidir nada sobre el documento. Los reintentos ante errores transitorios y el *circuit breaker* (el corte que deja
/// de llamar a un servicio que viene fallando) los aplica la implementación.
/// </summary>
/// <remarks>
/// Una respuesta de error de la DIAN (un SOAP Fault, que llega con HTTP 500) es una respuesta, no un error de red:
/// llega como <see cref="DianCallOutcome.Answered"/> y nunca se reintenta.
/// </remarks>
public interface IDianWebService
{
    /// <summary>Envía <paramref name="request"/> al servicio en <paramref name="endpointUrl"/> firmado con el certificado.</summary>
    /// <param name="certificate">Certificado del emisor con su llave privada; quien lo pasa lo libera.</param>
    /// <exception cref="OperationCanceledException">Si <paramref name="ct"/> se cancela; nunca se informa como falta de respuesta.</exception>
    Task<DianCall> CallAsync(DianSoapRequest request, string endpointUrl, X509Certificate2 certificate, CancellationToken ct);
}

/// <summary>Una llamada al servicio web de la DIAN y su resultado.</summary>
/// <param name="Outcome">Si hubo respuesta, si no se alcanzó a enviar nada o si se envió y no hubo respuesta.</param>
/// <param name="Envelope">Sobre SOAP enviado (sin la llave privada: solo lleva el certificado público y la firma).</param>
/// <param name="HttpStatus">Código HTTP de la última respuesta; nulo si no hubo respuesta.</param>
/// <param name="Body">Cuerpo de la última respuesta tal como llegó; nulo si no hubo respuesta.</param>
/// <param name="Response">Respuesta interpretada por <see cref="DianResponseReader"/>; nula si no hubo respuesta.</param>
/// <param name="Duration">Tiempo total de la llamada, con sus reintentos.</param>
/// <param name="Failure">Motivo técnico cuando no hubo respuesta (para registros, en inglés); nulo si la hubo.</param>
public sealed record DianCall(
    DianCallOutcome Outcome,
    string Envelope,
    int? HttpStatus,
    string? Body,
    DianSoapResponse? Response,
    TimeSpan Duration,
    string? Failure);

/// <summary>Qué pasó con una llamada al servicio web de la DIAN.</summary>
public enum DianCallOutcome
{
    /// <summary>Llegó una respuesta HTTP (cualquier código), ya interpretada.</summary>
    Answered,

    /// <summary>
    /// No se alcanzó a enviar nada: no se resolvió el nombre, no se abrió la conexión o el *circuit breaker* está
    /// abierto. La DIAN no recibió el documento, así que es seguro reintentar.
    /// </summary>
    NotSent,

    /// <summary>
    /// Se envió (o pudo haberse enviado) y no llegó respuesta: se agotó el tiempo o se cortó la conexión. La DIAN
    /// pudo haber recibido el documento; hay que consultar su estado antes de darlo por perdido.
    /// </summary>
    NoAnswer,
}
