namespace ImpiFact.Application.Abstractions.Messaging;

/// <summary>
/// Bandeja de salida (outbox; Arquitectura detallada, sección 4.5): trabajo entre procesos que el Worker hará
/// después, por ejemplo enviar el documento a la DIAN. El mensaje se escribe en <c>outbox_messages</c> en la misma
/// transacción que el cambio de estado: si la transacción se revierte, el mensaje tampoco queda.
/// </summary>
public interface IOutbox
{
    /// <summary>
    /// Agrega un mensaje pendiente; se guarda cuando el decorador de transacción guarda los cambios.
    /// </summary>
    /// <param name="type">Nombre del tipo de mensaje (ver <see cref="OutboxMessageTypes"/>).</param>
    /// <param name="payload">Datos del mensaje; se guardan como JSON con nombres en snake_case.</param>
    /// <param name="companyId">
    /// Empresa del mensaje; el Worker la fija como tenant antes de procesarlo. Nula solo para mensajes de la
    /// plataforma. Si la solicitud es de una empresa, debe ser la misma.
    /// </param>
    /// <param name="notBefore">Momento desde el cual se puede procesar; nulo para procesarlo apenas se pueda.</param>
    /// <returns>Id del mensaje.</returns>
    /// <exception cref="InvalidOperationException">Si la solicitud es de otra empresa.</exception>
    Task<Guid> EnqueueAsync<TPayload>(string type, TPayload payload, Guid? companyId, DateTimeOffset? notBefore, CancellationToken ct);
}

/// <summary>Tipos de mensaje de la bandeja de salida (Arquitectura detallada, secciones 4.5 y 7).</summary>
public static class OutboxMessageTypes
{
    /// <summary>Generar y firmar el XML de un documento ya guardado.</summary>
    public const string BuildDocumentXml = "BuildDocumentXml";

    /// <summary>Enviar el documento firmado a la DIAN.</summary>
    public const string SendDocumentToDian = "SendDocumentToDian";

    /// <summary>Volver a consultar en la DIAN el estado de un documento sin confirmar.</summary>
    public const string PollDianStatus = "PollDianStatus";

    public const string SendEmail = "SendEmail";

    public const string RenderPdf = "RenderPdf";

    /// <summary>Enviar por correo a la empresa un aviso de las tareas programadas (por ejemplo, el certificado por vencer).</summary>
    public const string SendNoticeEmail = "SendNoticeEmail";
}
