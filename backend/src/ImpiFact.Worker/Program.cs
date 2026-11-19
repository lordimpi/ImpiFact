using ImpiFact.Infrastructure.Observability;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Worker;
using ImpiFact.Infrastructure.Platform.Outbox;
using ImpiFact.Infrastructure.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Host genérico de .NET (sin servidor web). La configuración sale de appsettings.json junto al ejecutable, de
// appsettings.<Ambiente>.json y de variables de entorno, por ejemplo Database__ConnectionString. Los secretos (la
// cadena de conexión, la llave maestra) van solo en variables de entorno o en el gestor de secretos de la nube, nunca
// en un archivo versionado. Las opciones se validan al arrancar: si falta un valor obligatorio, el Worker no arranca.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// En Development el host revisa por defecto, al construirse, que todo servicio registrado se pueda crear. Aquí esa
// revisión se apaga (como en producción, donde ya viene apagada): AddApplication también registra los casos de uso de
// la API, como el inicio de sesión, cuyos adaptadores (la firma de JWT, el token del panel) el Worker no carga a
// propósito para no necesitar ese secreto, y nunca los usa. La revisión de ámbitos sigue activa en Development.
builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
{
    ValidateScopes = builder.Environment.IsDevelopment(),
    ValidateOnBuild = false,
}));

// Infrastructure, Application (con los handlers de la bandeja de salida) y el procesador: la misma composición que
// arma la prueba del Worker (WorkerCompositionTests), que además resuelve cada handler porque aquí no se revisa al construir.
builder.Services.AddWorkerServices(builder.Configuration, new PostgresDatabaseProvider());

// Registros JSON con Serilog, sin secretos (Arquitectura detallada, sección 12).
builder.AddImpiFactObservability(WorkerHost.ServiceName);

// Si la base tiene migraciones pendientes, el Worker no arranca (no corre ninguna tarea): escribe cuáles faltan y
// termina con código 1. Es la misma revisión de la API (PendingMigrationsGuard).
return await builder.Build().RunWhenDatabaseIsMigratedAsync();
