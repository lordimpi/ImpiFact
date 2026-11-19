using System.Text.Json.Serialization;
using FluentValidation;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Companies;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Features.Companies.SetResolutionNextNumber;

/// <summary>
/// Fija el próximo número de una resolución de la empresa (<c>PUT /api/v1/companies/{nit}/resolutions/{id}/next-number</c>):
/// el número que tomará el próximo documento emitido con ella. Sirve cuando la empresa viene de otro sistema de
/// facturación y debe seguir desde su último número, o cuando ya usó números del rango ante la DIAN (por ejemplo, un
/// set de pruebas anterior con la misma resolución de habilitación).
/// </summary>
public sealed record SetResolutionNextNumberCommand : ICommand
{
    [JsonIgnore]
    public string? Nit { get; init; }

    [JsonIgnore]
    public Guid ResolutionId { get; init; }

    /// <summary>Número que tomará el próximo documento; dentro del rango y nunca menor que el próximo número actual.</summary>
    [JsonPropertyName("next_number")]
    public long? NextNumber { get; init; }
}

/// <summary>Respuesta con el próximo número que quedó.</summary>
public sealed record ResolutionNextNumberResponse
{
    public const string UpdatedMessage = "Próximo número actualizado.";

    [JsonPropertyName("success")]
    public bool Success { get; init; } = true;

    [JsonPropertyName("message")]
    public string Message { get; init; } = UpdatedMessage;

    [JsonPropertyName("resolution_id")]
    public Guid ResolutionId { get; init; }

    /// <summary>Número que tomará el próximo documento de la resolución.</summary>
    [JsonPropertyName("next_number")]
    public long NextNumber { get; init; }
}

/// <summary>El número es obligatorio y no negativo; el rango y el número actual los revisa el caso de uso.</summary>
public sealed class SetResolutionNextNumberValidator : AbstractValidator<SetResolutionNextNumberCommand>
{
    public SetResolutionNextNumberValidator()
    {
        RuleFor(command => command.NextNumber)
            .NotNull().WithMessage(PhpMessages.Required("next_number"))
            .GreaterThanOrEqualTo(0).WithMessage(PhpMessages.MinNumber("next_number", 0))
            .OverridePropertyName("next_number");
    }
}

/// <summary>Mensajes de los datos inválidos del próximo número.</summary>
public static class NextNumberMessages
{
    public static string OutOfRange(long from, long to) =>
        $"El próximo número debe estar dentro del rango de la resolución, entre {from} y {to}.";

    public static string Backwards(long current) =>
        $"El próximo número no puede ser menor que {current}, el próximo número actual: bajarlo podría repetir un número " +
        "ya enviado a la DIAN.";
}

/// <summary>
/// Revisa que la resolución sea de la empresa y que el número esté en su rango, y adelanta el consecutivo con el UPDATE
/// atómico del motor (<see cref="INumberingAdvance"/>), que deja <c>max(actual, pedido)</c>. Si el valor que quedó es
/// mayor que el pedido, el número pedido estaba por debajo del actual: responde 422 y el decorador de transacción
/// revierte la sentencia.
/// </summary>
/// <remarks>
/// La revisión de «no bajar» se hace con el valor que devuelve el UPDATE y no con una lectura previa: el UPDATE bloquea
/// la fila, así una emisión simultánea o termina antes (y el UPDATE ve el número que ella tomó) o espera a que esta
/// transacción termine (y toma el número nuevo). En ningún caso el consecutivo baja ni se entrega un número repetido.
/// </remarks>
internal sealed class SetResolutionNextNumberHandler(
    ICompanyRepository companies, INumberingAdvance advance, INumberingSequences numbering, ITenantContext tenant)
    : IHandler<SetResolutionNextNumberCommand, ResolutionNextNumberResponse>
{
    public static readonly Error NotFound = new(
        "resolution.not_found",
        "La resolución no existe en la empresa.",
        ErrorType.NotFound);

    public async Task<Result<ResolutionNextNumberResponse>> HandleAsync(SetResolutionNextNumberCommand request, CancellationToken ct)
    {
        var own = await CompanyAccess.OwnCompanyAsync(companies, tenant, request.Nit, ct);
        if (!own.IsSuccess)
        {
            return own.Error;
        }

        var companyId = own.Value.Id;
        var resolution = await companies.FindResolutionAsync(companyId, request.ResolutionId, ct);
        if (resolution is null)
        {
            return NotFound;
        }

        var requested = request.NextNumber!.Value;
        if (requested < resolution.RangeFrom || requested > resolution.RangeTo)
        {
            return Invalid(NextNumberMessages.OutOfRange(resolution.RangeFrom, resolution.RangeTo));
        }

        var next = await advance.AdvanceToAsync(companyId, NumberingScope.ForResolution(resolution.Id), requested, ct);
        if (next is null)
        {
            // Resolución sin consecutivo (por ejemplo, cargada sin la ruta de resoluciones): se crea en el número pedido.
            await numbering.OpenForResolutionAsync(companyId, resolution.Id, requested, ct);
            next = requested;
        }
        else if (next > requested)
        {
            return Invalid(NextNumberMessages.Backwards(next.Value));
        }

        return new ResolutionNextNumberResponse { ResolutionId = resolution.Id, NextNumber = next.Value };
    }

    private static Error Invalid(string message) => Error.Validation([new FieldError("next_number", message)]);
}
