using FluentValidation;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Behaviors;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Application;

public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registra los casos de uso y validadores de Application y envuelve cada handler registrado hasta ese
    /// momento en los decoradores. El orden, del más externo al más interno, es
    /// Logging → Validation → Tenant → Transaction → Handler (Arquitectura detallada, sección 4.1).
    /// </summary>
    /// <remarks>
    /// Scrutor deja como más externo el último decorador aplicado, por eso se aplican al revés. Los
    /// handlers registrados después de esta llamada quedan sin decorar: hay que registrarlos antes.
    /// </remarks>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.Scan(scan => scan
            .FromAssemblies(typeof(ApplicationServiceCollectionExtensions).Assembly)
            .AddClasses(classes => classes.AssignableTo(typeof(IHandler<,>)).Where(type => !IsDecorator(type)), publicOnly: false)
            .AsImplementedInterfaces()
            .WithScopedLifetime()
            .AddClasses(classes => classes.AssignableTo(typeof(IValidator<>)), publicOnly: false)
            .AsImplementedInterfaces()
            .WithScopedLifetime());

        services.TryDecorate(typeof(IHandler<,>), typeof(TransactionDecorator<,>));
        services.TryDecorate(typeof(IHandler<,>), typeof(TenantDecorator<,>));
        services.TryDecorate(typeof(IHandler<,>), typeof(ValidationDecorator<,>));
        services.TryDecorate(typeof(IHandler<,>), typeof(LoggingDecorator<,>));
        return services;
    }

    private static bool IsDecorator(Type type) => type.Namespace == typeof(LoggingDecorator<,>).Namespace;
}
