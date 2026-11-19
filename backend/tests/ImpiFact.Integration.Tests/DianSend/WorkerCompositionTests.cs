using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Maintenance;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Infrastructure.Options;
using ImpiFact.Infrastructure.Platform.Outbox;
using ImpiFact.Infrastructure.Platform.Scheduling;
using ImpiFact.Infrastructure.Postgres;
using ImpiFact.Infrastructure.Tenancy;
using ImpiFact.Integration.Tests.Protection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ImpiFact.Integration.Tests.DianSend;

/// <summary>
/// La composición del Worker (<see cref="OutboxServiceCollectionExtensions.AddWorkerServices"/>, lo único que llama
/// <c>ImpiFact.Worker</c>). El Worker no revisa al construirse que cada servicio se pueda crear
/// (<c>ValidateOnBuild = false</c>), así que esta prueba crea cada handler de la bandeja de salida con sus dependencias.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class WorkerCompositionTests
{
    [Test]
    public async Task Every_registered_outbox_handler_can_be_created_with_the_worker_composition()
    {
        using var protection = new ProtectionScratch();
        await using var services = Build(protection, new Dictionary<string, string?>());
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(Guid.CreateVersion7(), userId: null, ActorType.System);

        var handlers = scope.ServiceProvider.GetServices<IOutboxMessageHandler>().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(handlers.Select(handler => handler.MessageType),
                Is.EquivalentTo(new[]
                {
                    OutboxMessageTypes.SendDocumentToDian, OutboxMessageTypes.PollDianStatus, OutboxMessageTypes.RenderPdf,
                    OutboxMessageTypes.SendEmail, OutboxMessageTypes.SendNoticeEmail,
                    OutboxMessageTypes.RegeneratePdf, OutboxMessageTypes.RegenerateAttachedDocument,
                    OutboxMessageTypes.SendPasswordResetEmail,
                }));
            Assert.That(handlers.Select(handler => handler.MessageType), Is.Unique);
        });
    }

    [Test]
    public async Task Every_daily_job_and_its_scheduler_can_be_created_with_the_worker_composition()
    {
        using var protection = new ProtectionScratch();
        await using var services = Build(protection, new Dictionary<string, string?>());
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(companyId: null, userId: null, ActorType.System);

        var jobs = scope.ServiceProvider.GetServices<IScheduledJob>().Select(job => job.Name).ToList();
        var hosted = services.GetServices<IHostedService>().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(jobs, Is.EquivalentTo(new[] { "Housekeeping", "CertificateExpiryWatcher", "ResolutionRangeWatcher" }));
            Assert.That(hosted.OfType<ScheduledJobsService>().Count(), Is.EqualTo(1));
            Assert.That(hosted.OfType<OutboxProcessor>().Count(), Is.EqualTo(1));
            Assert.That(scope.ServiceProvider.GetRequiredService<IScheduledNotices>(), Is.Not.Null);
            Assert.That(scope.ServiceProvider.GetRequiredService<ICompanyNoticeReads>(), Is.Not.Null);
        });
    }

    [Test]
    public async Task The_worker_runs_the_daily_jobs_once_a_day_with_the_documented_defaults()
    {
        using var protection = new ProtectionScratch();
        await using var services = Build(protection, new Dictionary<string, string?>());

        var options = services.GetRequiredService<IOptions<ScheduledJobsOptions>>().Value;

        Assert.Multiple(() =>
        {
            Assert.That(options.RunAtUtc, Is.EqualTo(new TimeOnly(8, 0)));
            Assert.That(options.RunOnStartup, Is.True);
            Assert.That(options.OutboxRetentionDays, Is.EqualTo(30));
            Assert.That(options.DeleteBatchSize, Is.EqualTo(1000));
        });
    }

    [TestCase(nameof(ScheduledJobsOptions.OutboxRetentionDays), "0")]
    [TestCase(nameof(ScheduledJobsOptions.DeleteBatchSize), "0")]
    [TestCase(nameof(ScheduledJobsOptions.DeleteBatchSize), "100001")]
    [TestCase(nameof(ScheduledJobsOptions.RunAtUtc), "08:00:30")]
    public async Task An_invalid_daily_jobs_setting_stops_the_worker(string property, string value)
    {
        using var protection = new ProtectionScratch();
        await using var services = Build(protection, new Dictionary<string, string?> { [$"ScheduledJobs:{property}"] = value });

        var exception = Assert.Throws<OptionsValidationException>(() => _ = services.GetRequiredService<IOptions<ScheduledJobsOptions>>().Value);

        Assert.That(exception?.Message, Does.Contain(property));
    }

    [Test]
    public async Task The_worker_accepts_the_default_resilience_budget()
    {
        using var protection = new ProtectionScratch();
        await using var services = Build(protection, new Dictionary<string, string?>());

        var resilience = services.GetRequiredService<IOptions<ResilienceOptions>>().Value;

        Assert.Multiple(() =>
        {
            Assert.That(resilience.Retries, Is.EqualTo(3));
            Assert.That(resilience.BreakerFailures, Is.EqualTo(5));
            Assert.That(resilience.BreakerSeconds, Is.EqualTo(30));
            Assert.That(resilience.TotalSeconds, Is.EqualTo(120));
        });
    }

    [Test]
    public async Task Two_dian_calls_that_do_not_fit_in_the_handler_timeout_stop_the_worker()
    {
        using var protection = new ProtectionScratch();
        await using var services = Build(protection, new Dictionary<string, string?> { ["Resilience:TotalSeconds"] = "135" });

        var exception = Assert.Throws<OptionsValidationException>(() => _ = services.GetRequiredService<IOptions<ResilienceOptions>>().Value);

        Assert.That(exception?.Message, Does.Contain("HandlerTimeoutSeconds"));
    }

    [Test]
    public async Task The_worker_does_not_start_without_the_platform_mail_server()
    {
        using var protection = new ProtectionScratch();
        await using var services = Build(protection, new Dictionary<string, string?>());

        var exception = Assert.Throws<OptionsValidationException>(() => _ = services.GetRequiredService<IOptions<EmailOptions>>().Value);

        Assert.Multiple(() =>
        {
            Assert.That(exception?.Message, Does.Contain(nameof(EmailOptions.Host)));
            Assert.That(exception?.Message, Does.Contain(nameof(EmailOptions.FromAddress)));
        });
    }

    [TestCase(null, "The BaseUrl field is required")]
    [TestCase("panel.impifact.test", "absolute https URL")]
    [TestCase("http://panel.impifact.test", "absolute https URL")]
    [TestCase("https://panel.impifact.test/?x=1", "absolute https URL")]
    public async Task The_worker_does_not_start_without_a_valid_panel_address(string? baseUrl, string expected)
    {
        using var protection = new ProtectionScratch();
        await using var services = Build(protection, new Dictionary<string, string?> { ["Panel:BaseUrl"] = baseUrl });

        var exception = Assert.Throws<OptionsValidationException>(() => _ = services.GetRequiredService<IOptions<PanelOptions>>().Value);

        Assert.That(exception?.Message, Does.Contain(expected));
    }

    [TestCase("https://panel.impifact.test", "https://panel.impifact.test/restablecer?token=a%2Bb%2Fc")]
    [TestCase("https://impifact.test/panel/", "https://impifact.test/panel/restablecer?token=a%2Bb%2Fc")]
    [TestCase("http://localhost:4200", "http://localhost:4200/restablecer?token=a%2Bb%2Fc")]
    [TestCase("http://127.0.0.1:4200", "http://127.0.0.1:4200/restablecer?token=a%2Bb%2Fc")]
    public async Task The_password_reset_link_points_to_the_panel_page_with_the_escaped_token(string baseUrl, string expected)
    {
        using var protection = new ProtectionScratch();
        await using var services = Build(protection, new Dictionary<string, string?> { ["Panel:BaseUrl"] = baseUrl });

        var link = services.GetRequiredService<ImpiFact.Application.Abstractions.Identity.IPanelLinks>().PasswordReset("a+b/c");

        Assert.That(link.AbsoluteUri, Is.EqualTo(expected));
    }

    [Test]
    public async Task A_platform_mail_user_without_its_password_stops_the_worker()
    {
        using var protection = new ProtectionScratch();
        await using var services = Build(protection, new Dictionary<string, string?>
        {
            ["Email:Host"] = "smtp.platform.test",
            ["Email:FromAddress"] = "no-reply@platform.test",
            ["Email:Username"] = "mailer",
        });

        var exception = Assert.Throws<OptionsValidationException>(() => _ = services.GetRequiredService<IOptions<EmailOptions>>().Value);

        Assert.That(exception?.Message, Does.Contain("set both or neither"));
    }

    [Test]
    public async Task The_worker_takes_the_platform_mail_server_from_its_settings()
    {
        using var protection = new ProtectionScratch();
        await using var services = Build(protection, new Dictionary<string, string?>
        {
            ["Email:Host"] = "smtp.platform.test",
            ["Email:FromAddress"] = "no-reply@platform.test",
        });

        var email = services.GetRequiredService<IOptions<EmailOptions>>().Value;

        Assert.Multiple(() =>
        {
            Assert.That(email.Port, Is.EqualTo(587));
            Assert.That(email.Encryption, Is.EqualTo(EmailOptions.StartTls));
            Assert.That(email.FromName, Is.EqualTo("ImpiFact"));
            Assert.That(email.TimeoutSeconds, Is.EqualTo(60));
        });
    }

    private static ServiceProvider Build(ProtectionScratch protection, Dictionary<string, string?> settings)
    {
        var values = new Dictionary<string, string?>(protection.LocalAesGcmSettings())
        {
            ["Database:Provider"] = "Postgres",
            ["Database:ConnectionString"] = DianSendDatabase.ConnectionString,
            ["Storage:Local:Root"] = Path.Combine(protection.Root, "storage"),
        };
        foreach (var (key, value) in settings)
        {
            values[key] = value;
        }

        // Los valores no secretos del appsettings.json del Worker; los secretos no hacen falta para crear los handlers.
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(WorkerSettingsFile(), optional: false)
            .AddInMemoryCollection(values)
            .Build();
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddWorkerServices(configuration, new PostgresDatabaseProvider());
        return collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static string WorkerSettingsFile()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ImpiFact.slnx")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(
            directory?.FullName ?? throw new InvalidOperationException("The backend folder was not found."),
            "src", "ImpiFact.Worker", "appsettings.json");
    }
}
