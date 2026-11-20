using System.Globalization;
using DotNet.Testcontainers.Containers;
using ImpiFact.Application.Abstractions.Maintenance;
using ImpiFact.Domain.Companies;
using ImpiFact.Domain.Documents;
using ImpiFact.Domain.Platform;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Persistence.Seeding;
using ImpiFact.Infrastructure.Platform.Scheduling;
using ImpiFact.Integration.Tests.DianSend;
using ImpiFact.Integration.Tests.Email;
using ImpiFact.Integration.Tests.Model;
using ImpiFact.Integration.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.Scheduling;

/// <summary>
/// PostgreSQL desechable y un capturador de correo (MailHog) en Docker, con Testcontainers, para las tareas
/// programadas diarias del Worker (Arquitectura detallada, sección 8). Es propio porque un <c>SetUpFixture</c> de
/// NUnit solo cubre su espacio de nombres.
/// </summary>
[SetUpFixture]
[Category("Integration")]
public sealed class SchedulingEnvironment
{
    private static TestDatabaseServer? database;
    private static IContainer? mailHog;
    private static int nitSequence;

    public static string ConnectionString =>
        database?.GetConnectionString() ?? throw new InvalidOperationException("The PostgreSQL container is not running.");

    private static IContainer MailHog => mailHog ?? throw new InvalidOperationException("The MailHog container is not running.");

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        database = TestDatabase.CreateServer();
        mailHog = EmailEnvironment.NewMailHog();
        await Task.WhenAll(database.StartAsync(), mailHog.StartAsync());

        await using var host = SchedulingHost.Create();
        await using var scope = host.Worker.OpenScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>();
        await db.Database.MigrateAsync();
        await new CatalogSynchronizer(db).SynchronizeAsync(CancellationToken.None);
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        if (mailHog is not null)
        {
            await mailHog.DisposeAsync();
        }

        if (database is not null)
        {
            await database.DisposeAsync();
        }
    }

    /// <summary>Servidor y puerto SMTP del capturador, vistos desde las pruebas.</summary>
    public static (string Host, int Port) Smtp => EmailEnvironment.SmtpOf(MailHog);

    /// <summary>Los correos que recibió el capturador cuyo asunto contiene <paramref name="subjectPart"/>.</summary>
    public static Task<List<CapturedEmail>> EmailsAsync(string subjectPart) => EmailEnvironment.EmailsAsync(MailHog, subjectPart);

    internal static string NextNit() =>
        (830000000 + Interlocked.Increment(ref nitSequence)).ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Una instancia del Worker con su composición real (<see cref="DianSendHost"/> sobre
/// <c>AddWorkerServices</c>) contra la base de estas pruebas, con un reloj fijo y el capturador como servidor de correo
/// de la plataforma. Dos hosts a la vez son dos instancias del Worker.
/// </summary>
public sealed class SchedulingHost : IAsyncDisposable
{
    public const string PlatformFrom = "avisos@impifact.test";

    private SchedulingHost(DianSendHost worker) => Worker = worker;

    public DianSendHost Worker { get; }

    public FixedClock Clock => Worker.Clock;

    internal ScheduledJobsService Scheduler => Worker.Services.GetRequiredService<ScheduledJobsService>();

    public static SchedulingHost Create(IReadOnlyDictionary<string, string?>? settings = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Email:Host"] = "localhost",
            ["Email:Port"] = "25",
            ["Email:Encryption"] = "none",
            ["Email:FromAddress"] = PlatformFrom,
        };
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            values[key] = value;
        }

        return new SchedulingHost(DianSendHost.Create(values, SchedulingEnvironment.ConnectionString));
    }

    /// <summary>Un host cuyo servidor de correo de la plataforma es el capturador.</summary>
    public static SchedulingHost WithMailHog(IReadOnlyDictionary<string, string?>? settings = null)
    {
        var (host, port) = SchedulingEnvironment.Smtp;
        var values = new Dictionary<string, string?>
        {
            ["Email:Host"] = host,
            ["Email:Port"] = port.ToString(CultureInfo.InvariantCulture),
        };
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            values[key] = value;
        }

        return Create(values);
    }

    /// <summary>Corre las tareas programadas como lo hace el Worker cada día.</summary>
    internal Task<IReadOnlyList<ScheduledJobRun>> RunJobsAsync() => Scheduler.RunAllAsync(CancellationToken.None);

    /// <summary>Corre solo la tarea <paramref name="name"/>.</summary>
    internal Task<ScheduledJobRun> RunJobAsync(string name) => Scheduler.RunAsync(name, CancellationToken.None);

    public Task<int> RunOutboxAsync() => Worker.RunOutboxAsync();

    /// <summary>Crea y guarda una empresa nueva, sin filas hijas; su correo es único.</summary>
    public async Task<(Guid Id, string Name, string Email)> CreateCompanyAsync()
    {
        var id = Guid.CreateVersion7();
        var company = ModelGraph.NewCompany(id, SchedulingEnvironment.NextNit());
        await using var scope = Worker.OpenScope();
        var db = Db(scope);
        db.Add(company);
        await db.SaveChangesAsync();
        return (id, company.Name, company.Email);
    }

    public async Task<Certificate> AddCertificateAsync(Guid companyId, DateTimeOffset notAfter, bool active = true)
    {
        var certificate = new Certificate
        {
            CompanyId = companyId,
            Name = "firma-" + companyId.ToString("N")[..8] + ".p12",
            SecretRef = "certificates/" + companyId.ToString("N"),
            Thumbprint = Convert.ToHexString(Guid.NewGuid().ToByteArray()) + "00000000",
            Subject = "CN=Empresa",
            Issuer = "CN=Emisor",
            NotBefore = notAfter.AddYears(-1),
            NotAfter = notAfter,
            IsActive = active,
        };
        await using var scope = Worker.OpenScope();
        var db = Db(scope);
        db.Add(certificate);
        await db.SaveChangesAsync();
        return certificate;
    }

    /// <summary>Una resolución activa de factura y, si se indica, el consecutivo con su próximo número.</summary>
    public async Task<Resolution> AddResolutionAsync(Guid companyId, long from, long to, DateOnly? validTo, long? nextNumber)
    {
        var resolution = new Resolution
        {
            Id = Guid.CreateVersion7(),
            CompanyId = companyId,
            DocumentTypeId = 1,
            TypeEnvironmentId = 2,
            Prefix = "SETP",
            ResolutionNumber = "18760000001",
            ResolutionDate = new DateOnly(2025, 1, 1),
            TechnicalKey = "fc8eac422eba16e22ffd8c6f94b3f40a6e38162c",
            RangeFrom = from,
            RangeTo = to,
            ValidFrom = new DateOnly(2025, 1, 1),
            ValidTo = validTo,
            IsActive = true,
        };
        await using var scope = Worker.OpenScope();
        var db = Db(scope);
        db.Add(resolution);
        if (nextNumber is { } next)
        {
            db.Add(new NumberingSequence { CompanyId = companyId, ScopeKey = NumberingScope.ForResolution(resolution.Id), NextNumber = next });
        }

        await db.SaveChangesAsync();
        return resolution;
    }

    public Task<List<Notification>> NotificationsAsync(Guid companyId) => Worker.NotificationsAsync(companyId);

    public Task<List<OutboxMessage>> NoticeEmailsAsync(Guid companyId) =>
        Worker.MessagesAsync(companyId, Application.Abstractions.Messaging.OutboxMessageTypes.SendNoticeEmail);

    /// <summary>Guarda un aviso con la implementación real de <see cref="IScheduledNotices"/>, como lo hace un vigilante.</summary>
    public async Task<bool> TryAddNoticeAsync(ScheduledNotice notice)
    {
        await using var scope = Worker.OpenScope();
        return await scope.ServiceProvider.GetRequiredService<IScheduledNotices>().TryAddAsync(notice, CancellationToken.None);
    }

    public static ImpiFactDbContext Db(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>();

    public ValueTask DisposeAsync() => Worker.DisposeAsync();
}
