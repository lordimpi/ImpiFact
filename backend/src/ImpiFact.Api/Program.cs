using ImpiFact.Api;
using ImpiFact.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.AddImpiFactApi();

var app = builder.Build();
app.UseImpiFactPipeline();

// Si la base tiene migraciones pendientes, la API no arranca: escribe cuáles faltan y termina con código 1.
return await app.RunWhenDatabaseIsMigratedAsync();

/// <summary>Punto de entrada; es público para que las pruebas de integración puedan levantar la API.</summary>
public partial class Program;
