using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Results;

namespace ImpiFact.Application.Behaviors;

/// <summary>
/// Tercer decorador: corta la solicitud si el caso de uso necesita una empresa y el contexto no trae
/// ninguna. Por defecto todo caso de uso la necesita; solo las solicitudes marcadas con
/// <see cref="ICompanyOptional"/> pasan sin empresa.
/// </summary>
internal sealed class TenantDecorator<TIn, TOut>(IHandler<TIn, TOut> inner, ITenantContext tenant) : IHandler<TIn, TOut>
{
    internal static readonly Error CompanyRequired =
        new("tenant.company_required", "La solicitud no está asociada a ninguna empresa.", ErrorType.Unauthorized);

    private static readonly bool RequiresCompany = !typeof(ICompanyOptional).IsAssignableFrom(typeof(TIn));

    public Task<Result<TOut>> HandleAsync(TIn request, CancellationToken ct) =>
        RequiresCompany && tenant.CompanyId is null
            ? Task.FromResult(Result<TOut>.Fail(CompanyRequired))
            : inner.HandleAsync(request, ct);
}
