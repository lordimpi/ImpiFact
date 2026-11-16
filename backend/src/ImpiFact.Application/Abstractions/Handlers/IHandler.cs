using ImpiFact.Application.Abstractions.Results;

namespace ImpiFact.Application.Abstractions.Handlers;

/// <summary>
/// Un caso de uso (Arquitectura detallada, sección 4.1). Hay un handler por caso de uso y el endpoint lo
/// recibe por inyección de dependencias, ya envuelto en los decoradores de registro, validación, empresa y
/// transacción (ver <see cref="ApplicationServiceCollectionExtensions.AddApplication"/>).
/// </summary>
public interface IHandler<in TIn, TOut>
{
    Task<Result<TOut>> HandleAsync(TIn request, CancellationToken ct);
}

/// <summary>
/// Marca una solicitud que cambia datos. Solo estas corren dentro de una transacción, que se confirma si
/// el caso de uso sale bien y se revierte si no.
/// </summary>
public interface ICommand;

/// <summary>
/// Marca una solicitud que se puede atender sin una empresa en el contexto, por ejemplo el inicio de
/// sesión o una operación de plataforma. Las demás fallan con <see cref="Results.ErrorType.Unauthorized"/>
/// si no hay empresa: por defecto todo caso de uso exige una.
/// </summary>
public interface ICompanyOptional;
