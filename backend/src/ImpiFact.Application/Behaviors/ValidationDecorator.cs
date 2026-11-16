using FluentValidation;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Results;

namespace ImpiFact.Application.Behaviors;

/// <summary>
/// Segundo decorador: corre todos los validadores de la solicitud y, si alguno encuentra datos inválidos,
/// devuelve un error de validación con la lista de campos sin llegar al caso de uso.
/// </summary>
internal sealed class ValidationDecorator<TIn, TOut>(IHandler<TIn, TOut> inner, IEnumerable<IValidator<TIn>> validators)
    : IHandler<TIn, TOut>
{
    public async Task<Result<TOut>> HandleAsync(TIn request, CancellationToken ct)
    {
        var fields = new List<FieldError>();
        foreach (var validator in validators)
        {
            var validation = await validator.ValidateAsync(request, ct);
            fields.AddRange(validation.Errors.Select(failure => new FieldError(failure.PropertyName, failure.ErrorMessage)));
        }

        return fields.Count > 0
            ? Result<TOut>.Fail(Error.Validation(fields))
            : await inner.HandleAsync(request, ct);
    }
}
