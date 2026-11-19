using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Abstractions.Protection;
using ImpiFact.Domain.Companies;
using ImpiFact.Infrastructure.Persistence.Seeding;
using ImpiFact.Integration.Tests.Companies;
using ImpiFact.Integration.Tests.Model;
using ImpiFact.Integration.Tests.Protection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace ImpiFact.Integration.Tests.Masters;

/// <summary>
/// PostgreSQL desechable en Docker y la API real encima, una sola vez para las pruebas de clientes y productos (un
/// <c>SetUpFixture</c> de NUnit solo cubre su propio espacio de nombres). El esquema sale de las migraciones reales y del
/// sincronizador de catálogos; la DIAN es la falsa de <see cref="CompanyApiFactory.Dian"/>.
/// </summary>
[SetUpFixture]
[Category("Integration")]
public sealed class MastersHost
{
    private static PostgreSqlContainer? container;
    private static CompanyApiFactory? factory;
    private static int nitSequence;

    public static CompanyApiFactory Api => factory ?? throw new InvalidOperationException("The masters API host is not running.");

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        container = new PostgreSqlBuilder("postgres:18.1-alpine").Build();
        await container.StartAsync();
        factory = new CompanyApiFactory(container.GetConnectionString());

        await using var scope = factory.OpenScope(companyId: null, userId: null, ActorType.System);
        await scope.Db.Database.MigrateAsync();
        await new CatalogSynchronizer(scope.Db).SynchronizeAsync(CancellationToken.None);
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        if (factory is not null)
        {
            await factory.DisposeAsync();
        }

        if (container is not null)
        {
            await container.DisposeAsync();
        }
    }

    /// <summary>Una empresa nueva; con certificado activo guardado en el almacén real si se pide.</summary>
    public static async Task<Guid> CompanyAsync(bool withCertificate = false, DateTimeOffset? certificateNotAfter = null)
    {
        var companyId = Guid.CreateVersion7();
        var nit = (850_000_000 + Interlocked.Increment(ref nitSequence)).ToString(CultureInfo.InvariantCulture);
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        scope.Db.Add(ModelGraph.NewCompany(companyId, nit));
        await scope.Db.SaveChangesAsync();
        if (withCertificate)
        {
            var pfx = TestPfx.Rsa();
            var secretRef = await scope.Services.GetRequiredService<ICertificateStore>().SaveAsync(companyId, pfx, TestPfx.Password, CancellationToken.None);
            using var certificate = X509CertificateLoader.LoadPkcs12(pfx, TestPfx.Password);
            scope.Db.Add(new Certificate
            {
                CompanyId = companyId,
                Name = "test.p12",
                SecretRef = secretRef,
                Thumbprint = certificate.Thumbprint,
                Subject = certificate.Subject,
                Issuer = certificate.Issuer,
                NotBefore = certificate.NotBefore.ToUniversalTime(),
                NotAfter = certificateNotAfter ?? certificate.NotAfter.ToUniversalTime(),
                IsActive = true,
            });
            await scope.Db.SaveChangesAsync();
        }

        return companyId;
    }

    /// <summary>Un cliente HTTP con una API key de la empresa; por defecto con el alcance de emisión.</summary>
    public static Task<HttpClient> ClientAsync(Guid companyId, params string[] scopes) =>
        Api.ClientWithNewKeyAsync(companyId, scopes.Length == 0 ? [ApiScopes.DocumentsWrite] : scopes);

    public static async Task<(int Status, JsonElement Body)> SendAsync(HttpClient client, HttpMethod method, string url, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            var json = body as string ?? JsonSerializer.Serialize(body);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return ((int)response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    /// <summary>Recorre todas las páginas de un listado y devuelve el valor de <paramref name="field"/> de cada fila.</summary>
    public static async Task<(List<string> Values, int Pages)> WalkAsync(HttpClient client, string url, string field)
    {
        var values = new List<string>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var separator = url.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            var (status, body) = await SendAsync(client, HttpMethod.Get, url + (cursor is null ? "" : separator + "cursor=" + Uri.EscapeDataString(cursor)));
            Assert.That(status, Is.EqualTo(200), body.ToString());
            values.AddRange(body.GetProperty("items").EnumerateArray().Select(item => item.GetProperty(field).GetString()!));
            cursor = body.GetProperty("next_cursor").ValueKind == JsonValueKind.Null ? null : body.GetProperty("next_cursor").GetString();
            pages++;
        }
        while (cursor is not null && pages < 20);

        return (values, pages);
    }
}
