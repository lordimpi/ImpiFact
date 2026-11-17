using ImpiFact.Application.Abstractions.Billing;
using ImpiFact.Application.Features.Billing;
using ImpiFact.Application.Tests.Features.Documents.Issue;
using ImpiFact.Domain.Billing;
using ImpiFact.Domain.Common;

namespace ImpiFact.Application.Tests.Features.Billing;

/// <summary>
/// Revisión del plan antes de emitir, con las reglas y los mensajes de ApiDian (la implementación de referencia en
/// PHP; <c>InvoiceController::store</c>): plan inactivo, plan por defecto sin límites, mensual y anual por tiempo o por
/// cantidad, paquete por cantidad. Una empresa sin plan contratado no tiene límites.
/// </summary>
public sealed class PlanGateTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid CompanyId = Guid.CreateVersion7();

    [Test]
    public async Task A_company_without_plan_may_issue()
    {
        var gate = Gate(current: null);

        Assert.That(await gate.CheckIssueAsync(CompanyId, DocumentKind.Invoice, CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task An_inactive_plan_is_refused_with_the_php_message_whatever_its_period()
    {
        var gate = Gate(Current(PlanPeriod.Default, isActive: false));

        var refused = await gate.CheckIssueAsync(CompanyId, DocumentKind.Invoice, CancellationToken.None);

        Assert.That(refused, Is.EqualTo(BillingErrors.PlanInactive));
        Assert.That(refused!.Value.Message,
            Is.EqualTo("El plan en el que esta registrado la empresa se encuentra en el momento INACTIVO para enviar documentos electronicos..."));
    }

    [Test]
    public async Task The_default_period_has_no_limits()
    {
        var gate = Gate(Current(PlanPeriod.Default, quantity: 0, used: 999, startsAt: Now.AddYears(-5)));

        Assert.That(await gate.CheckIssueAsync(CompanyId, DocumentKind.Invoice, CancellationToken.None), Is.Null);
    }

    [TestCase(PlanPeriod.Monthly, "La empresa ha llegado al limite de tiempo/documentos del plan por mensualidad, por favor renueve su membresia...")]
    [TestCase(PlanPeriod.Yearly, "La empresa ha llegado al limite de tiempo/documentos del plan por anualidad, por favor renueve su membresia...")]
    [TestCase(PlanPeriod.Package, "La empresa ha llegado al limite de documentos del plan por paquetes, por favor renueve su membresia...")]
    public async Task A_plan_whose_documents_are_used_up_is_refused(PlanPeriod period, string message)
    {
        var gate = Gate(Current(period, quantity: 10, used: 10));

        var refused = await gate.CheckIssueAsync(CompanyId, DocumentKind.Invoice, CancellationToken.None);

        Assert.That(refused?.Code, Is.EqualTo("plan.limit_reached"));
        Assert.That(refused?.Message, Is.EqualTo(message));
        Assert.That(refused?.Type, Is.EqualTo(Abstractions.Results.ErrorType.Forbidden));
    }

    [TestCase(PlanPeriod.Monthly)]
    [TestCase(PlanPeriod.Yearly)]
    [TestCase(PlanPeriod.Package)]
    public async Task A_plan_with_documents_left_and_within_its_period_may_issue(PlanPeriod period)
    {
        var gate = Gate(Current(period, quantity: 10, used: 9));

        Assert.That(await gate.CheckIssueAsync(CompanyId, DocumentKind.Invoice, CancellationToken.None), Is.Null);
    }

    // Como el PHP: el mensual vence al mes de empezar y el anual al año, aunque queden documentos; el paquete no vence.
    [TestCase(PlanPeriod.Monthly, 1, 0, true)]
    [TestCase(PlanPeriod.Monthly, 0, 29, false)]
    [TestCase(PlanPeriod.Yearly, 12, 0, true)]
    [TestCase(PlanPeriod.Yearly, 11, 29, false)]
    [TestCase(PlanPeriod.Package, 60, 0, false)]
    public async Task Monthly_and_yearly_plans_expire_with_time(PlanPeriod period, int monthsAgo, int daysAgo, bool refused)
    {
        var gate = Gate(Current(period, quantity: 10, used: 0, startsAt: Now.AddMonths(-monthsAgo).AddDays(-daysAgo)));

        var result = await gate.CheckIssueAsync(CompanyId, DocumentKind.Invoice, CancellationToken.None);

        Assert.That(result is not null, Is.EqualTo(refused));
    }

    [Test]
    public async Task An_explicit_end_replaces_the_period_and_also_ends_a_package()
    {
        var expired = Gate(Current(PlanPeriod.Package, quantity: 10, used: 0, endsAt: Now));
        var extended = Gate(Current(PlanPeriod.Monthly, quantity: 10, used: 0, startsAt: Now.AddMonths(-3), endsAt: Now.AddDays(1)));

        Assert.That(await expired.CheckIssueAsync(CompanyId, DocumentKind.Invoice, CancellationToken.None), Is.EqualTo(BillingErrors.PackageLimitReached));
        Assert.That(await extended.CheckIssueAsync(CompanyId, DocumentKind.Invoice, CancellationToken.None), Is.Null);
    }

    // Las columnas qty_docs_* del PHP valen 0 por defecto: un plan sin cantidad para la familia no deja emitir.
    [Test]
    public async Task A_family_without_quantity_in_the_plan_has_none_left()
    {
        var gate = Gate(Current(PlanPeriod.Package, quantity: 10, used: 0, kind: DocumentKind.Payroll));

        Assert.That(await gate.CheckIssueAsync(CompanyId, DocumentKind.Invoice, CancellationToken.None), Is.EqualTo(BillingErrors.PackageLimitReached));
    }

    [Test]
    public async Task Counting_an_accepted_document_adds_it_to_the_current_company_plan()
    {
        var current = Current(PlanPeriod.Package);
        var store = new FakePlanStore { Current = current };

        await new PlanGate(store, new IssueClock(Now)).CountAcceptedAsync(CompanyId, DocumentKind.Invoice, CancellationToken.None);

        Assert.That(store.Counted, Is.EqualTo(new[] { (CompanyId, current.CompanyPlanId, DocumentKind.Invoice) }));
    }

    [Test]
    public async Task Counting_without_a_plan_does_nothing()
    {
        var store = new FakePlanStore();

        await new PlanGate(store, new IssueClock(Now)).CountAcceptedAsync(CompanyId, DocumentKind.Invoice, CancellationToken.None);

        Assert.That(store.Counted, Is.Empty);
    }

    private static PlanGate Gate(CurrentPlan? current) => new(new FakePlanStore { Current = current }, new IssueClock(Now));

    private static CurrentPlan Current(
        PlanPeriod period,
        bool isActive = true,
        long quantity = 10,
        long used = 0,
        DateTimeOffset? startsAt = null,
        DateTimeOffset? endsAt = null,
        DocumentKind kind = DocumentKind.Invoice)
    {
        var plan = new Plan { Id = 1, Name = "Plan", Period = period, IsActive = isActive };
        return new CurrentPlan(
            Guid.CreateVersion7(),
            new PlanWithQuotas(plan, new Dictionary<DocumentKind, long> { [kind] = quantity }),
            startsAt ?? Now.AddDays(-1),
            endsAt,
            new Dictionary<DocumentKind, long> { [kind] = used });
    }
}
