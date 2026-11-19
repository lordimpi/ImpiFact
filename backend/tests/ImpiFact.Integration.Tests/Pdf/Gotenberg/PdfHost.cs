using System.Globalization;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Application.Abstractions.Storage;
using ImpiFact.Application.Features.Documents.Delivery;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Companies;
using ImpiFact.Domain.Documents;
using ImpiFact.Domain.Platform;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Persistence.Seeding;
using ImpiFact.Infrastructure.Platform.Outbox;
using ImpiFact.Infrastructure.Tenancy;
using ImpiFact.Integration.Tests.Model;
using ImpiFact.Integration.Tests.Persistence;
using ImpiFact.Integration.Tests.Protection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.Pdf.Gotenberg;

/// <summary>
/// PostgreSQL y Gotenberg (el conversor de HTML a PDF con Chromium) desechables en Docker, con Testcontainers, para las
/// pruebas del PDF. Gotenberg usa la misma versión fijada en <c>deploy/docker-compose.yml</c>. Es propio porque un
/// <c>SetUpFixture</c> de NUnit solo cubre su espacio de nombres.
/// </summary>
[SetUpFixture]
[Category("Integration")]
public sealed class PdfContainers
{
    /// <summary>Imagen de Gotenberg, la misma del compose.</summary>
    public const string GotenbergImage = "gotenberg/gotenberg:8.39.0-chromium";

    private const ushort GotenbergPort = 3000;

    private static TestDatabaseContainer? postgres;
    private static IContainer? gotenberg;
    private static int nitSequence;

    public static string ConnectionString =>
        postgres?.GetConnectionString() ?? throw new InvalidOperationException("The PostgreSQL container is not running.");

    public static Uri GotenbergUrl => gotenberg is null
        ? throw new InvalidOperationException("The Gotenberg container is not running.")
        : new UriBuilder(Uri.UriSchemeHttp, gotenberg.Hostname, gotenberg.GetMappedPublicPort(GotenbergPort)).Uri;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        postgres = TestDatabase.CreateContainer();
        gotenberg = new ContainerBuilder(GotenbergImage)
            .WithPortBinding(GotenbergPort, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(GotenbergPort).ForPath("/health")))
            .Build();
        await Task.WhenAll(postgres.StartAsync(), gotenberg.StartAsync());

        await using var host = PdfHost.Create();
        await using var scope = host.OpenScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>();
        await db.Database.MigrateAsync();
        await new CatalogSynchronizer(db).SynchronizeAsync(CancellationToken.None);
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        if (gotenberg is not null)
        {
            await gotenberg.DisposeAsync();
        }

        if (postgres is not null)
        {
            await postgres.DisposeAsync();
        }
    }

    internal static string NextNit() =>
        (830000000 + Interlocked.Increment(ref nitSequence)).ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// La composición real del Worker (<see cref="OutboxServiceCollectionExtensions.AddWorkerServices"/>) contra los
/// contenedores de las pruebas, con un reloj fijo y una carpeta temporal para los archivos.
/// </summary>
public sealed class PdfHost : IAsyncDisposable
{
    private static int documentNumber = 990100000;

    private readonly ProtectionScratch scratch = new();
    private readonly ServiceProvider services;

    private PdfHost()
    {
        var values = new Dictionary<string, string?>(scratch.LocalAesGcmSettings())
        {
            ["Database:Provider"] = TestDatabase.ProviderName,
            ["Database:ConnectionString"] = PdfContainers.ConnectionString,
            ["Storage:Provider"] = "Local",
            ["Storage:Local:Root"] = Path.Combine(scratch.Root, "storage"),
            ["Dian:HabilitacionUrl"] = "https://vpfe-hab.dian.test/WcfDianCustomerServices.svc",
            ["Dian:ProduccionUrl"] = "https://vpfe.dian.test/WcfDianCustomerServices.svc",
            ["Dian:HabilitacionQrUrl"] = "https://catalogo-vpfe-hab.dian.test/document/searchqr?documentkey=",
            ["Dian:ProduccionQrUrl"] = "https://catalogo-vpfe.dian.test/document/searchqr?documentkey=",
            ["Pdf:GotenbergUrl"] = PdfContainers.GotenbergUrl.AbsoluteUri,
            ["Pdf:TimeoutSeconds"] = "60",
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddWorkerServices(configuration, TestDatabase.Providers);
        collection.AddSingleton<IClock>(Clock);
        services = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>Hora fija de las pruebas: 14-nov-2026 a las 15:20 UTC (10:20 en Colombia).</summary>
    public FixedClock Clock { get; } = new(new DateTimeOffset(2026, 11, 14, 15, 20, 0, TimeSpan.Zero));

    public IServiceProvider Services => services;

    /// <summary>Un host nuevo sin mensajes pendientes de otras pruebas, para que el procesador solo tome los suyos.</summary>
    public static async Task<PdfHost> StartAsync()
    {
        var host = Create();
        await using var scope = host.OpenScope();
        await scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>().Set<OutboxMessage>()
            .Where(message => message.Status == OutboxStatus.Pending || message.Status == OutboxStatus.Processing)
            .ExecuteUpdateAsync(setters => setters.SetProperty(message => message.Status, OutboxStatus.Done));
        return host;
    }

    internal static PdfHost Create() => new();

    /// <summary>Abre un ámbito de servicios como proceso del sistema, que puede escribir para cualquier empresa.</summary>
    public AsyncServiceScope OpenScope()
    {
        var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(companyId: null, userId: null, ActorType.System);
        return scope;
    }

    /// <summary>
    /// Una empresa nueva con su resolución y una factura aceptada emitida con el caso golden
    /// <c>invoice/withholdings-renta-iva-ica</c>: el documento, la solicitud JSON y el XML firmado.
    /// </summary>
    public async Task<Document> CreateAcceptedInvoiceAsync()
    {
        var companyId = Guid.CreateVersion7();
        var resolution = new Resolution
        {
            Id = Guid.CreateVersion7(),
            CompanyId = companyId,
            DocumentTypeId = 1,
            TypeEnvironmentId = 2,
            Prefix = "SETP",
            ResolutionNumber = "18760000001",
            ResolutionDate = new DateOnly(2019, 1, 19),
            TechnicalKey = "fc8eac422eba16e22ffd8c6f94b3f40a6e38162c",
            RangeFrom = 990000000,
            RangeTo = 995000000,
            ValidFrom = new DateOnly(2019, 1, 19),
            ValidTo = new DateOnly(2030, 1, 19),
            IsActive = true,
        };
        var number = Interlocked.Increment(ref documentNumber);
        var document = new Document
        {
            Id = Guid.CreateVersion7(),
            CompanyId = companyId,
            DocumentTypeId = 1,
            TypeEnvironmentId = 2,
            ResolutionId = resolution.Id,
            Prefix = "SETP",
            Number = number,
            FullNumber = "SETP" + number.ToString(CultureInfo.InvariantCulture),
            Status = DocumentStatus.Accepted,
            Cufe = GoldenCufe,
            UuidAlgorithm = "CUFE-SHA384",
            IssueDate = new DateOnly(2026, 11, 14),
            IssueTime = new TimeOnly(10, 20, 0),
            IssuedAt = Clock.UtcNow,
            CustomerSnapshot = new CustomerSnapshot
            {
                TypeDocumentIdentificationId = 6,
                IdentificationNumber = "800000001",
                Dv = "1",
                Name = "CLIENTE DE PRUEBA GOLDEN SAS",
                Phone = "3000000001",
                Address = "CARRERA 2 # 2-02",
                Email = "cliente.golden@example.com",
                MunicipalityId = 149,
                TypeOrganizationId = 1,
                TypeRegimeId = 1,
                TypeLiabilityId = 117,
            },
            CurrencyId = 35,
            LineExtensionAmount = 9200000m,
            TaxExclusiveAmount = 9200000m,
            TaxInclusiveAmount = 10948000m,
            PayableAmount = 10948000m,
            Notes = "Pago a 30 días",
            CreatedByType = ActorKind.ApiKey,
            SentAt = Clock.UtcNow,
            AcceptedAt = Clock.UtcNow.AddMinutes(1),
        };

        await using var scope = OpenScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>();
        db.Add(ModelGraph.NewCompany(companyId, PdfContainers.NextNit()));
        await db.SaveChangesAsync();
        db.AddRange(resolution, document);
        var files = scope.ServiceProvider.GetRequiredService<IDocumentFiles>();
        await files.AddAsync(new NewDocumentFile(companyId, document.Id, DocumentFileKind.RequestJson, Golden("input.json"), "application/json"), CancellationToken.None);
        await files.AddAsync(new NewDocumentFile(companyId, document.Id, DocumentFileKind.SignedXml, Golden("expected.xml"), "application/xml"), CancellationToken.None);
        await db.SaveChangesAsync();
        return document;
    }

    /// <summary>Encola un mensaje <c>RenderPdf</c> como el que deja la aceptación del documento.</summary>
    public async Task EnqueueRenderPdfAsync(Document document)
    {
        await using var scope = OpenScope();
        await scope.ServiceProvider.GetRequiredService<IOutbox>().EnqueueAsync(
            OutboxMessageTypes.RenderPdf, new RenderPdfPayload(document.Id), document.CompanyId, null, CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>().SaveChangesAsync();
    }

    /// <summary>Procesa una tanda del outbox y devuelve cuántos mensajes tomó.</summary>
    public Task<int> RunOutboxAsync() => services.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(CancellationToken.None);

    public async Task<List<DocumentFile>> FilesAsync(Guid documentId, DocumentFileKind kind)
    {
        await using var scope = OpenScope();
        return await scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>().Set<DocumentFile>()
            .IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking()
            .Where(row => row.DocumentId == documentId && row.Kind == kind).ToListAsync();
    }

    /// <summary>Mensajes de la empresa de un tipo, del más antiguo al más nuevo.</summary>
    public async Task<List<OutboxMessage>> MessagesAsync(Guid companyId, string type)
    {
        await using var scope = OpenScope();
        return await scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>().Set<OutboxMessage>().AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.Type == type).OrderBy(row => row.CreatedAt).ThenBy(row => row.Id).ToListAsync();
    }

    /// <summary>Contenido guardado de un archivo de documento.</summary>
    public async Task<byte[]> ReadAsync(DocumentFile file)
    {
        await using var scope = OpenScope();
        await using var stream = await scope.ServiceProvider.GetRequiredService<IBlobStorage>().OpenReadAsync(file.StorageKey, CancellationToken.None);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        await services.DisposeAsync();
        scratch.Dispose();
    }

    /// <summary>CUFE del caso golden, el mismo que lleva su XML firmado.</summary>
    public const string GoldenCufe = "0b7a6bf1ac6fcd7463acd47da12e65c7756dab27f9456b302238d082aeed99e47a82028975423120683190bcd89d3ded";

    private static byte[] Golden(string file)
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ImpiFact.slnx")))
        {
            directory = directory.Parent;
        }

        return File.ReadAllBytes(Path.Combine(
            directory?.FullName ?? throw new InvalidOperationException("The backend folder was not found."),
            "tests", "golden", "invoice", "withholdings-renta-iva-ica", file));
    }
}
