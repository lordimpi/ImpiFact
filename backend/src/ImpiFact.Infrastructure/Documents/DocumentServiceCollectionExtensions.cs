using ImpiFact.Application.Abstractions.Documents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ImpiFact.Infrastructure.Documents;

public static class DocumentServiceCollectionExtensions
{
    /// <summary>
    /// Registra los adaptadores del módulo Documents: consecutivos, archivos de documento y los puertos de la emisión
    /// (contexto de la empresa, catálogos, documentos, llaves de idempotencia y URL del QR) y del envío a la DIAN
    /// (documento con rastreo e intentos en <c>dian_submissions</c>). El SQL del
    /// consecutivo (<see cref="INumberingSql"/>) lo registra el motor de base de datos elegido.
    /// </summary>
    public static IServiceCollection AddDocuments(this IServiceCollection services)
    {
        services.TryAddScoped<INumberingSequences, EfNumberingSequences>();
        services.TryAddScoped<IDocumentFiles, EfDocumentFiles>();
        services.TryAddScoped<IIssuerContextReads, EfIssuerContextReads>();
        services.TryAddScoped<IDocumentCatalogs, EfDocumentCatalogs>();
        services.TryAddScoped<IDocumentStore, EfDocumentStore>();
        services.TryAddScoped<IIdempotencyKeys, EfIdempotencyKeys>();
        services.TryAddScoped<IDocumentDelivery, EfDocumentDelivery>();
        services.TryAddSingleton<IDianQrUrls, DianQrUrls>();
        services.TryAddScoped<IDocumentFileReads, EfDocumentFileReads>();
        return services;
    }
}
