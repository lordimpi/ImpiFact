using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Protection;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Companies;
using ImpiFact.Domain.Documents;
using ImpiFact.Domain.Platform;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Persistence.Seeding;
using ImpiFact.Integration.Tests.Companies;
using ImpiFact.Integration.Tests.DianSend;
using ImpiFact.Integration.Tests.Model;
using ImpiFact.Integration.Tests.Protection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace ImpiFact.Integration.Tests.DocumentUtilities;

/// <summary>
/// PostgreSQL desechable en Docker y la API real encima, una sola vez para las pruebas de las rutas utilitarias
/// (carpetas 03, 05, 06 y 10 del Postman). Un <c>SetUpFixture</c> de NUnit solo cubre su propio espacio de nombres.
/// La DIAN es la falsa de <see cref="CompanyApiFactory.Dian"/>.
/// </summary>
[SetUpFixture]
[Category("Integration")]
public sealed class UtilitiesHost
{
    private static PostgreSqlContainer? container;
    private static CompanyApiFactory? factory;
    private static int nitSequence;
    private static int documentNumber = 990000000;

    public static CompanyApiFactory Api => factory ?? throw new InvalidOperationException("The utilities API host is not running.");

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

    /// <summary>Una empresa nueva; con certificado activo guardado en el almacén real, salvo que se pida lo contrario.</summary>
    /// <param name="certificateNotAfter">Fin de vigencia que se guarda en la fila del certificado (por defecto el del archivo).</param>
    public static async Task<UtilityCompany> CompanyAsync(bool withCertificate = true, DateTimeOffset? certificateNotAfter = null)
    {
        var companyId = Guid.CreateVersion7();
        var nit = (830_000_000 + Interlocked.Increment(ref nitSequence)).ToString(CultureInfo.InvariantCulture);
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

        return new UtilityCompany(companyId, nit);
    }

    public static Task<HttpClient> ClientAsync(UtilityCompany company, params string[] scopes) =>
        Api.ClientWithNewKeyAsync(company.Id, scopes);

    /// <summary>
    /// Un documento en <paramref name="status"/> con su XML firmado de prueba y, si se indica, la solicitud guardada
    /// (<c>document_files</c> de tipo 4).
    /// </summary>
    public static async Task<Document> DocumentAsync(
        UtilityCompany company,
        DocumentStatus status = DocumentStatus.Accepted,
        int documentTypeId = 1,
        string prefix = "SETP",
        string? requestJson = null,
        CustomerSnapshot? customer = null,
        EmailStatus emailStatus = EmailStatus.NotSent)
    {
        var number = Interlocked.Increment(ref documentNumber);
        var document = new Document
        {
            Id = Guid.CreateVersion7(),
            CompanyId = company.Id,
            DocumentTypeId = documentTypeId,
            TypeEnvironmentId = 2,
            Prefix = prefix,
            Number = number,
            FullNumber = prefix + number.ToString(CultureInfo.InvariantCulture),
            Status = status,
            Cufe = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(48)),
            UuidAlgorithm = documentTypeId == 1 ? "CUFE-SHA384" : "CUDE-SHA384",
            IssueDate = new DateOnly(2026, 1, 15),
            IssueTime = new TimeOnly(5, 0),
            IssuedAt = CompaniesHost.Now,
            CustomerSnapshot = customer ?? new CustomerSnapshot
            {
                IdentificationNumber = "900123456", Name = "Cliente de prueba SAS", Email = "cliente@example.com",
            },
            CurrencyId = 35,
            LineExtensionAmount = 100m,
            TaxExclusiveAmount = 100m,
            TaxInclusiveAmount = 119m,
            PayableAmount = 119m,
            CreatedByType = ActorKind.ApiKey,
            EmailStatus = emailStatus,
        };

        await using var scope = Api.OpenScope(null, null, ActorType.System);
        scope.Db.Add(document);
        var files = scope.Services.GetRequiredService<IDocumentFiles>();
        await files.AddAsync(
            new NewDocumentFile(company.Id, document.Id, DocumentFileKind.SignedXml, Encoding.UTF8.GetBytes(DianSendHost.SignedXml(document)),
                "application/xml"), CancellationToken.None);
        if (requestJson is not null)
        {
            await files.AddAsync(
                new NewDocumentFile(company.Id, document.Id, DocumentFileKind.RequestJson, Encoding.UTF8.GetBytes(requestJson), "application/json"),
                CancellationToken.None);
        }

        await scope.Db.SaveChangesAsync();
        return document;
    }

    /// <summary>Guarda otra revisión de un archivo del documento.</summary>
    public static async Task FileAsync(Document document, DocumentFileKind kind, byte[] content, string contentType, int revision = 1)
    {
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        await scope.Services.GetRequiredService<IDocumentFiles>().AddAsync(
            new NewDocumentFile(document.CompanyId, document.Id, kind, content, contentType, revision), CancellationToken.None);
        await scope.Db.SaveChangesAsync();
    }

    /// <summary>Un intento contra la DIAN con la respuesta guardada como la deja el Worker (<c>document_files</c> tipo 5).</summary>
    public static async Task SubmissionAsync(
        Document document, int attempt, DianOperation operation, string? responseBody, string? zipKey = null, DateTimeOffset? finishedAt = null)
    {
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        Guid? responseFileId = null;
        if (responseBody is not null)
        {
            responseFileId = (await scope.Services.GetRequiredService<IDocumentFiles>().AddAsync(
                new NewDocumentFile(document.CompanyId, document.Id, DocumentFileKind.DianResponse, Encoding.UTF8.GetBytes(responseBody),
                    "application/soap+xml", attempt), CancellationToken.None)).Id;
        }

        var started = (finishedAt ?? CompaniesHost.Now).AddSeconds(-1);
        scope.Db.Add(new DianSubmission
        {
            Id = Guid.CreateVersion7(),
            CompanyId = document.CompanyId,
            DocumentId = document.Id,
            Attempt = attempt,
            Operation = operation,
            TypeEnvironmentId = document.TypeEnvironmentId,
            TrackId = zipKey,
            ZipKey = zipKey,
            ResponseFileId = responseFileId,
            StartedAt = started,
            FinishedAt = responseBody is null ? null : started.AddSeconds(1),
        });
        await scope.Db.SaveChangesAsync();
    }

    /// <summary>Mensajes de la bandeja de salida de la empresa con ese tipo.</summary>
    public static async Task<List<OutboxMessage>> MessagesAsync(Guid companyId, string type)
    {
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        return await scope.Db.Set<OutboxMessage>().AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.Type == type).OrderBy(row => row.CreatedAt).ToListAsync();
    }

    public static async Task<Document> ReloadAsync(Document document)
    {
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        return await scope.Db.Set<Document>().IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking().SingleAsync(row => row.Id == document.Id);
    }

    /// <summary>Id del documento guardado en el mensaje (<c>document_id</c> del JSON en snake_case).</summary>
    public static Guid DocumentIdOf(OutboxMessage message) => message.Payload["document_id"]!.GetValue<Guid>();

    public static async Task<(int Status, JsonElement Body)> SendAsync(HttpClient client, HttpMethod method, string url, string? json = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return ((int)response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }
}

/// <summary>Empresa de una prueba de las rutas utilitarias.</summary>
public sealed record UtilityCompany(Guid Id, string Nit);
