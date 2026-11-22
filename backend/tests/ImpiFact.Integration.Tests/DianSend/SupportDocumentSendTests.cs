using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Dian.Soap;
using ImpiFact.Domain.Billing;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Documents;
using ImpiFact.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.DianSend;

/// <summary>
/// The Worker sends the support document (type 11) and its adjustment note (type 13) with the existing delivery pipeline
/// (phase 7a, T4; php-rules 13.3, items 4 and 5): to the service of the environment the document was issued in (the
/// support-document family's), with the <c>dse</c> and <c>nds</c> file names and their own counters, SendTestSetAsync with
/// the test set of the route, and the acceptance counted in the support-document quota (<c>qty_docs_ds</c>).
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class SupportDocumentSendTests
{
    private static int planSequence = 1500;

    [Test]
    public async Task A_support_document_of_the_production_family_goes_to_the_production_service_with_its_dse_name()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer, documentTypeId: DocumentFamilies.SupportDocumentType, typeEnvironmentId: 1);
        host.Dian.Reply(DianReplies.SendBillSync(true, "00"));

        await host.RunOutboxAsync();

        var request = host.Dian.Requests.Single();
        var zip = (await host.FilesAsync(document.Id)).Single(file => file.Kind == DocumentFileKind.SentZip);
        Assert.Multiple(async () =>
        {
            Assert.That(request.Url, Is.EqualTo(new Uri(DianSendHost.ProductionUrl)));
            Assert.That(request.Operation, Is.EqualTo("SendBillSync"));
            Assert.That(DianSendHost.ZipEntryName(await host.ReadFileAsync(zip)),
                Is.EqualTo(DianFileNames.XmlEntry(DianFileKind.SupportDocument, issuer.Nit, 2026, 1)));
            Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Accepted));
        });
    }

    [Test]
    public async Task An_adjustment_note_of_the_test_set_is_sent_with_SendTestSetAsync_and_its_nds_name()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(
            issuer, documentTypeId: DocumentFamilies.SupportDocumentAdjustmentNoteType, testSetId: "7d1e2f3a-8b9c-4d0e-a1b2-c3d4e5f6a7b8");
        host.Dian.Reply(DianReplies.SendTestSetAsync("3f6e1d52-8a3b-4c1e-9b7a-2d4c5e6f7a8b"));

        await host.RunOutboxAsync();

        var request = host.Dian.Requests.Single();
        var zip = (await host.FilesAsync(document.Id)).Single(file => file.Kind == DocumentFileKind.SentZip);
        Assert.Multiple(async () =>
        {
            Assert.That(request.Url, Is.EqualTo(new Uri(DianSendHost.HabilitationUrl)));
            Assert.That(request.Operation, Is.EqualTo("SendTestSetAsync"));
            Assert.That(request.Parameter("testSetId"), Is.EqualTo("7d1e2f3a-8b9c-4d0e-a1b2-c3d4e5f6a7b8"));
            Assert.That(DianSendHost.ZipEntryName(await host.ReadFileAsync(zip)),
                Is.EqualTo(DianFileNames.XmlEntry(DianFileKind.SupportDocumentAdjustmentNote, issuer.Nit, 2026, 1)));
        });
    }

    // php-rules 13.1, item 7: the support document and its note consume the support-document quota, not the invoice one.
    [Test]
    public async Task An_accepted_support_document_counts_in_the_support_document_quota_and_renders_no_pdf_yet()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var companyPlanId = await AssignPlanAsync(host, issuer);
        var document = await host.CreateDocumentAsync(issuer, documentTypeId: DocumentFamilies.SupportDocumentType);
        var invoice = await host.CreateDocumentAsync(issuer);
        host.Dian.Reply(DianReplies.SendBillSync(true, "00"));
        host.Dian.Reply(DianReplies.SendBillSync(true, "00"));

        await host.RunOutboxAsync();

        await using var scope = host.OpenScope();
        var usage = await scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>().Set<PlanUsage>().AsNoTracking()
            .Where(row => row.CompanyPlanId == companyPlanId).OrderBy(row => row.DocumentKind).Select(row => new { row.DocumentKind, row.Used })
            .ToListAsync();
        var pdfs = await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.RenderPdf);
        Assert.Multiple(async () =>
        {
            Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Accepted));
            Assert.That(usage.Select(row => (row.DocumentKind, row.Used)),
                Is.EqualTo(new[] { (DocumentKind.Invoice, 1L), (DocumentKind.SupportDocument, 1L) }));
            Assert.That(pdfs.Select(message => message.Payload["document_id"]!.GetValue<Guid>()), Is.EqualTo(new[] { invoice.Id }),
                "The support document has no PDF template yet, so only the invoice gets one.");
        });
    }

    private static async Task<Guid> AssignPlanAsync(DianSendHost host, TestIssuer issuer)
    {
        var planId = Interlocked.Increment(ref planSequence);
        await using var scope = host.OpenScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>();
        db.Add(new Plan { Id = planId, Name = $"Paquete {planId}", Period = PlanPeriod.Package, IsActive = true });
        db.Add(new PlanQuota { PlanId = planId, DocumentKind = DocumentKind.Invoice, Quantity = 100 });
        db.Add(new PlanQuota { PlanId = planId, DocumentKind = DocumentKind.SupportDocument, Quantity = 100 });
        var companyPlan = new CompanyPlan { CompanyId = issuer.CompanyId, PlanId = planId, StartsAt = host.Clock.UtcNow.AddDays(-1), IsActive = true };
        db.Add(companyPlan);
        await db.SaveChangesAsync();
        return companyPlan.Id;
    }
}
