using ImpiFact.Application.Abstractions.Billing;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Abstractions.Persistence;
using ImpiFact.Domain.Billing;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Documents;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Platform.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static ImpiFact.Integration.Tests.DocumentUtilities.UtilitiesHost;

namespace ImpiFact.Integration.Tests.DianSend;

/// <summary>
/// Escrituras atómicas pendientes de la unidad de trabajo (<c>IPendingWrites</c>): un conteo del plan cuyo guardado
/// falló no lo escribe un guardado posterior del mismo ámbito.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class PendingWritesTests
{
    [Test]
    public async Task A_count_whose_save_failed_is_not_written_by_a_later_save_of_the_same_scope()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var planId = 9000 + Random.Shared.Next(1000);
        Guid companyPlanId;
        await using (var setup = host.OpenScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<ImpiFactDbContext>();
            db.Add(new Plan { Id = planId, Name = $"Probe {planId}", Period = PlanPeriod.Package, IsActive = true });
            db.Add(new PlanQuota { PlanId = planId, DocumentKind = DocumentKind.Invoice, Quantity = 100 });
            var companyPlan = new CompanyPlan { CompanyId = issuer.CompanyId, PlanId = planId, StartsAt = host.Clock.UtcNow.AddDays(-1), IsActive = true };
            db.Add(companyPlan);
            await db.SaveChangesAsync();
            companyPlanId = companyPlan.Id;
        }

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

        // El mismo ámbito sigue en uso después del guardado fallido (por ejemplo, para registrar otra cosa).
        second.ServiceProvider.GetRequiredService<ImpiFactDbContext>().ChangeTracker.Clear();
        await second.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);

        await using var check = host.OpenScope();
        var used = await check.ServiceProvider.GetRequiredService<ImpiFactDbContext>().Set<PlanUsage>().AsNoTracking()
            .Where(row => row.CompanyPlanId == companyPlanId).Select(row => row.Used).SingleAsync();
        Assert.That(used, Is.EqualTo(1));
    }
}
