using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Application.Abstractions.Protection;
using ImpiFact.Application.Features.Documents.Delivery;
using ImpiFact.Application.Features.Documents.Issue;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Companies;
using ImpiFact.Domain.Documents;
using ImpiFact.Domain.Platform;
using ImpiFact.Infrastructure.Dian;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Persistence.Seeding;
using ImpiFact.Infrastructure.Platform.Outbox;
using ImpiFact.Infrastructure.Postgres;
using ImpiFact.Infrastructure.Tenancy;
using ImpiFact.Integration.Tests.Model;
using ImpiFact.Integration.Tests.Persistence;
using ImpiFact.Integration.Tests.Protection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace ImpiFact.Integration.Tests.DianSend;

/// <summary>
/// PostgreSQL desechable en Docker (con Testcontainers) para el envío de documentos a la DIAN (la autoridad tributaria
/// de Colombia) desde el Worker, con el esquema de las migraciones reales y los catálogos del sincronizador. Es propio
/// porque un <c>SetUpFixture</c> de NUnit solo cubre su espacio de nombres.
/// </summary>
[SetUpFixture]
[Category("Integration")]
public sealed class DianSendDatabase
{
    private static PostgreSqlContainer? container;
    private static int nitSequence;

    public static string ConnectionString =>
        container?.GetConnectionString() ?? throw new InvalidOperationException("The PostgreSQL container is not running.");

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        container = new PostgreSqlBuilder("postgres:18.1-alpine").Build();
        await container.StartAsync();

        await using var host = DianSendHost.Create();
        await using var scope = host.OpenScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>();
        await db.Database.MigrateAsync();
        await new CatalogSynchronizer(db).SynchronizeAsync(CancellationToken.None);
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        if (container is not null)
        {
            await container.DisposeAsync();
        }
    }

    internal static string NextNit() =>
        (820000000 + Interlocked.Increment(ref nitSequence)).ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// La composición real del Worker (<see cref="OutboxServiceCollectionExtensions.AddWorkerServices"/>) contra la base de
/// las pruebas, con un reloj fijo, una carpeta temporal para los certificados y los archivos, y una DIAN falsa
/// (<see cref="FakeDian"/>) como manejador final del <see cref="HttpClient"/>: la cadena de resiliencia real sigue
/// delante, con esperas cortas entre reintentos.
/// </summary>
public sealed class DianSendHost : IAsyncDisposable
{
    public const string HabilitationUrl = "https://vpfe-hab.dian.test/WcfDianCustomerServices.svc";
    public const string ProductionUrl = "https://vpfe.dian.test/WcfDianCustomerServices.svc";

    private const int PesoColombiano = 35;

    private static int documentNumber = 990000000;

    private readonly ProtectionScratch protection = new();
    private readonly ServiceProvider services;

    private DianSendHost(IReadOnlyDictionary<string, string?> settings)
    {
        var values = new Dictionary<string, string?>(protection.LocalAesGcmSettings())
        {
            ["Database:Provider"] = "Postgres",
            ["Database:ConnectionString"] = DianSendDatabase.ConnectionString,
            ["Storage:Provider"] = "Local",
            ["Storage:Local:Root"] = Path.Combine(protection.Root, "storage"),
            ["Dian:HabilitacionUrl"] = HabilitationUrl,
            ["Dian:ProduccionUrl"] = ProductionUrl,
            ["Dian:HabilitacionQrUrl"] = "https://catalogo-vpfe-hab.dian.test/document/searchqr?documentkey=",
            ["Dian:ProduccionQrUrl"] = "https://catalogo-vpfe.dian.test/document/searchqr?documentkey=",
            ["Dian:TimeoutSeconds"] = "60",
            ["Resilience:BaseDelaySeconds"] = "0.01",
        };
        foreach (var (key, value) in settings)
        {
            values[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddWorkerServices(configuration, new PostgresDatabaseProvider());
        collection.AddSingleton<IClock>(Clock);
        collection.AddHttpClient(DianSoapClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => Dian)
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
        services = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>Hora fija de las pruebas: 2-mar-2026 a las 13:00 UTC (08:00 en Colombia).</summary>
    public FixedClock Clock { get; } = new(new DateTimeOffset(2026, 3, 2, 13, 0, 0, TimeSpan.Zero));

    public FakeDian Dian { get; } = new();

    internal OutboxProcessor Processor => services.GetRequiredService<OutboxProcessor>();

    public IServiceProvider Services => services;

    public static DianSendHost Create(IReadOnlyDictionary<string, string?>? settings = null) =>
        new(settings ?? new Dictionary<string, string?>());

    /// <summary>
    /// Un host nuevo sin mensajes pendientes de otras pruebas: los que quedaron (por ejemplo, consultas programadas) se
    /// marcan hechos, para que el procesador de esta prueba solo tome los suyos.
    /// </summary>
    public static async Task<DianSendHost> StartAsync(IReadOnlyDictionary<string, string?>? settings = null)
    {
        var host = Create(settings);
        await using var scope = host.OpenScope();
        await scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>().Set<OutboxMessage>()
            .Where(message => message.Status == OutboxStatus.Pending || message.Status == OutboxStatus.Processing)
            .ExecuteUpdateAsync(setters => setters.SetProperty(message => message.Status, OutboxStatus.Done));
        return host;
    }

    /// <summary>Abre un ámbito de servicios como proceso del sistema, que puede escribir para cualquier empresa.</summary>
    public AsyncServiceScope OpenScope()
    {
        var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(companyId: null, userId: null, ActorType.System);
        return scope;
    }

    /// <summary>Una empresa nueva con su certificado de prueba activo, guardado en el almacén real de certificados.</summary>
    public async Task<TestIssuer> CreateIssuerAsync()
    {
        var companyId = Guid.CreateVersion7();
        var nit = DianSendDatabase.NextNit();
        await using var scope = OpenScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>();
        db.Add(ModelGraph.NewCompany(companyId, nit));
        await db.SaveChangesAsync();

        var pfx = TestPfx.Rsa();
        var secretRef = await scope.ServiceProvider.GetRequiredService<ICertificateStore>().SaveAsync(companyId, pfx, TestPfx.Password, CancellationToken.None);
        using var certificate = X509CertificateLoader.LoadPkcs12(pfx, TestPfx.Password);
        db.Add(new Certificate
        {
            CompanyId = companyId,
            Name = "test.p12",
            SecretRef = secretRef,
            Thumbprint = certificate.Thumbprint,
            Subject = certificate.Subject,
            Issuer = certificate.Issuer,
            NotBefore = certificate.NotBefore.ToUniversalTime(),
            NotAfter = certificate.NotAfter.ToUniversalTime(),
            IsActive = true,
        });
        await db.SaveChangesAsync();
        return new TestIssuer(companyId, nit);
    }

    /// <summary>
    /// Guarda un documento en <paramref name="status"/> con su XML firmado (de prueba) y, si está en <c>Queued</c>, el
    /// mensaje <c>SendDocumentToDian</c> que deja la emisión.
    /// </summary>
    public async Task<Document> CreateDocumentAsync(
        TestIssuer issuer,
        int documentTypeId = 1,
        int typeEnvironmentId = 2,
        string? testSetId = null,
        DocumentStatus status = DocumentStatus.Queued,
        DateTimeOffset? sentAt = null,
        string? trackId = null)
    {
        var number = Interlocked.Increment(ref documentNumber);
        var prefix = documentTypeId switch { 4 => "NC", 5 => "ND", _ => "SETP" };
        var document = new Document
        {
            Id = Guid.CreateVersion7(),
            CompanyId = issuer.CompanyId,
            DocumentTypeId = documentTypeId,
            TypeEnvironmentId = typeEnvironmentId,
            Prefix = prefix,
            Number = number,
            FullNumber = prefix + number.ToString(CultureInfo.InvariantCulture),
            Status = status,
            Cufe = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(48)),
            UuidAlgorithm = "CUFE-SHA384",
            IssueDate = new DateOnly(2026, 3, 2),
            IssueTime = new TimeOnly(8, 0),
            IssuedAt = Clock.UtcNow,
            CustomerSnapshot = new CustomerSnapshot { IdentificationNumber = "222222222222", Name = "Consumidor final" },
            CurrencyId = PesoColombiano,
            LineExtensionAmount = 100m,
            TaxExclusiveAmount = 100m,
            TaxInclusiveAmount = 119m,
            PayableAmount = 119m,
            CreatedByType = ActorKind.ApiKey,
            SentAt = sentAt,
            TrackId = trackId,
        };

        await using var scope = OpenScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>();
        db.Add(document);
        await scope.ServiceProvider.GetRequiredService<IDocumentFiles>().AddAsync(
            new NewDocumentFile(issuer.CompanyId, document.Id, DocumentFileKind.SignedXml, Encoding.UTF8.GetBytes(SignedXml(document)), "application/xml"),
            CancellationToken.None);
        if (status == DocumentStatus.Queued)
        {
            await scope.ServiceProvider.GetRequiredService<IOutbox>().EnqueueAsync(
                OutboxMessageTypes.SendDocumentToDian, new SendDocumentToDianPayload(document.Id, testSetId), issuer.CompanyId, null,
                CancellationToken.None);
        }

        await db.SaveChangesAsync();
        return document;
    }

    /// <summary>Encola otro mensaje <c>SendDocumentToDian</c> para el documento, como una entrega repetida.</summary>
    public async Task EnqueueSendAsync(Document document)
    {
        await using var scope = OpenScope();
        await scope.ServiceProvider.GetRequiredService<IOutbox>().EnqueueAsync(
            OutboxMessageTypes.SendDocumentToDian, new SendDocumentToDianPayload(document.Id, null), document.CompanyId, null, CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>().SaveChangesAsync();
    }

    /// <summary>Encola una consulta de estado como la que programa el envío.</summary>
    public async Task<Guid> EnqueuePollAsync(Document document, DianStatusQuery query)
    {
        await using var scope = OpenScope();
        var id = await scope.ServiceProvider.GetRequiredService<IOutbox>().EnqueueAsync(
            OutboxMessageTypes.PollDianStatus, new PollDianStatusPayload(document.Id, query), document.CompanyId, null, CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>().SaveChangesAsync();
        return id;
    }

    /// <summary>Procesa una tanda del outbox y devuelve cuántos mensajes tomó.</summary>
    public Task<int> RunOutboxAsync() => Processor.ProcessBatchAsync(CancellationToken.None);

    public async Task<Document> DocumentAsync(Guid id)
    {
        await using var scope = OpenScope();
        return await Db(scope).Set<Document>().IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking().SingleAsync(row => row.Id == id);
    }

    public async Task<List<DianSubmission>> SubmissionsAsync(Guid documentId)
    {
        await using var scope = OpenScope();
        return await Db(scope).Set<DianSubmission>().IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking()
            .Where(row => row.DocumentId == documentId).OrderBy(row => row.Attempt).ToListAsync();
    }

    public async Task<List<DocumentFile>> FilesAsync(Guid documentId)
    {
        await using var scope = OpenScope();
        return await Db(scope).Set<DocumentFile>().IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking()
            .Where(row => row.DocumentId == documentId).OrderBy(row => row.Kind).ThenBy(row => row.Revision).ToListAsync();
    }

    /// <summary>Mensajes de la empresa de un tipo, del más antiguo al más nuevo.</summary>
    public async Task<List<OutboxMessage>> MessagesAsync(Guid companyId, string type)
    {
        await using var scope = OpenScope();
        return await Db(scope).Set<OutboxMessage>().AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.Type == type).OrderBy(row => row.CreatedAt).ThenBy(row => row.Id).ToListAsync();
    }

    public async Task<List<Notification>> NotificationsAsync(Guid companyId)
    {
        await using var scope = OpenScope();
        return await Db(scope).Set<Notification>().IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking()
            .Where(row => row.CompanyId == companyId).ToListAsync();
    }

    public async Task<long?> NextFileCounterAsync(Guid companyId, string scopeKey)
    {
        await using var scope = OpenScope();
        return await Db(scope).Set<NumberingSequence>().IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.ScopeKey == scopeKey).Select(row => (long?)row.NextNumber).SingleOrDefaultAsync();
    }

    /// <summary>Contenido guardado de un archivo de documento.</summary>
    public async Task<byte[]> ReadFileAsync(DocumentFile file)
    {
        await using var scope = OpenScope();
        await using var stream = await scope.ServiceProvider.GetRequiredService<Application.Abstractions.Storage.IBlobStorage>()
            .OpenReadAsync(file.StorageKey, CancellationToken.None);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    /// <summary>Nombre de la única entrada de un ZIP.</summary>
    public static string ZipEntryName(byte[] zip)
    {
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        return archive.Entries.Single().FullName;
    }

    public async ValueTask DisposeAsync()
    {
        await services.DisposeAsync();
        Dian.Dispose();
        protection.Dispose();
    }

    private static string SignedXml(Document document) =>
        $"<Invoice xmlns=\"urn:oasis:names:specification:ubl:schema:xsd:Invoice-2\"><ID>{document.FullNumber}</ID></Invoice>";

    private static ImpiFactDbContext Db(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>();
}

/// <summary>Empresa emisora de una prueba.</summary>
public sealed record TestIssuer(Guid CompanyId, string Nit);
