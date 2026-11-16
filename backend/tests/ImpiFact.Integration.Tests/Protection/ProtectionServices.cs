using ImpiFact.Application.Abstractions;
using ImpiFact.Infrastructure;
using ImpiFact.Infrastructure.Postgres;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.Protection;

/// <summary>
/// Arma el contenedor de dependencias como la API (<c>AddInfrastructure</c> con PostgreSQL) y la configuración
/// válida de la fase 2, más los ajustes que pase cada prueba. Sin una base real, la cadena de conexión no se usa.
/// </summary>
public static class ProtectionServices
{
    public static Dictionary<string, string?> ValidSettings(Dictionary<string, string?> secrets, string? connectionString = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Postgres",
            ["Database:ConnectionString"] = connectionString ?? UnreachableDatabase.ConnectionString,
            ["Dian:HabilitacionUrl"] = "https://vpfe-hab.dian.gov.co/WcfDianCustomerServices.svc",
            ["Dian:ProduccionUrl"] = "https://vpfe.dian.gov.co/WcfDianCustomerServices.svc",
            ["Dian:HabilitacionQrUrl"] = "https://catalogo-vpfe-hab.dian.gov.co/document/searchqr?documentkey=",
            ["Dian:ProduccionQrUrl"] = "https://catalogo-vpfe.dian.gov.co/document/searchqr?documentkey=",
            ["Dian:TimeoutSeconds"] = "60",
            ["Jwt:Issuer"] = "impifact",
            ["Jwt:Audience"] = "impifact-web",
            ["Jwt:AccessMinutes"] = "15",
            ["Jwt:RefreshDays"] = "14",
            ["ApiKey:Prefix"] = "apk_",
            ["Storage:Provider"] = "Local",
            ["Storage:Local:Root"] = Path.Combine(Path.GetTempPath(), "impifact-tests", "protection-storage"),
        };
        foreach (var (key, value) in secrets)
        {
            settings[key] = value;
        }

        return settings;
    }

    public static ServiceProvider Build(Dictionary<string, string?> settings, IClock? clock = null, Action<IServiceCollection>? extra = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddInfrastructure(configuration, new PostgresDatabaseProvider());
        if (clock is not null)
        {
            collection.AddSingleton(clock);
        }

        extra?.Invoke(collection);
        return collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}

/// <summary>Reloj fijo para las pruebas de esta carpeta.</summary>
public sealed class FixedProtectionClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; } = now;
}
