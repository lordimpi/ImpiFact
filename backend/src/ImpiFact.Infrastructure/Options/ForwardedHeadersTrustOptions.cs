using System.ComponentModel.DataAnnotations;
using System.Net;

namespace ImpiFact.Infrastructure.Options;

/// <summary>
/// Proxies de confianza (Arquitectura detallada, sección 9.1). Detrás de un proxy o ingress (la puerta de entrada
/// del clúster), la IP (Internet Protocol) real del cliente llega en el encabezado <c>X-Forwarded-For</c>. La API
/// solo cree ese encabezado si la conexión viene de una dirección de esta lista o de la propia máquina (loopback),
/// que siempre es de confianza. Si se creyera a cualquiera, un cliente podría elegir su IP y saltarse el límite de
/// solicitudes por IP. Las dos listas están vacías por defecto; cada despliegue agrega las suyas.
/// </summary>
public sealed class ForwardedHeadersTrustOptions : IValidatableObject
{
    public const string SectionName = "ForwardedHeaders";

    /// <summary>Direcciones IP exactas de los proxies, por ejemplo <c>10.0.0.5</c>.</summary>
    public string[] KnownProxies { get; set; } = [];

    /// <summary>Redes de los proxies en notación CIDR (dirección y largo del prefijo), por ejemplo <c>10.0.0.0/8</c>.</summary>
    public string[] KnownNetworks { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        for (var index = 0; index < KnownProxies.Length; index++)
        {
            if (!IPAddress.TryParse(KnownProxies[index], out _))
            {
                yield return new ValidationResult(
                    $"{SectionName}:{nameof(KnownProxies)}:{index} is not a valid IP address.", [nameof(KnownProxies)]);
            }
        }

        for (var index = 0; index < KnownNetworks.Length; index++)
        {
            if (!IPNetwork.TryParse(KnownNetworks[index], out _))
            {
                yield return new ValidationResult(
                    $"{SectionName}:{nameof(KnownNetworks)}:{index} is not a valid network in CIDR notation (for example 10.0.0.0/8).",
                    [nameof(KnownNetworks)]);
            }
        }
    }
}
