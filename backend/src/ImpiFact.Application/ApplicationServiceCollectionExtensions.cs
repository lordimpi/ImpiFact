using FluentValidation;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Application.Behaviors;
using ImpiFact.Application.Features.Documents.Delivery;
using ImpiFact.Application.Features.Documents.Email;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

    /// <summary>
    /// Registra los handlers de la bandeja de salida (outbox: <see cref="IOutboxMessageHandler"/>) de Application, con
    /// ámbito por mensaje, y sus piezas comunes. Solo los usa el Worker: la API no los registra, porque no procesa la
    /// bandeja y sus dependencias (el cliente de la DIAN, el almacén de archivos) piden configuración que no necesita.
    /// </summary>
    public static IServiceCollection AddOutboxHandlers(this IServiceCollection services)
    {
        services.Scan(scan => scan
            .FromAssemblies(typeof(ApplicationServiceCollectionExtensions).Assembly)
            .AddClasses(classes => classes.AssignableTo<IOutboxMessageHandler>(), publicOnly: false)
            .As<IOutboxMessageHandler>()
            .WithScopedLifetime());

        // Piezas comunes de los handlers que hablan con la DIAN (envío y consulta de estado).
        services.TryAddScoped<DianDelivery>();
        services.TryAddScoped<Features.Documents.Pdf.DocumentPdfModelBuilder>();

        // Elige el servidor de correo de la empresa o el de la plataforma para el correo del documento (SendEmail).
        services.TryAddScoped<MailServerSelector>();
        return services;
    }

    private static bool IsDecorator(Type type) => type.Namespace == typeof(LoggingDecorator<,>).Namespace;
}
