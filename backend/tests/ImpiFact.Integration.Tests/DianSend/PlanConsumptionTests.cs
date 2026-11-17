using ImpiFact.Application.Abstractions.Billing;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Persistence;
using ImpiFact.Application.Features.Documents.Delivery;
using ImpiFact.Domain.Billing;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Documents;
using ImpiFact.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.DianSend;

/// <summary>
/// Consumo del plan (Modelo de datos detallado, sección 3.6: «<c>plan_usage</c> se incrementa al aceptar un
/// documento»): el Worker suma un documento al plan vigente de la empresa cuando la DIAN acepta, en el mismo guardado
/// que la aceptación, una sola vez por documento y sin perder cuentas cuando varios procesos aceptan a la vez.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class PlanConsumptionTests
{
    private static int planSequence = 500;

    [Test]
    public async Task An_accepted_invoice_counts_one_document_in_the_company_plan()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var companyPlanId = await AssignPlanAsync(host, issuer);
        var document = await host.CreateDocumentAsync(issuer);
        host.Dian.Reply(DianReplies.SendBillSync(true, "00"));

        await host.RunOutboxAsync();

        var usage = await UsageAsync(host, companyPlanId);
        Assert.Multiple(async () =>
        {
            Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Accepted));
            Assert.That(usage.Select(row => (row.DocumentKind, row.Used, row.RowVersion)), Is.EqualTo(new[] { (DocumentKind.Invoice, 1L, 1L) }));
        });
    }

    [Test]
    public async Task A_note_accepted_with_warnings_by_reconciliation_after_rule_90_counts_once_in_the_invoice_family()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var companyPlanId = await AssignPlanAsync(host, issuer);
        var document = await host.CreateDocumentAsync(issuer, documentTypeId: 4);
        host.Dian.Reply(DianReplies.SendBillSync(false, "99", DianReplies.Rule90));
        host.Dian.Reply(DianReplies.GetStatus(true, "00", DianReplies.Notification));

        await host.RunOutboxAsync();

        Assert.Multiple(async () =>
        {
            Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.AcceptedWithWarnings));
            Assert.That((await UsageAsync(host, companyPlanId)).Select(row => (row.DocumentKind, row.Used)),
                Is.EqualTo(new[] { (DocumentKind.Invoice, 1L) }));
        });
    }

    [Test]
    public async Task Repeated_deliveries_and_polls_after_the_acceptance_do_not_count_it_again()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var companyPlanId = await AssignPlanAsync(host, issuer);
        var document = await host.CreateDocumentAsync(issuer, status: DocumentStatus.Unconfirmed, sentAt: host.Clock.UtcNow);
        await host.EnqueuePollAsync(document, DianStatusQuery.GetStatus);
        host.Dian.Reply(DianReplies.GetStatus(true, "00"));
        await host.RunOutboxAsync();

        await host.EnqueuePollAsync(document, DianStatusQuery.GetStatus);
        await host.EnqueueSendAsync(document);
        await host.RunOutboxAsync();

        Assert.Multiple(async () =>
        {
            Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Accepted));
            Assert.That(host.Dian.Requests, Has.Count.EqualTo(1), "The repeated messages do not call the DIAN.");
            Assert.That((await UsageAsync(host, companyPlanId)).Single().Used, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task A_rejected_document_counts_nothing()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var companyPlanId = await AssignPlanAsync(host, issuer);
        var document = await host.CreateDocumentAsync(issuer);
        host.Dian.Reply(DianReplies.SendBillSync(false, "99", DianReplies.Rejection));

        await host.RunOutboxAsync();

        Assert.Multiple(async () =>
        {
            Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Rejected));
            Assert.That(await UsageAsync(host, companyPlanId), Is.Empty);
        });
    }

    // Como el PHP, que deja en estado 0 los documentos del set de pruebas y por eso nunca los cuenta: el set de
    // habilitación no gasta documentos del plan.
    [Test]
    public async Task A_test_set_document_accepted_through_GetStatusZip_counts_nothing()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var companyPlanId = await AssignPlanAsync(host, issuer);
        var document = await host.CreateDocumentAsync(issuer, testSetId: "c3a1f0e2-test-set");
        host.Dian.Reply(DianReplies.SendTestSetAsync("zip-key-1"));
        await host.RunOutboxAsync();

        host.Clock.Advance(TimeSpan.FromMinutes(1));
        host.Dian.Reply(DianReplies.GetStatusZip(true, "00"));
        await host.RunOutboxAsync();

        Assert.Multiple(async () =>
        {
            Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Accepted));
            Assert.That(await UsageAsync(host, companyPlanId), Is.Empty);
        });
    }

    [Test]
    public async Task Without_a_plan_the_acceptance_counts_nothing_and_still_succeeds()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer);
        host.Dian.Reply(DianReplies.SendBillSync(true, "00"));

        await host.RunOutboxAsync();

        await using var scope = host.OpenScope();
        var companyPlans = await Db(scope).Set<CompanyPlan>().IgnoreQueryFilters([QueryFilters.Tenant])
            .CountAsync(row => row.CompanyId == issuer.CompanyId);
        Assert.Multiple(async () =>
        {
            Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Accepted));
            Assert.That(companyPlans, Is.Zero);
        });
    }

    // Los mensajes de una tanda se procesan a la vez, cada uno con su propio ámbito y su propia conexión, como varias
    // instancias del Worker: todas suman sobre la misma fila de plan_usage.
    [Test]
    public async Task Concurrent_acceptances_of_the_same_company_never_lose_a_count()
    {
        const int concurrent = 8;
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var companyPlanId = await AssignPlanAsync(host, issuer);
        host.Dian.Otherwise = () => FakeDian.Response(DianReplies.SendBillSync(true, "00"));
        // El primer envío crea el consecutivo de nombres de archivo del año; si varios lo crearan a la vez, todos menos
        // uno chocarían con su índice único y se reintentarían más tarde.
        var first = await host.CreateDocumentAsync(issuer);
        await host.RunOutboxAsync();
        var documents = new List<Document>();
        for (var index = 0; index < concurrent; index++)
        {
            documents.Add(await host.CreateDocumentAsync(issuer));
        }

        await host.RunOutboxAsync();

        var statuses = new List<DocumentStatus>();
        foreach (var document in documents.Prepend(first))
        {
            statuses.Add((await host.DocumentAsync(document.Id)).Status);
        }

        Assert.Multiple(async () =>
        {
            Assert.That(statuses, Is.All.EqualTo(DocumentStatus.Accepted));
            Assert.That((await UsageAsync(host, companyPlanId)).Single().Used, Is.EqualTo(concurrent + 1));
        });
    }

    // Dos procesos que deciden aceptar el mismo documento a la vez (por ejemplo, una consulta repetida tomada por otra
    // instancia): cada uno cuenta en su propio ámbito, pero la cuenta va en el guardado de la aceptación, y el segundo
    // guardado falla por la versión de fila del documento, así que su cuenta tampoco se escribe.
    [Test]
    public async Task Two_workers_accepting_the_same_document_at_once_count_it_once()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var companyPlanId = await AssignPlanAsync(host, issuer);
        var document = await host.CreateDocumentAsync(issuer, status: DocumentStatus.Unconfirmed, sentAt: host.Clock.UtcNow);
        await using var first = host.OpenScope();
        await using var second = host.OpenScope();
        foreach (var scope in new[] { first, second })
        {
            var loaded = await scope.ServiceProvider.GetRequiredService<IDocumentDelivery>()
                .FindForUpdateAsync(issuer.CompanyId, document.Id, CancellationToken.None);
            loaded!.MoveTo(DocumentStatus.Accepted);
            await scope.ServiceProvider.GetRequiredService<IPlanGate>().CountAcceptedAsync(issuer.CompanyId, DocumentKind.Invoice, CancellationToken.None);
        }

        await first.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        Assert.ThrowsAsync<PersistenceConflictException>(() =>
            second.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None));

        Assert.Multiple(async () =>
        {
            Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Accepted));
            Assert.That((await UsageAsync(host, companyPlanId)).Single().Used, Is.EqualTo(1));
        });
    }

    // La cuenta se ejecuta con el guardado: si la transacción del que llama se revierte, la cuenta se revierte con ella.
    [Test]
    public async Task A_count_inside_a_transaction_that_is_rolled_back_is_not_written()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var companyPlanId = await AssignPlanAsync(host, issuer);
        await using var scope = host.OpenScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var gate = scope.ServiceProvider.GetRequiredService<IPlanGate>();

        await using (var transaction = await unitOfWork.BeginTransactionAsync(CancellationToken.None))
        {
            await gate.CountAcceptedAsync(issuer.CompanyId, DocumentKind.Invoice, CancellationToken.None);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            await transaction.RollbackAsync(CancellationToken.None);
        }

        await gate.CountAcceptedAsync(issuer.CompanyId, DocumentKind.Invoice, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        Assert.That((await UsageAsync(host, companyPlanId)).Single().Used, Is.EqualTo(1));
    }

    /// <summary>Un paquete activo de <paramref name="quantity"/> facturas contratado ayer por la empresa.</summary>
    private static async Task<Guid> AssignPlanAsync(DianSendHost host, TestIssuer issuer, long quantity = 100)
    {
        var planId = Interlocked.Increment(ref planSequence);
        await using var scope = host.OpenScope();
        var db = Db(scope);
        db.Add(new Plan { Id = planId, Name = $"Paquete {planId}", Period = PlanPeriod.Package, IsActive = true });
        db.Add(new PlanQuota { PlanId = planId, DocumentKind = DocumentKind.Invoice, Quantity = quantity });
        var companyPlan = new CompanyPlan
        {
            CompanyId = issuer.CompanyId, PlanId = planId, StartsAt = host.Clock.UtcNow.AddDays(-1), IsActive = true,
        };
        db.Add(companyPlan);
        await db.SaveChangesAsync();
        return companyPlan.Id;
    }

    private static async Task<List<PlanUsage>> UsageAsync(DianSendHost host, Guid companyPlanId)
    {
        await using var scope = host.OpenScope();
        return await Db(scope).Set<PlanUsage>().AsNoTracking().Where(row => row.CompanyPlanId == companyPlanId).ToListAsync();
    }

    private static ImpiFactDbContext Db(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>();
}
