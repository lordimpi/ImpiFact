using System.Reflection;
using ImpiFact.Domain.Billing;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Companies;
using ImpiFact.Domain.Documents;
using ImpiFact.Domain.Identity;
using ImpiFact.Domain.Masters;
using ImpiFact.Domain.Platform;
using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Integration.Tests.Model;

/// <summary>
/// Aislamiento multiempresa (Modelo de datos detallado, sección 7): «Una prueba por cada entidad con
/// <c>company_id</c>: crear datos en dos empresas y verificar que ninguna consulta cruza». Además, las llaves
/// foráneas compuestas impiden que una fila apunte a una fila de otra empresa.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class TenantIsolationTests
{
    /// <summary>Todas las entidades con <c>company_id</c> obligatorio, que llevan el filtro de empresa.</summary>
    public static readonly Type[] TenantEntities =
    [
        typeof(UserCompany), typeof(CompanyLiability), typeof(CompanyEnvironment), typeof(CompanySoftware),
        typeof(Certificate), typeof(Resolution), typeof(CompanyMailSettings), typeof(CompanyLogo), typeof(Customer),
        typeof(Item), typeof(Document), typeof(DocumentLine), typeof(DocumentTax), typeof(DocumentLineTax),
        typeof(DocumentAllowanceCharge), typeof(DocumentPayment), typeof(DocumentReference), typeof(DocumentFile),
        typeof(DianSubmission), typeof(DocumentEvent), typeof(ReceivedDocument), typeof(NumberingSequence),
        typeof(CompanyPlan), typeof(Notification), typeof(IdempotencyKey),
    ];

    private static readonly MethodInfo CompanyIdsMethod =
        typeof(TenantIsolationTests).GetMethod(nameof(CompanyIdsOfAsync), BindingFlags.Static | BindingFlags.NonPublic)!;

    private ModelGraph companyA = null!;
    private ModelGraph companyB = null!;

    [OneTimeSetUp]
    public async Task CreateTwoCompaniesAsync()
    {
        companyA = await ModelGraph.CreateAsync();
        companyB = await ModelGraph.CreateAsync();
    }

    [Test]
    public async Task Every_entity_with_company_id_is_covered_by_an_isolation_case()
    {
        await using var session = ModelDatabase.Open(companyId: null);
        var tenantTypes = session.Db.Model.GetEntityTypes()
            .Select(entityType => entityType.ClrType)
            .Where(type => typeof(ITenantOwned).IsAssignableFrom(type));

        Assert.That(tenantTypes, Is.EquivalentTo(TenantEntities));
    }

    [TestCaseSource(nameof(TenantEntities))]
    public async Task A_company_only_sees_its_own_rows(Type entity)
    {
        var seenByA = await CompanyIdsAsync(entity, companyA.CompanyId);
        var seenByB = await CompanyIdsAsync(entity, companyB.CompanyId);
        var seenWithoutCompany = await CompanyIdsAsync(entity, companyId: null);

        Assert.Multiple(() =>
        {
            Assert.That(seenByA, Is.Not.Empty.And.All.EqualTo(companyA.CompanyId));
            Assert.That(seenByB, Is.Not.Empty.And.All.EqualTo(companyB.CompanyId));
            Assert.That(seenWithoutCompany, Is.Empty);
        });
    }

    [Test]
    public async Task A_row_cannot_reference_a_row_of_another_company()
    {
        await using var session = ModelDatabase.Open(companyId: null);
        session.Db.Add(new DocumentLine
        {
            CompanyId = companyA.CompanyId,
            DocumentId = companyB.DocumentId,
            LineNumber = 99,
            Description = "Cross-company line",
            TypeItemIdentificationId = 1,
            UnitMeasureId = 1,
        });

        var exception = Assert.ThrowsAsync<DbUpdateException>(() => session.Db.SaveChangesAsync());

        Assert.That(TestDatabase.IsForeignKeyViolation(exception), Is.True, exception?.InnerException?.Message);
    }

    private static Task<List<Guid>> CompanyIdsAsync(Type entity, Guid? companyId) =>
        (Task<List<Guid>>)CompanyIdsMethod.MakeGenericMethod(entity).Invoke(null, [companyId])!;

    private static async Task<List<Guid>> CompanyIdsOfAsync<TEntity>(Guid? companyId)
        where TEntity : class, ITenantOwned
    {
        await using var session = ModelDatabase.Open(companyId);
        return await session.Db.Set<TEntity>().Select(entity => entity.CompanyId).ToListAsync();
    }
}
