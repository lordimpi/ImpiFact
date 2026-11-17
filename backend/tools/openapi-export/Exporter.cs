using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace ImpiFact.OpenApiExport;

/// <summary>
/// Exporta <c>/openapi/v1.json</c> a la ruta indicada (por defecto <c>openapi.json</c> en la carpeta actual).
/// </summary>
/// <remarks>
/// Levanta la API real en memoria (WebApplicationFactory, el mismo mecanismo de las pruebas de integración) en el
/// ambiente Development, que es donde se publica el documento. La API no arranca sin cadena de conexión, llave maestra
/// ni llave de firma de los JWT (JSON Web Token): aquí se usan valores desechables en una carpeta temporal y una base
/// inalcanzable, porque generar el documento no consulta la base. Se quita <c>servers</c>: depende del host de la
/// solicitud, y el panel fija la URL base de la API en tiempo de ejecución.
/// </remarks>
internal static class Exporter
{
    public static async Task Main(string[] args)
    {
        var output = Path.GetFullPath(args.Length > 0 ? args[0] : "openapi.json");

        // Fuera de un proyecto de pruebas, WebApplicationFactory busca la carpeta de la API junto a la solución y no la
        // encuentra; la carpeta de salida tiene el appsettings.json de la API, que es lo único que necesita leer.
        Environment.SetEnvironmentVariable("ASPNETCORE_TEST_CONTENTROOT_IMPIFACT_API", AppContext.BaseDirectory);

        using var scratch = new Scratch();
        string json;
        using (var factory = new ExportFactory(scratch))
        {
            using var client = factory.CreateClient();
            using var response = await client.GetAsync("/openapi/v1.json");
            response.EnsureSuccessStatusCode();

            var document = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
            document.Remove("servers");
            json = document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n") + "\n";
        }

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(output, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Console.WriteLine($"OpenAPI escrito en {output}");
    }
}

/// <summary>La API en memoria con la configuración mínima para arrancar.</summary>
internal sealed class ExportFactory(Scratch scratch) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Database:ConnectionString", "Host=127.0.0.1;Port=1;Database=impifact_openapi_export;Timeout=1");
        builder.UseSetting("Secrets:LocalAesGcm:MasterKeyFile", scratch.MasterKeyFile);
        builder.UseSetting("Secrets:LocalAesGcm:CertificateDirectory", Path.Combine(scratch.Root, "certificates"));
        builder.UseSetting("Storage:Local:Root", Path.Combine(scratch.Root, "storage"));
        builder.UseSetting("Jwt:SigningKey", scratch.SigningKeyPem);
        builder.ConfigureLogging(logging => logging.ClearProviders());
    }
}

/// <summary>Carpeta temporal con una llave maestra y una llave de firma generadas al azar; se borra al terminar.</summary>
internal sealed class Scratch : IDisposable
{
    public Scratch()
    {
        Root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "impifact-openapi-export", Guid.NewGuid().ToString("N"))).FullName;
        MasterKeyFile = Path.Combine(Root, "master.key");
        File.WriteAllText(MasterKeyFile, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) + "\n");
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        SigningKeyPem = key.ExportPkcs8PrivateKeyPem();
    }

    public string Root { get; }

    public string MasterKeyFile { get; }

    public string SigningKeyPem { get; }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
