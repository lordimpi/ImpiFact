using ImpiFact.Migrator;
using Microsoft.Extensions.Configuration;

// La configuración sale de appsettings.json junto al ejecutable (opcional) y de variables de entorno, por ejemplo
// Database__ConnectionString. La cadena de conexión es un secreto: va solo en variables de entorno o en el gestor
// de secretos de la nube, nunca en un archivo versionado.
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

return await MigratorApp.RunAsync(args, configuration, Console.Out, Console.Error, cancellation.Token);
