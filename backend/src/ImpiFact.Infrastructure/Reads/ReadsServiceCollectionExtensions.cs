using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Infrastructure.Reads.Documents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ImpiFact.Infrastructure.Reads;

public static class ReadsServiceCollectionExtensions
{
    /// <summary>
    /// Registra las consultas de solo lectura con Dapper (una librería que ejecuta SQL escrito a mano). Usan la
    /// conexión que entrega <c>IReadConnectionFactory</c>, que registra el proveedor de base de datos.
    /// </summary>
    public static IServiceCollection AddReads(this IServiceCollection services)
    {
        services.TryAddScoped<IDocumentReads, DapperDocumentReads>();
        return services;
    }
}
