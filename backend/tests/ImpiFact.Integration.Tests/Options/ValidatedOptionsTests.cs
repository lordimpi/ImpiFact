using ImpiFact.Infrastructure;
using ImpiFact.Infrastructure.Options;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Postgres;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ImpiFact.Integration.Tests.Options;

/// <summary>
/// Opciones validadas al arrancar (patrón Options de .NET: clases que se llenan desde la configuración). No
/// necesitan Docker, por eso no llevan la categoría «Integration».
/// </summary>
[TestFixture]
public sealed class ValidatedOptionsTests
{
    private static readonly Dictionary<string, string?> ValidDatabase = new()
    {
        ["Database:Provider"] = TestDatabase.ProviderName,
        ["Database:ConnectionString"] = "Host=localhost;Database=impifact",
    };

    [Test]
    public void A_missing_required_value_fails_at_startup_validation()
    {
        var services = Build(new() { ["Database:Provider"] = "Postgres" },
            (collection, configuration) => collection.AddValidatedOptions<DatabaseOptions>(configuration, "Database"));

        var exception = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IStartupValidator>().Validate());

        Assert.That(exception?.Message, Does.Contain(nameof(DatabaseOptions.ConnectionString)));
    }

    [Test]
    public void Valid_values_are_bound_and_exposed_through_the_options_service()
    {
        var services = Build(ValidDatabase,
            (collection, configuration) => collection.AddValidatedOptions<DatabaseOptions>(configuration, "Database"));

        services.GetRequiredService<IStartupValidator>().Validate();
        var options = services.GetRequiredService<IOptionsService<DatabaseOptions>>().Value;

        Assert.Multiple(() =>
        {
            Assert.That(options.Provider, Is.EqualTo(TestDatabase.ProviderName));
            Assert.That(options.ConnectionString, Is.EqualTo("Host=localhost;Database=impifact"));
        });
    }

    [Test]
    public void Dian_urls_must_be_absolute_urls()
    {
        var services = Build(new()
        {
            ["Dian:HabilitacionUrl"] = "not a url",
            ["Dian:ProduccionUrl"] = "https://vpfe.dian.gov.co/WcfDianCustomerServices.svc",
            ["Dian:HabilitacionQrUrl"] = "https://catalogo-vpfe-hab.dian.gov.co/document/searchqr?documentkey=",
            ["Dian:ProduccionQrUrl"] = "https://catalogo-vpfe.dian.gov.co/document/searchqr?documentkey=",
            ["Dian:TimeoutSeconds"] = "60",
        }, (collection, configuration) => collection.AddValidatedOptions<DianOptions>(configuration, "Dian"));

        var exception = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IStartupValidator>().Validate());

        Assert.That(exception?.Message, Does.Contain(nameof(DianOptions.HabilitacionUrl)));
    }

    [Test]
    public void Add_infrastructure_validates_every_phase_two_option_at_startup()
    {
        var services = Build(ValidDatabase,
            (collection, configuration) => collection.AddInfrastructure(configuration, TestDatabase.Providers));

        // Con varias opciones inválidas, la validación del arranque las reúne todas en una AggregateException.
        var exception = Assert.Throws<AggregateException>(() => services.GetRequiredService<IStartupValidator>().Validate());
        var failedOptions = exception?.InnerExceptions.OfType<OptionsValidationException>().Select(failure => failure.OptionsType);

        // ApiKeyOptions no falla: su prefijo tiene valor por defecto («apk_») para que la API y el Migrator coincidan.
        Assert.That(failedOptions, Is.EquivalentTo(new[] { typeof(DianOptions), typeof(JwtOptions), typeof(SecretsOptions), typeof(StorageOptions) }));
    }

    [Test]
    public void An_api_key_prefix_that_is_not_lowercase_letters_and_underscore_stops_startup()
    {
        var services = Build(new() { ["ApiKey:Prefix"] = "APK" },
            (collection, configuration) => collection.AddValidatedOptions<ApiKeyOptions>(configuration, ApiKeyOptions.SectionName));

        var exception = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IStartupValidator>().Validate());

        Assert.That(exception?.Message, Does.Contain("Prefix"));
    }

    [Test]
    public void Add_infrastructure_rejects_a_database_provider_that_is_not_registered()
    {
        var configuration = Configuration(new()
        {
            ["Database:Provider"] = "SqlServer",
            ["Database:ConnectionString"] = "Server=localhost",
        });

        var exception = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddInfrastructure(configuration, TestDatabase.Providers));

        Assert.That(exception?.Message, Is.EqualTo("Database provider 'SqlServer' is not available. Available providers: Postgres, MySql."));
    }

    // Sin Database:Provider ningún motor se elige por defecto: la API, el Worker y el Migrator no arrancan.
    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    public void Add_infrastructure_requires_a_database_provider(string? provider)
    {
        var configuration = Configuration(new()
        {
            ["Database:Provider"] = provider,
            ["Database:ConnectionString"] = "Server=localhost",
        });

        var exception = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddInfrastructure(configuration, TestDatabase.Providers));

        Assert.That(exception?.Message, Is.EqualTo("Database:Provider is not set. Available providers: Postgres, MySql."));
    }

    // El nombre del motor no distingue mayúsculas, como el resto de la configuración de .NET.
    [Test]
    public void Add_infrastructure_picks_the_provider_named_in_the_configuration_ignoring_case()
    {
        var configuration = Configuration(new()
        {
            ["Database:Provider"] = "postgres",
            ["Database:ConnectionString"] = "Host=localhost;Database=impifact",
        });

        using var services = new ServiceCollection().AddInfrastructure(configuration, TestDatabase.Providers).BuildServiceProvider();

        Assert.That(services.GetRequiredService<IDatabaseProvider>(), Is.InstanceOf<PostgresDatabaseProvider>());
    }

    private static ServiceProvider Build(
        Dictionary<string, string?> values, Action<IServiceCollection, IConfiguration> register)
    {
        var collection = new ServiceCollection();
        register(collection, Configuration(values));
        return collection.BuildServiceProvider();
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
