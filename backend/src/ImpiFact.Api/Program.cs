using ImpiFact.Infrastructure;
using ImpiFact.Infrastructure.Postgres;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHealthChecks();

// Opciones validadas al arrancar, reloj, tenant y persistencia. Si falta un valor obligatorio (por ejemplo la
// cadena de conexión, que llega por la variable de entorno Database__ConnectionString), la API no arranca.
builder.Services.AddInfrastructure(builder.Configuration, new PostgresDatabaseProvider());

var app = builder.Build();

// Prueba de vida del proceso: responde 200 mientras la API esté arriba (Arquitectura, sección 13).
app.MapHealthChecks("/health/live");

app.Run();

/// <summary>Punto de entrada; es público para que las pruebas de integración puedan levantar la API.</summary>
public partial class Program;
