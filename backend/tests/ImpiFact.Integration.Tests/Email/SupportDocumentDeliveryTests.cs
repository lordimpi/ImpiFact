using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Dian.Soap;
using ImpiFact.Domain.Companies;
using ImpiFact.Domain.Documents;
using ImpiFact.Domain.Platform;
using ImpiFact.Infrastructure.Pdf;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Integration.Tests.DianSend;
using ImpiFact.Integration.Tests.DocumentIssue;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MimeKit;

namespace ImpiFact.Integration.Tests.Email;

/// <summary>
/// The support document (type 11) and its adjustment note (type 13) after the DIAN accepts them (phase 7a, T4b;
/// php-rules 13.3, items 6 and 7): the Worker renders their PDF (a fake Gotenberg) with their own template and emails it to
/// the seller, as the PHP does, with the subject code 05 or 95 and the <c>ad</c> name of their <c>dse</c> or <c>nds</c> file.
/// The document is stored as the issue leaves it (the golden request and the <c>SendDocumentToDian</c> message); the
/// issue itself is covered by <c>SupportDocumentIssueApiTests</c>.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class SupportDocumentDeliveryTests
{
    private const string SellerEmail = "proveedor.golden@example.com";

    [TestCase(DocumentFamilies.SupportDocumentType, "support-document/resident-seller", "DOCUMENTO SOPORTE ELECTRONICO No", "05",
        DianFileKind.SupportDocument)]
    [TestCase(DocumentFamilies.SupportDocumentAdjustmentNoteType, "sd-credit-note/adjustment-with-reference",
        "NOTA DE AJUSTE DEL DOCUMENTO SOPORTE A NO OBLIGADOS A FACTURAR No", "95", DianFileKind.SupportDocumentAdjustmentNote)]
    public async Task An_accepted_support_document_gets_its_pdf_and_is_emailed_to_the_seller(
        int documentTypeId, string goldenCase, string title, string typeCode, DianFileKind fileKind)
    {
        var gotenberg = new FakeGotenberg();
        await using var host = await StartAsync(gotenberg);
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(
            issuer,
            documentTypeId: documentTypeId,
            customer: new CustomerSnapshot { IdentificationNumber = "800000003", Name = "PROVEEDOR NO OBLIGADO GOLDEN", Email = SellerEmail });
        await AddRequestAsync(host, document, goldenCase);
        host.Dian.Reply(DianReplies.SendBillSync(true, "00"));

        // Send and accept, then RenderPdf, then SendEmail: each message is taken by the next batch.
        for (var batch = 0; batch < 4; batch++)
        {
            await host.RunOutboxAsync();
        }

        var stored = await host.DocumentAsync(document.Id);
        var pdfs = (await host.FilesAsync(document.Id)).Where(file => file.Kind == DocumentFileKind.Pdf).ToList();
        var messages = await MessagesAsync(host, issuer.CompanyId);
        var email = (await EmailEnvironment.EmailsAsync(document.FullNumber)).Single();
        var adName = "ad" + DianFileNames.XmlEntry(fileKind, issuer.Nit, 2026, 1)[DianFileNames.Prefix(fileKind).Length..^".xml".Length];
        Assert.Multiple(async () =>
        {
            Assert.That(stored.Status, Is.EqualTo(DocumentStatus.Accepted));
            Assert.That(messages.Where(message => message.Status == OutboxStatus.Failed).Select(message => (message.Type, message.LastError)),
                Is.Empty, "No outbox message of the company failed for good.");
            Assert.That(messages.Select(message => message.Type), Is.SupersetOf(new[]
            {
                OutboxMessageTypes.SendDocumentToDian, OutboxMessageTypes.RenderPdf, OutboxMessageTypes.SendEmail,
            }));
            Assert.That(messages.Where(message => message.Type is OutboxMessageTypes.RenderPdf or OutboxMessageTypes.SendEmail)
                .Select(message => message.Status), Is.All.EqualTo(OutboxStatus.Done));
            Assert.That(pdfs, Has.Count.EqualTo(1));
            Assert.That(await host.ReadFileAsync(pdfs.Single()), Is.EqualTo(FakeGotenberg.Pdf));
            Assert.That(gotenberg.ConversionsWith(title), Has.Count.EqualTo(1));
            Assert.That(gotenberg.ConversionsWith("<td>Proveedor:</td><td>PROVEEDOR NO OBLIGADO GOLDEN</td>"), Has.Count.EqualTo(1));
            Assert.That(gotenberg.ConversionsWith("CUDS: <span class=\"uuid\">" + document.Cufe + "</span>"), Has.Count.EqualTo(1));
            Assert.That(email.Message.To.Mailboxes.Select(mailbox => mailbox.Address), Is.EqualTo(new[] { SellerEmail }));
            Assert.That(email.Message.Subject,
                Is.EqualTo($"{issuer.Nit};Company {issuer.Nit};{document.FullNumber};{typeCode};Company {issuer.Nit}"));
            Assert.That(email.Message.HtmlBody, Does.Contain("Señor(es), PROVEEDOR NO OBLIGADO GOLDEN identificado con NIT 800000003"));
            Assert.That(email.Message.Attachments.OfType<MimePart>().Single().FileName, Is.EqualTo(adName + ".zip"));
            Assert.That(stored.EmailStatus, Is.EqualTo(EmailStatus.Sent));
        });
    }

    private static Task<DianSendHost> StartAsync(FakeGotenberg gotenberg)
    {
        var (smtpHost, smtpPort) = EmailEnvironment.Smtp;
        return DianSendHost.StartAsync(
            new Dictionary<string, string?>
            {
                ["Email:Host"] = smtpHost,
                ["Email:Port"] = smtpPort.ToString(CultureInfo.InvariantCulture),
                ["Email:Encryption"] = "none",
                ["Email:FromAddress"] = "no-reply@platform.test",
                ["Email:TimeoutSeconds"] = "10",
            },
            EmailEnvironment.ConnectionString,
            services => services.AddHttpClient<GotenbergClient>()
                .ConfigurePrimaryHttpMessageHandler(() => gotenberg)
                .SetHandlerLifetime(Timeout.InfiniteTimeSpan));
    }

    /// <summary>The golden request with <c>sendmail</c>, stored as the issue stores it.</summary>
    private static async Task AddRequestAsync(DianSendHost host, Document document, string goldenCase)
    {
        var folder = Path.Combine(GoldenIssueTests.GoldenRoot(), goldenCase.Replace('/', Path.DirectorySeparatorChar));
        var request = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "input.json"), Encoding.UTF8))!.AsObject();
        request["sendmail"] = true;
        await using var scope = host.OpenScope();
        await scope.ServiceProvider.GetRequiredService<IDocumentFiles>().AddAsync(
            new NewDocumentFile(document.CompanyId, document.Id, DocumentFileKind.RequestJson, Encoding.UTF8.GetBytes(request.ToJsonString()),
                "application/json"),
            CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>().SaveChangesAsync();
    }

    private static async Task<List<OutboxMessage>> MessagesAsync(DianSendHost host, Guid companyId)
    {
        await using var scope = host.OpenScope();
        return await scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>().Set<OutboxMessage>().AsNoTracking()
            .Where(message => message.CompanyId == companyId)
            .ToListAsync();
    }
}
