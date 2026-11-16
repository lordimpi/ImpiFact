using ImpiFact.Application.Abstractions.Documents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ImpiFact.Infrastructure.Documents;

public static class DocumentServiceCollectionExtensions
{
    /// <summary>
    /// Registra los adaptadores de escritura del módulo Documents: consecutivos y archivos de documento. El SQL del
    /// consecutivo (<see cref="INumberingSql"/>) lo registra el motor de base de datos elegido.
    /// </summary>
    public static IServiceCollection AddDocuments(this IServiceCollection services)
    {
        services.TryAddScoped<INumberingSequences, EfNumberingSequences>();
        services.TryAddScoped<IDocumentFiles, EfDocumentFiles>();
        return services;
    }
}
