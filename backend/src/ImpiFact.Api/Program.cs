using ImpiFact.Api;

var builder = WebApplication.CreateBuilder(args);
builder.AddImpiFactApi();

var app = builder.Build();
app.UseImpiFactPipeline();
app.Run();

/// <summary>Punto de entrada; es público para que las pruebas de integración puedan levantar la API.</summary>
public partial class Program;
