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
    /// Registra la protección de secretos (Arquitectura detallada, secciones 5 y 19.2): la validación de la llave
    /// maestra al arrancar, el almacén de certificados <c>LocalAesGcm</c>, la revisión de certificados y
    /// <see cref="ISecretProtector"/> sobre Data Protection, con sus llaves en <c>data_protection_keys</c>
    /// cifradas con la llave maestra local. Espera que <see cref="SecretsOptions"/> ya esté registrado.
    /// </summary>
    public static IServiceCollection AddSecrets(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SecretsOptions>, SecretsOptionsValidator>());
        services.TryAddSingleton<LocalMasterKey>();
        services.TryAddSingleton<ICertificateStore, LocalAesGcmCertificateStore>();
        services.TryAddSingleton<ICertificateInspector, CertificateInspector>();
        services.TryAddSingleton<ISecretProtector, DataProtectionSecretProtector>();

        services.AddDataProtection()
            .SetApplicationName(DataProtectionApplicationName)
            .SetDefaultKeyLifetime(TimeSpan.FromDays(90))
            .PersistKeysToDbContext<ImpiFactDbContext>();

        // Cada llave nueva se cifra con la llave maestra local antes de guardarse en la tabla (ADR-25).
        services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(serviceProvider =>
            new ConfigureOptions<KeyManagementOptions>(options =>
                options.XmlEncryptor = new LocalAesGcmXmlEncryptor(serviceProvider.GetRequiredService<LocalMasterKey>())));
        return services;
    }
}
