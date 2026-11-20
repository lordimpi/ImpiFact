using ImpiFact.Application.Abstractions.Protection;
using ImpiFact.Infrastructure.Options;
using ImpiFact.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ImpiFact.Infrastructure.Protection;

public static class SecretsServiceCollectionExtensions
{
    /// <summary>
    /// Nombre de aplicación de Data Protection. Es fijo para que todas las instancias (API, Worker, varias réplicas)
    /// compartan las mismas llaves; cambiarlo deja ilegibles los campos <c>*_cipher</c> ya guardados.
    /// </summary>
    public const string DataProtectionApplicationName = "ImpiFact";

    /// <summary>
    /// Registra la protección de secretos (Arquitectura detallada, secciones 5 y 19.2): la validación al arrancar, el
    /// almacén de certificados del proveedor de <c>Secrets:Provider</c>, la revisión de certificados y
    /// <see cref="ISecretProtector"/> sobre Data Protection, con sus llaves en <c>data_protection_keys</c> cifradas con la
    /// llave maestra local (<c>LocalAesGcm</c>) o envueltas con la llave del servicio de la nube. Espera que
    /// <see cref="SecretsOptions"/> ya esté registrado.
    /// </summary>
    public static IServiceCollection AddSecrets(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SecretsOptions>, SecretsOptionsValidator>());
        services.TryAddSingleton<LocalMasterKey>();
        services.TryAddSingleton<KeyVaultClients>();
        services.TryAddSingleton<ICertificateStore>(provider => ProviderName(provider) switch
        {
            SecretsOptions.KeyVaultProvider => ActivatorUtilities.CreateInstance<KeyVaultCertificateStore>(provider),
            _ => ActivatorUtilities.CreateInstance<LocalAesGcmCertificateStore>(provider),
        });
        services.TryAddSingleton<IKeyEncryptionKey>(provider => ProviderName(provider) switch
        {
            SecretsOptions.KeyVaultProvider => ActivatorUtilities.CreateInstance<KeyVaultKeyEncryptionKey>(provider),
            var name => throw new InvalidOperationException($"The {name} secrets provider does not use a cloud key."),
        });
        services.TryAddSingleton<ICertificateInspector, CertificateInspector>();
        services.TryAddSingleton<ISecretProtector, DataProtectionSecretProtector>();

        services.AddDataProtection()
            .SetApplicationName(DataProtectionApplicationName)
            .SetDefaultKeyLifetime(TimeSpan.FromDays(90))
            .PersistKeysToDbContext<ImpiFactDbContext>();

        // Cada llave nueva se cifra con la llave maestra local, o se envuelve con la llave del servicio de la nube, antes de
        // guardarse en la tabla (ADR-25).
        services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(serviceProvider =>
            new ConfigureOptions<KeyManagementOptions>(options =>
                options.XmlEncryptor = ProviderName(serviceProvider) == SecretsOptions.LocalAesGcmProvider
                    ? new LocalAesGcmXmlEncryptor(serviceProvider.GetRequiredService<LocalMasterKey>())
                    : new CloudKeyXmlEncryptor(serviceProvider.GetRequiredService<IKeyEncryptionKey>())));
        return services;
    }

    private static string ProviderName(IServiceProvider provider) => provider.GetRequiredService<IOptions<SecretsOptions>>().Value.Provider;
}
