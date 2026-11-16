namespace ImpiFact.Infrastructure.Options;

/// <summary>
/// Acceso a una clase de opciones ya validada (Arquitectura detallada, sección 4.3). Las opciones son clases
/// que .NET llena desde la configuración (<c>appsettings.json</c>, variables de entorno o el proveedor de
/// secretos). Este servicio evita que el resto del código dependa de <c>IOptions&lt;T&gt;</c> de Microsoft.
/// </summary>
public interface IOptionsService<out T>
    where T : class
{
    T Value { get; }
}
