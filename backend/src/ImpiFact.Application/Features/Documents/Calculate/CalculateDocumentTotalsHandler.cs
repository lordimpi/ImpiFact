using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Results;

namespace ImpiFact.Application.Features.Documents.Calculate;

/// <summary>
/// Caso de uso de <c>POST /api/v1/documents/calculate</c>: calcula los valores del borrador con
/// <see cref="DocumentTotalsCalculator"/>. Llega ya validado (<see cref="CalculateDocumentTotalsValidator"/>) y con una
/// empresa en el contexto (lo exige el decorador de tenant); no lee ni escribe datos, así que no usa la empresa ni abre
/// transacción.
/// </summary>
internal sealed class CalculateDocumentTotalsHandler : IHandler<CalculateDocumentTotalsRequest, DocumentTotalsCalculation>
{
    public Task<Result<DocumentTotalsCalculation>> HandleAsync(CalculateDocumentTotalsRequest request, CancellationToken ct) =>
        Task.FromResult(DocumentTotalsCalculator.Calculate(request));
}
