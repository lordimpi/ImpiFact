namespace ImpiFact.Application.Features.Documents.Delivery;

/// <summary>
/// Calendario para volver a consultar en la DIAN (la autoridad tributaria de Colombia) un documento enviado sin
/// decisión (Arquitectura detallada, sección 7): cada minuto durante los primeros 10 minutos desde el envío, luego cada
/// 15 minutos, hasta 24 horas después del envío; pasado ese plazo no hay más consultas.
/// </summary>
internal static class DianPollSchedule
{
    /// <summary>Primera consulta de un envío de set de pruebas (por su ZipKey, la clave del envío asíncrono).</summary>
    public static readonly TimeSpan FirstZipPoll = TimeSpan.FromSeconds(15);

    /// <summary>Plazo total desde el envío.</summary>
    public static readonly TimeSpan Deadline = TimeSpan.FromHours(24);

    private static readonly TimeSpan FastWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan FastInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan SlowInterval = TimeSpan.FromMinutes(15);

    /// <summary>Momento de la próxima consulta, nunca después del plazo; nulo si el plazo ya se cumplió.</summary>
    public static DateTimeOffset? Next(DateTimeOffset sentAt, DateTimeOffset now)
    {
        var deadline = sentAt + Deadline;
        if (now >= deadline)
        {
            return null;
        }

        var next = now + (now - sentAt < FastWindow ? FastInterval : SlowInterval);
        return next > deadline ? deadline : next;
    }
}
