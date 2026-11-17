namespace ImpiFact.Application.Abstractions.Maintenance;

/// <summary>
/// Avisos de las tareas programadas (<c>notifications</c>) que se dejan una sola vez por llave, aunque la tarea corra
/// todos los días, se reinicie el Worker o lo corran dos instancias a la vez.
/// </summary>
/// <remarks>
/// La llave del aviso se convierte en el id de la notificación (siempre el mismo id para la misma llave), así la llave
/// primaria de <c>notifications</c> impide el duplicado sin columnas nuevas. Cada aviso se guarda en su propia
/// transacción, junto con el mensaje de la bandeja de salida que envía su correo cuando se pide.
/// </remarks>
public interface IScheduledNotices
{
    /// <summary>Guarda el aviso si su llave no se usó antes.</summary>
    /// <returns>Verdadero si lo guardó; falso si ya existía un aviso con la misma llave.</returns>
    Task<bool> TryAddAsync(ScheduledNotice notice, CancellationToken ct);
}

/// <summary>Un aviso para todos los usuarios de una empresa.</summary>
/// <param name="Key">
/// Llave estable del aviso, en inglés (por ejemplo <c>certificate-expiry:{id}:30</c>): dos avisos con la misma llave
/// son el mismo aviso.
/// </param>
/// <param name="Type">Tipo del aviso, en inglés y estable (hasta 50 caracteres).</param>
/// <param name="Title">Título en español que ve el usuario.</param>
/// <param name="Body">Texto en español que ve el usuario; nunca lleva secretos.</param>
/// <param name="SendEmail">Si además se envía por correo al correo de la empresa (mensaje <c>SendNoticeEmail</c>).</param>
public sealed record ScheduledNotice(Guid CompanyId, string Key, string Type, string Title, string Body, bool SendEmail);

/// <summary>Datos del mensaje <c>SendNoticeEmail</c>: la notificación cuyo título y texto se envían por correo.</summary>
public sealed record SendNoticeEmailPayload(Guid NotificationId);

/// <summary>
/// Lo que necesita el correo de un aviso: la notificación y el nombre y el correo de la empresa. Lo implementa
/// Infrastructure con una consulta sin rastreo de la empresa del mensaje.
/// </summary>
public interface ICompanyNoticeReads
{
    /// <summary>El aviso de la empresa; nulo si la notificación o la empresa ya no existen.</summary>
    Task<CompanyNotice?> FindAsync(Guid companyId, Guid notificationId, CancellationToken ct);
}

/// <summary>Un aviso con los datos de su empresa.</summary>
/// <param name="CompanyEmail">Correo de la empresa; vacío si no tiene.</param>
public sealed record CompanyNotice(string CompanyName, string CompanyEmail, string Title, string Body);
