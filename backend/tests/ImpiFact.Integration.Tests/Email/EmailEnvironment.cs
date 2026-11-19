using System.Net.Http;
using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Persistence.Seeding;
using ImpiFact.Integration.Tests.DianSend;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MimeKit;

namespace ImpiFact.Integration.Tests.Email;

/// <summary>
/// PostgreSQL desechable y un capturador de correo (MailHog: un servidor SMTP de pruebas que guarda los correos y los
/// muestra por una API HTTP) en Docker, con Testcontainers, para el envío del correo del documento desde el Worker. Es
/// propio porque un <c>SetUpFixture</c> de NUnit solo cubre su espacio de nombres.
/// </summary>
[SetUpFixture]
[Category("Integration")]
public sealed class EmailEnvironment
{
    public const string MailHogImage = "mailhog/mailhog:v1.0.1";

    private const int SmtpPort = 1025;
    private const int ApiPort = 8025;

    private static TestDatabaseServer? database;
    private static IContainer? mailHog;

    public static string ConnectionString =>
        database?.GetConnectionString() ?? throw new InvalidOperationException("The PostgreSQL container is not running.");

    private static IContainer MailHog => mailHog ?? throw new InvalidOperationException("The MailHog container is not running.");

    /// <summary>Servidor y puerto SMTP del capturador, vistos desde las pruebas.</summary>
    public static (string Host, int Port) Smtp => SmtpOf(MailHog);

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        database = TestDatabase.CreateServer();
        mailHog = NewMailHog();
        await Task.WhenAll(database.StartAsync(), mailHog.StartAsync());

        await using var host = DianSendHost.Create(connectionString: ConnectionString);
        await using var scope = host.OpenScope();
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

    /// <summary>Un capturador de correo nuevo, sin arrancar, con sus puertos SMTP y HTTP en puertos libres del equipo.</summary>
    internal static IContainer NewMailHog() => new ContainerBuilder(MailHogImage)
        .WithPortBinding(SmtpPort, assignRandomHostPort: true)
        .WithPortBinding(ApiPort, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(ApiPort).ForPath("/api/v2/messages")))
        .Build();

    /// <summary>Servidor y puerto SMTP de un capturador, vistos desde las pruebas.</summary>
    internal static (string Host, int Port) SmtpOf(IContainer mailHog) => (mailHog.Hostname, mailHog.GetMappedPublicPort(SmtpPort));

    /// <summary>Los correos que recibió el capturador cuyo asunto contiene <paramref name="subjectPart"/>.</summary>
    public static Task<List<CapturedEmail>> EmailsAsync(string subjectPart) => EmailsAsync(MailHog, subjectPart);

    /// <summary>Los correos que recibió el capturador <paramref name="mailHog"/> cuyo asunto contiene <paramref name="subjectPart"/>.</summary>
    internal static async Task<List<CapturedEmail>> EmailsAsync(IContainer mailHog, string subjectPart)
    {
        using var http = new HttpClient { BaseAddress = new Uri($"http://{mailHog.Hostname}:{mailHog.GetMappedPublicPort(ApiPort)}") };
        using var json = JsonDocument.Parse(await http.GetStringAsync("/api/v2/messages?limit=250"));
        var emails = new List<CapturedEmail>();
        foreach (var item in json.RootElement.GetProperty("items").EnumerateArray())
        {
            var raw = item.GetProperty("Raw");
            var message = await MimeMessage.LoadAsync(new MemoryStream(Encoding.UTF8.GetBytes(raw.GetProperty("Data").GetString()!)));
            if (message.Subject?.Contains(subjectPart, StringComparison.Ordinal) == true)
            {
                emails.Add(new CapturedEmail(
                    raw.GetProperty("From").GetString()!,
                    raw.GetProperty("To").EnumerateArray().Select(to => to.GetString()!).ToList(),
                    message));
            }
        }

        return emails;
    }
}

/// <summary>Un correo capturado: el remitente y los destinatarios del sobre SMTP (incluidas las copias ocultas) y el mensaje.</summary>
public sealed record CapturedEmail(string EnvelopeFrom, IReadOnlyList<string> EnvelopeTo, MimeMessage Message);
