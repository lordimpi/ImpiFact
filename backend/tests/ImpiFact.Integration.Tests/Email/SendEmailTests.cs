using System.Globalization;
using System.IO.Compression;
using System.Text;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Application.Abstractions.Protection;
using ImpiFact.Application.Features.Documents.Delivery;
using ImpiFact.Application.Features.Documents.Email;
using ImpiFact.Dian.Soap;
using ImpiFact.Domain.Companies;
using ImpiFact.Domain.Documents;
using ImpiFact.Domain.Platform;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Integration.Tests.DianSend;
using Microsoft.Extensions.DependencyInjection;
using MimeKit;

namespace ImpiFact.Integration.Tests.Email;

/// <summary>
/// El Worker envía el correo del documento aceptado (<c>SendEmail</c>; Arquitectura detallada, sección 7, paso 9;
/// supuesto C6 de la fase 4) por SMTP real contra un capturador de correo: destinatarios, asunto y ZIP como ApiDian,
/// reintento mientras falta el PDF, sin envíos repetidos, servidor de la empresa o de la plataforma y sin la contraseña
/// en los logs.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class SendEmailTests
{
    private const string BuyerEmail = "buyer@example.com";
    private const string PlatformFrom = "no-reply@platform.test";
    private const string SmtpPassword = "S3cret-Pa55word!";

    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7 test");
    private static readonly byte[] AttachedXml = Encoding.UTF8.GetBytes("<AttachedDocument>signed</AttachedDocument>");

    [Test]
    public async Task An_accepted_document_is_emailed_once_with_its_recipients_subject_and_zip()
    {
        await using var host = await StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await AcceptedDocumentAsync(host, issuer, """
            { "sendmail": true, "sendmailtome": true, "email_cc_list": [ { "email": "cc@example.com" } ] }
            """);

        await host.RunOutboxAsync();

        var email = (await EmailEnvironment.EmailsAsync(document.FullNumber)).Single();
        var stored = await host.DocumentAsync(document.Id);
        var adName = "ad" + DianFileNames.XmlEntry(DianFileKind.Invoice, issuer.Nit, 2026, 1)[2..^".xml".Length];
        var attachment = email.Message.Attachments.OfType<MimePart>().Single();
        Assert.Multiple(async () =>
        {
            Assert.That(email.EnvelopeFrom, Is.EqualTo(PlatformFrom));
            Assert.That(email.EnvelopeTo, Is.EquivalentTo(new[] { BuyerEmail, $"company{issuer.Nit}@example.com", "cc@example.com" }));
            Assert.That(email.Message.To.Mailboxes.Select(mailbox => mailbox.Address), Is.EqualTo(new[] { BuyerEmail }));
            Assert.That(email.Message.Cc, Is.Empty);
            Assert.That(email.Message.Bcc, Is.Empty, "Hidden copies never travel in the headers.");
            Assert.That(email.Message.Subject, Is.EqualTo($"{issuer.Nit};Company {issuer.Nit};{document.FullNumber};01;Company {issuer.Nit}"));
            Assert.That(email.Message.HtmlBody, Does.Contain("Señor(es), Cliente SAS identificado con NIT 900111222"));
            Assert.That(email.Message.HtmlBody, Does.Contain("119.00"));
            Assert.That(email.Message.HtmlBody, Does.Contain("https://catalogo-vpfe-hab.dian.test/document/searchqr?documentkey=" + document.Cufe));
            Assert.That(email.Message.TextBody, Does.Contain(document.FullNumber));
            Assert.That(attachment.FileName, Is.EqualTo(adName + ".zip"));
            Assert.That(Entries(attachment), Is.EqualTo(new Dictionary<string, byte[]> { [adName + ".xml"] = AttachedXml, [adName + ".pdf"] = Pdf }));
            Assert.That(stored.EmailStatus, Is.EqualTo(EmailStatus.Sent));
            Assert.That(stored.EmailSentAt, Is.EqualTo(host.Clock.UtcNow));
            Assert.That((await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.SendEmail)).Single().Status, Is.EqualTo(OutboxStatus.Done));
        });
    }

    [Test]
    public async Task A_repeated_message_does_not_send_the_email_twice()
    {
        await using var host = await StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await AcceptedDocumentAsync(host, issuer, """{ "sendmail": true }""");
        await host.RunOutboxAsync();
        await EnqueueEmailAsync(host, document);

        await host.RunOutboxAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(await EmailEnvironment.EmailsAsync(document.FullNumber), Has.Count.EqualTo(1));
            Assert.That((await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.SendEmail)).Select(message => message.Status),
                Is.EqualTo(new[] { OutboxStatus.Done, OutboxStatus.Done }));
        });
    }

    [Test]
    public async Task Without_the_pdf_the_message_is_retried_until_the_pdf_is_stored()
    {
        await using var host = await StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await AcceptedDocumentAsync(host, issuer, """{ "sendmail": true }""", withPdf: false);

        await host.RunOutboxAsync();

        var waiting = (await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.SendEmail)).Single();
        Assert.Multiple(async () =>
        {
            Assert.That(waiting.Status, Is.EqualTo(OutboxStatus.Pending));
            Assert.That(waiting.Attempts, Is.EqualTo(1));
            Assert.That(waiting.NextAttemptAt, Is.EqualTo(host.Clock.UtcNow.AddSeconds(10)));
            Assert.That(waiting.LastError, Does.Contain(nameof(EmailAttachmentsNotReadyException)));
            Assert.That(await EmailEnvironment.EmailsAsync(document.FullNumber), Is.Empty);
            Assert.That((await host.DocumentAsync(document.Id)).EmailStatus, Is.EqualTo(EmailStatus.NotSent));
        });

        await AddFileAsync(host, document, DocumentFileKind.Pdf, Pdf, "application/pdf");
        host.Clock.Advance(TimeSpan.FromSeconds(10));
        await host.RunOutboxAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(await EmailEnvironment.EmailsAsync(document.FullNumber), Has.Count.EqualTo(1));
            Assert.That((await host.DocumentAsync(document.Id)).EmailStatus, Is.EqualTo(EmailStatus.Sent));
        });
    }

    [Test]
    public async Task Without_the_attached_document_the_message_is_retried_and_finally_marks_the_email_failed()
    {
        await using var host = await StartAsync(new Dictionary<string, string?> { ["Outbox:MaxAttempts"] = "1" });
        var issuer = await host.CreateIssuerAsync();
        var document = await AcceptedDocumentAsync(host, issuer, """{ "sendmail": true }""", withAttachedDocument: false);

        await host.RunOutboxAsync();

        Assert.Multiple(async () =>
        {
            Assert.That((await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.SendEmail)).Single().Status, Is.EqualTo(OutboxStatus.Failed));
            Assert.That((await host.DocumentAsync(document.Id)).EmailStatus, Is.EqualTo(EmailStatus.Failed));
            Assert.That(await EmailEnvironment.EmailsAsync(document.FullNumber), Is.Empty);
        });
    }

    [TestCase("""{ "sendmail": false, "sendmailtome": true }""", "900111222")]
    [TestCase("""{ "number": 1 }""", "900111222")]
    [TestCase("""{ "sendmail": true, "sendmailtome": true }""", "222222222222")]
    public async Task No_email_is_sent_when_it_was_not_asked_for_or_the_buyer_is_the_final_consumer(string request, string buyer)
    {
        await using var host = await StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await AcceptedDocumentAsync(host, issuer, request, buyer: buyer);

        await host.RunOutboxAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(await EmailEnvironment.EmailsAsync(document.FullNumber), Is.Empty);
            Assert.That((await host.DocumentAsync(document.Id)).EmailStatus, Is.EqualTo(EmailStatus.NotSent));
            Assert.That((await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.SendEmail)).Single().Status, Is.EqualTo(OutboxStatus.Done));
        });
    }

    [Test]
    public async Task The_final_consumer_gets_the_email_at_email_pos_customer()
    {
        await using var host = await StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await AcceptedDocumentAsync(host, issuer, """{ "sendmail": true, "email_pos_customer": "pos@example.com" }""",
            buyer: "222222222222");

        await host.RunOutboxAsync();

        Assert.That((await EmailEnvironment.EmailsAsync(document.FullNumber)).Single().EnvelopeTo, Is.EqualTo(new[] { "pos@example.com" }));
    }

    [Test]
    public async Task A_company_with_its_own_server_sends_through_it_and_its_password_never_reaches_the_logs()
    {
        await using var host = await StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var (smtpHost, smtpPort) = EmailEnvironment.Smtp;
        await SetCompanyMailAsync(host, issuer, smtpHost, smtpPort, $"billing@company{issuer.Nit}.test", usePlatformDefault: false);
        var document = await AcceptedDocumentAsync(host, issuer, """{ "sendmail": true }""");

        await host.RunOutboxAsync();

        var email = (await EmailEnvironment.EmailsAsync(document.FullNumber)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(email.EnvelopeFrom, Is.EqualTo($"billing@company{issuer.Nit}.test"));
            Assert.That(email.Message.From.Mailboxes.Single().Name, Is.EqualTo("Company billing"));
            Assert.That(host.Logs.Entries, Has.None.Contains(SmtpPassword));
        });
    }

    [Test]
    public async Task A_company_that_chose_the_platform_server_sends_through_the_platform()
    {
        await using var host = await StartAsync();
        var issuer = await host.CreateIssuerAsync();
        await SetCompanyMailAsync(host, issuer, "smtp.unreachable.test", 25, $"billing@company{issuer.Nit}.test", usePlatformDefault: true);
        var document = await AcceptedDocumentAsync(host, issuer, """{ "sendmail": true }""");

        await host.RunOutboxAsync();

        var email = (await EmailEnvironment.EmailsAsync(document.FullNumber)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(email.EnvelopeFrom, Is.EqualTo(PlatformFrom));
            Assert.That(email.Message.From.Mailboxes.Single().Name, Is.EqualTo("ImpiFact Pruebas"));
        });
    }

    [Test]
    public async Task A_failing_company_server_fails_the_email_without_its_password_or_addresses_in_errors_logs_or_notifications()
    {
        await using var host = await StartAsync(new Dictionary<string, string?> { ["Outbox:MaxAttempts"] = "1" });
        var issuer = await host.CreateIssuerAsync();
        await SetCompanyMailAsync(host, issuer, "127.0.0.1", ClosedPort(), $"billing@company{issuer.Nit}.test", usePlatformDefault: false);
        var document = await AcceptedDocumentAsync(host, issuer, """{ "sendmail": true }""");

        await host.RunOutboxAsync();

        var message = (await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.SendEmail)).Single();
        var notifications = await host.NotificationsAsync(issuer.CompanyId);
        Assert.Multiple(async () =>
        {
            Assert.That(message.Status, Is.EqualTo(OutboxStatus.Failed));
            Assert.That(message.LastError, Does.Contain("EmailDeliveryException"));
            Assert.That(message.LastError, Does.Not.Contain(SmtpPassword).And.Not.Contain(BuyerEmail));
            Assert.That((await host.DocumentAsync(document.Id)).EmailStatus, Is.EqualTo(EmailStatus.Failed));
            Assert.That(notifications, Is.Not.Empty);
            Assert.That(notifications.Select(notification => notification.Title + " " + notification.Body), Has.None.Contains(SmtpPassword));
            Assert.That(host.Logs.Entries, Has.None.Contains(SmtpPassword));
            Assert.That(host.Logs.Entries, Has.None.Contains(BuyerEmail));
            Assert.That(await EmailEnvironment.EmailsAsync(document.FullNumber), Is.Empty);
        });
    }

    private static Task<DianSendHost> StartAsync(Dictionary<string, string?>? settings = null)
    {
        var (smtpHost, smtpPort) = EmailEnvironment.Smtp;
        var values = new Dictionary<string, string?>
        {
            ["Email:Host"] = smtpHost,
            ["Email:Port"] = smtpPort.ToString(CultureInfo.InvariantCulture),
            ["Email:Encryption"] = "none",
            ["Email:FromAddress"] = PlatformFrom,
            ["Email:FromName"] = "ImpiFact Pruebas",
            ["Email:TimeoutSeconds"] = "10",
        };
        foreach (var (key, value) in settings ?? [])
        {
            values[key] = value;
        }

        return DianSendHost.StartAsync(values, EmailEnvironment.ConnectionString);
    }

    /// <summary>
    /// Un documento aceptado con la solicitud guardada, el ZIP enviado a la DIAN, el AttachedDocument y el PDF (si se
    /// piden), y el mensaje <c>SendEmail</c> que deja el handler del PDF.
    /// </summary>
    private static async Task<Document> AcceptedDocumentAsync(
        DianSendHost host, TestIssuer issuer, string request, bool withPdf = true, bool withAttachedDocument = true, string buyer = "900111222")
    {
        var document = await host.CreateDocumentAsync(
            issuer,
            status: DocumentStatus.Accepted,
            customer: new CustomerSnapshot { IdentificationNumber = buyer, Name = "Cliente SAS", Email = BuyerEmail });
        await AddFileAsync(host, document, DocumentFileKind.RequestJson, Encoding.UTF8.GetBytes(request), "application/json");
        await AddFileAsync(host, document, DocumentFileKind.SentZip, Zip(DianFileNames.XmlEntry(DianFileKind.Invoice, issuer.Nit, 2026, 1)), "application/zip");
        if (withAttachedDocument)
        {
            await AddFileAsync(host, document, DocumentFileKind.AttachedDocument, AttachedXml, "application/xml");
        }

        if (withPdf)
        {
            await AddFileAsync(host, document, DocumentFileKind.Pdf, Pdf, "application/pdf");
        }

        await EnqueueEmailAsync(host, document);
        return document;
    }

    private static async Task AddFileAsync(DianSendHost host, Document document, DocumentFileKind kind, byte[] content, string contentType)
    {
        await using var scope = host.OpenScope();
        await scope.ServiceProvider.GetRequiredService<IDocumentFiles>().AddAsync(
            new NewDocumentFile(document.CompanyId, document.Id, kind, content, contentType), CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>().SaveChangesAsync();
    }

    private static async Task EnqueueEmailAsync(DianSendHost host, Document document)
    {
        await using var scope = host.OpenScope();
        await scope.ServiceProvider.GetRequiredService<IOutbox>().EnqueueAsync(
            OutboxMessageTypes.SendEmail, new SendEmailPayload(document.Id), document.CompanyId, null, CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>().SaveChangesAsync();
    }

    private static async Task SetCompanyMailAsync(DianSendHost host, TestIssuer issuer, string smtpHost, int smtpPort, string from, bool usePlatformDefault)
    {
        await using var scope = host.OpenScope();
        var cipher = scope.ServiceProvider.GetRequiredService<ISecretProtector>().Protect(SecretPurposes.SmtpPassword, SmtpPassword);
        var db = scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>();
        db.Add(new CompanyMailSettings
        {
            CompanyId = issuer.CompanyId,
            UsePlatformDefault = usePlatformDefault,
            Host = smtpHost,
            Port = smtpPort,
            Username = "mailer",
            PasswordCipher = cipher,
            Encryption = "none",
            FromAddress = from,
            FromName = "Company billing",
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Un puerto local donde no escucha nadie: la conexión se rechaza de inmediato.</summary>
    private static int ClosedPort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static byte[] Zip(string entryName)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var stream = archive.CreateEntry(entryName).Open();
            stream.Write("<Invoice/>"u8);
        }

        return buffer.ToArray();
    }

    private static Dictionary<string, byte[]> Entries(MimePart attachment)
    {
        using var content = new MemoryStream();
        attachment.Content!.DecodeTo(content);
        content.Position = 0;
        using var archive = new ZipArchive(content, ZipArchiveMode.Read);
        return archive.Entries.ToDictionary(entry => entry.FullName, entry =>
        {
            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        });
    }
}
