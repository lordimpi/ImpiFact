using System.Text.Json;
using System.Text.Json.Nodes;
using ImpiFact.Application.Abstractions.Email;
using ImpiFact.Application.Abstractions.Maintenance;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Application.Features.Maintenance;
using Microsoft.Extensions.Logging.Abstractions;

namespace ImpiFact.Application.Tests.Features.Maintenance;

/// <summary>
/// Correo de un aviso de las tareas programadas (mensaje <c>SendNoticeEmail</c>): sale con el servidor de correo de la
/// plataforma hacia el correo de la empresa, con el título y el texto del aviso.
/// </summary>
[TestFixture]
public sealed class SendNoticeEmailHandlerTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private static readonly MailServer Platform = new("smtp.platform.test", 25, null, null, MailEncryption.None, "no-reply@platform.test", "ImpiFact");

    private readonly Guid companyId = Guid.CreateVersion7();
    private readonly Guid notificationId = Guid.CreateVersion7();
    private RecordingSender sender = null!;
    private FakeNoticeReads reads = null!;

    [SetUp]
    public void SetUp()
    {
        sender = new RecordingSender();
        reads = new FakeNoticeReads();
    }

    [Test]
    public async Task The_notice_is_sent_to_the_company_email_through_the_platform_server()
    {
        reads.Notices[(companyId, notificationId)] = new CompanyNotice(
            "Empresa <Uno> SAS", "empresa@example.com", "El certificado de firma vence en 30 días", "Suba el certificado nuevo & listo.");

        await Handler().HandleAsync(Message(), CancellationToken.None);

        var (server, email) = sender.Sent.Single();
        Assert.Multiple(() =>
        {
            Assert.That(server, Is.SameAs(Platform));
            Assert.That(email.To, Is.EqualTo(new[] { "empresa@example.com" }));
            Assert.That(email.Bcc, Is.Empty);
            Assert.That(email.Attachments, Is.Empty);
            Assert.That(email.Subject, Is.EqualTo("Empresa <Uno> SAS: El certificado de firma vence en 30 días"));
            Assert.That(email.TextBody, Is.EqualTo(
                "Hola, Empresa <Uno> SAS:\n\nSuba el certificado nuevo & listo.\n\nEste aviso también está en las notificaciones del panel de ImpiFact."));
            Assert.That(email.HtmlBody, Does.Contain("Hola, Empresa &lt;Uno&gt; SAS:"));
            Assert.That(email.HtmlBody, Does.Contain("Suba el certificado nuevo &amp; listo."));
        });
    }

    [Test]
    public async Task A_company_without_email_gets_no_email()
    {
        reads.Notices[(companyId, notificationId)] = new CompanyNotice("Empresa", " ", "Título", "Texto");

        await Handler().HandleAsync(Message(), CancellationToken.None);

        Assert.That(sender.Sent, Is.Empty);
    }

    [Test]
    public void A_missing_notice_fails_without_retries()
    {
        Assert.ThrowsAsync<PermanentOutboxFailureException>(() => Handler().HandleAsync(Message(), CancellationToken.None));
        Assert.That(sender.Sent, Is.Empty);
    }

    [Test]
    public void The_handler_takes_the_send_notice_email_messages()
    {
        Assert.That(Handler().MessageType, Is.EqualTo(OutboxMessageTypes.SendNoticeEmail));
    }

    private SendNoticeEmailHandler Handler() =>
        new(reads, new FixedPlatformServer(), sender, NullLogger<SendNoticeEmailHandler>.Instance);

    private OutboxMessageContext Message() => new(
        Guid.CreateVersion7(),
        companyId,
        OutboxMessageTypes.SendNoticeEmail,
        JsonSerializer.SerializeToNode(new SendNoticeEmailPayload(notificationId), Json) ?? new JsonObject(),
        1,
        Json);

    private sealed class FixedPlatformServer : IPlatformMailServer
    {
        public MailServer Default => Platform;
    }

    private sealed class RecordingSender : IEmailSender
    {
        public List<(MailServer Server, EmailMessage Message)> Sent { get; } = [];

        public Task SendAsync(MailServer server, EmailMessage message, CancellationToken ct)
        {
            Sent.Add((server, message));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeNoticeReads : ICompanyNoticeReads
    {
        public Dictionary<(Guid, Guid), CompanyNotice> Notices { get; } = [];

        public Task<CompanyNotice?> FindAsync(Guid companyId, Guid notificationId, CancellationToken ct) =>
            Task.FromResult(Notices.GetValueOrDefault((companyId, notificationId)));
    }
}
