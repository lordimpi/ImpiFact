namespace ImpiFact.Application.Abstractions.Documents;

/// <summary>
/// Adelanta un consecutivo de numeración (<c>numbering_sequences</c>) hasta un número pedido, por ejemplo cuando la
/// empresa viene de otro sistema de facturación y debe seguir desde su último número. Usa el mismo UPDATE atómico que
/// la reserva de un número del cliente (<see cref="INumberingSequences.ReserveAsync"/>): el consecutivo queda en
/// <c>max(next_number, número pedido)</c>, así nunca baja y nunca repite un número ya tomado.
/// </summary>
/// <remarks>
/// Exige una transacción abierta (la del decorador de transacción). El UPDATE bloquea la fila hasta el final de la
/// transacción: una emisión simultánea espera y toma un número desde el valor nuevo, o termina antes y el UPDATE ve el
/// valor que ella dejó. Como los demás puertos con <c>companyId</c>, solo toca esa empresa. No revisa el rango de la
/// resolución: lo hace el caso de uso antes de llamarlo.
/// </remarks>
public interface INumberingAdvance
{
    /// <summary>Deja <c>next_number = max(next_number, nextNumber)</c> y devuelve el valor que quedó.</summary>
    /// <returns>El nuevo <c>next_number</c>, o nulo si la empresa no tiene ese consecutivo.</returns>
    /// <exception cref="InvalidOperationException">Si no hay una transacción abierta.</exception>
    Task<long?> AdvanceToAsync(Guid companyId, string scopeKey, long nextNumber, CancellationToken ct);
}
