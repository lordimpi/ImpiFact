var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHealthChecks();

var app = builder.Build();

// Prueba de vida del proceso: responde 200 mientras la API esté arriba (Arquitectura, sección 13).
app.MapHealthChecks("/health/live");

app.Run();

/// <summary>Punto de entrada; es público para que las pruebas de integración puedan levantar la API.</summary>
public partial class Program;
