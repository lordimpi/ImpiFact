namespace ImpiFact.Application.Abstractions.Documents;

/// <summary>
/// Consecutivos de numeración (<c>numbering_sequences</c>; Modelo de datos detallado, sección 5). Dos solicitudes
/// simultáneas nunca obtienen el mismo número: el número se toma con un solo UPDATE atómico dentro de la
/// transacción de la solicitud, y la fila queda bloqueada hasta que esa transacción confirma o se revierte.
/// </summary>
/// <remarks>
/// <para>
/// Tomar o reservar un número exige una transacción abierta (la del decorador de transacción). Si el caso de uso
/// falla después, la transacción se revierte y el número vuelve a quedar libre; si el documento se guarda y luego
/// termina <c>Failed</c>, el número queda consumido como hueco (no se reutiliza).
/// </para>
/// <para>
/// Este puerto no revisa el rango ni la vigencia de la resolución: el caso de uso lo hace con
/// <c>NumberingRange.Check</c> antes de guardar, también para el número que entrega <see cref="TakeNextAsync"/>
/// (que sigue subiendo aunque pase del final del rango).
/// </para>
/// <para>
/// Como los demás puertos con <c>companyId</c>, busca solo en esa empresa; si la solicitud es de una empresa,
/// debe ser la misma (si no, lanza <see cref="InvalidOperationException"/>, igual que la guardia de escritura).
/// </para>
/// </remarks>
public interface INumberingSequences
{
    /// <summary>
    /// Deja listo el consecutivo de una resolución: si no existe lo crea empezando en <paramref name="firstNumber"/>
    /// (el <c>from</c> del rango); si existe y va por debajo de ese número, lo adelanta. Nunca lo hace bajar. La fila
    /// se guarda cuando el decorador de transacción guarda los cambios.
    /// </summary>
    Task OpenForResolutionAsync(Guid companyId, Guid resolutionId, long firstNumber, CancellationToken ct);

    /// <summary>
    /// Lo mismo que <see cref="OpenForResolutionAsync"/> para cualquier consecutivo <paramref name="scopeKey"/> (ver
    /// <c>NumberingScope</c>): lo crea en <paramref name="firstNumber"/> si no existe, o lo adelanta hasta ese número.
    /// </summary>
    Task OpenAsync(Guid companyId, string scopeKey, long firstNumber, CancellationToken ct);

    /// <summary>
    /// Toma el siguiente número del consecutivo <paramref name="scopeKey"/> y lo deja avanzado en uno.
    /// </summary>
    /// <returns>El número tomado, o nulo si la empresa no tiene ese consecutivo.</returns>
    /// <exception cref="InvalidOperationException">Si no hay una transacción abierta.</exception>
    Task<long?> TakeNextAsync(Guid companyId, string scopeKey, CancellationToken ct);

    /// <summary>
    /// Reserva un número enviado por el cliente (supuesto B2 de la fase 3): el consecutivo pasa a
    /// <c>max(next_number, número + 1)</c>, así los números automáticos siguientes nunca lo repiten y el consecutivo
    /// nunca baja; después se revisa que ningún documento guardado use ya ese tipo, ambiente, prefijo y número.
    /// </summary>
    /// <remarks>
    /// El UPDATE bloquea la fila del consecutivo hasta el final de la transacción, por eso dos reservas simultáneas del
    /// mismo número se atienden una tras otra: la segunda ve el documento que guardó la primera y recibe
    /// <see cref="NumberReservation.AlreadyUsed"/>. El índice único <c>ux_documents_number</c> sigue siendo la última
    /// barrera (por ejemplo, contra una resolución distinta con el mismo prefijo).
    /// </remarks>
    /// <exception cref="InvalidOperationException">Si no hay una transacción abierta.</exception>
    Task<NumberReservation> ReserveAsync(Guid companyId, string scopeKey, DocumentNumberKey number, CancellationToken ct);
}

/// <summary>
/// Número completo de un documento, como lo identifica el índice único <c>ux_documents_number</c>: tipo, ambiente,
/// prefijo (vacío si no tiene) y número.
/// </summary>
public sealed record DocumentNumberKey(int DocumentTypeId, int TypeEnvironmentId, string Prefix, long Number);

/// <summary>Resultado de <see cref="INumberingSequences.ReserveAsync"/>.</summary>
public enum NumberReservation
{
    /// <summary>El número quedó reservado para esta transacción.</summary>
    Reserved,

    /// <summary>Un documento guardado ya usa ese número.</summary>
    AlreadyUsed,

    /// <summary>La empresa no tiene ese consecutivo.</summary>
    SequenceNotFound,
}
