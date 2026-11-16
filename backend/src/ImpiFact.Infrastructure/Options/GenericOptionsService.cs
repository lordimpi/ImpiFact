using Microsoft.Extensions.Options;

namespace ImpiFact.Infrastructure.Options;

/// <summary>
/// Implementación única de <see cref="IOptionsService{T}"/> para cualquier clase de opciones. Se registra como
/// genérico abierto: el contenedor de dependencias crea una por cada tipo que se pida.
/// </summary>
internal sealed class GenericOptionsService<T>(IOptions<T> options) : IOptionsService<T>
    where T : class
{
    public T Value => options.Value;
}
