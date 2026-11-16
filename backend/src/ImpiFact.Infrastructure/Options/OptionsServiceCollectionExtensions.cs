using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ImpiFact.Infrastructure.Options;

public static class OptionsServiceCollectionExtensions
{
    /// <summary>
    /// Registra una clase de opciones leída de la sección indicada de la configuración y la valida con sus
    /// atributos (DataAnnotations: marcas como <c>[Required]</c> o <c>[Range]</c> sobre cada propiedad). La
    /// validación corre al arrancar (<c>ValidateOnStart</c>): si falta un valor obligatorio, por ejemplo un
    /// secreto que no llegó por variable de entorno, la aplicación no arranca en lugar de fallar después.
    /// </summary>
    /// <returns>El constructor de opciones, por si hace falta agregar validaciones propias.</returns>
    public static OptionsBuilder<T> AddValidatedOptions<T>(this IServiceCollection services, IConfiguration configuration, string section)
        where T : class
    {
        services.TryAddSingleton(typeof(IOptionsService<>), typeof(GenericOptionsService<>));
        return services.AddOptions<T>()
            .Bind(configuration.GetSection(section))
            .ValidateDataAnnotations()
            .ValidateOnStart();
    }
}
