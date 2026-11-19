using ImpiFact.Domain.Documents;
using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Integration.Tests.Model;

/// <summary>
/// <c>ux_documents_number</c> y <c>ux_documents_cufe</c> impiden duplicados y permiten varios documentos sin CUFE
/// (Modelo de datos detallado, criterio 6 de la sección 11). También se comprueba que un documento completo vuelve
/// sin perder decimales ni datos de sus columnas JSON.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class DocumentUniquenessTests
{
    private Guid companyId;

    [OneTimeSetUp]
    public async Task CreateCompanyAsync() => companyId = (await ModelGraph.CreateAsync()).CompanyId;

    [Test]
    public async Task Several_documents_without_cufe_are_allowed()
    {
        await SaveAsync(ModelGraph.NewDocument(companyId, 1, cufe: null), ModelGraph.NewDocument(companyId, 2, cufe: null));

        await using var session = ModelDatabase.Open(companyId);
        var withoutCufe = await session.Db.Set<Document>().CountAsync(document => document.Cufe == null && document.Number <= 2);

        Assert.That(withoutCufe, Is.EqualTo(2));
    }

    [Test]
    public async Task The_same_cufe_cannot_be_saved_twice_in_a_company()
    {
        await SaveAsync(ModelGraph.NewDocument(companyId, 10, cufe: "same-cufe"));

        var exception = Assert.ThrowsAsync<DbUpdateException>(() => SaveAsync(ModelGraph.NewDocument(companyId, 11, cufe: "same-cufe")));

        Assert.That(ConstraintOf(exception), Is.EqualTo("ux_documents_cufe"));
    }

    [Test]
    public async Task The_same_prefix_and_number_cannot_be_saved_twice()
    {
        await SaveAsync(ModelGraph.NewDocument(companyId, 20, cufe: null));

        var exception = Assert.ThrowsAsync<DbUpdateException>(() => SaveAsync(ModelGraph.NewDocument(companyId, 20, cufe: null)));

        Assert.That(ConstraintOf(exception), Is.EqualTo("ux_documents_number"));
    }

    [Test]
    public async Task A_document_round_trips_its_decimals_and_json_columns()
    {
        var document = ModelGraph.NewDocument(companyId, 30, cufe: null);
        var line = new DocumentLine
        {
            CompanyId = companyId,
            DocumentId = document.Id,
            LineNumber = 1,
            Description = "Line",
            TypeItemIdentificationId = 1,
            UnitMeasureId = 1,
            Quantity = 3.123456m,
            BaseQuantity = 1m,
            UnitPrice = 1234.567891m,
            LineExtensionAmount = 3855.97m,
        };
        await SaveAsync(document, line);

        await using var session = ModelDatabase.Open(companyId);
        var saved = await session.Db.Set<Document>().SingleAsync(found => found.Id == document.Id);
        var savedLine = await session.Db.Set<DocumentLine>().SingleAsync(found => found.Id == line.Id);

        Assert.Multiple(() =>
        {
            Assert.That(savedLine.Quantity, Is.EqualTo(3.123456m));
            Assert.That(savedLine.UnitPrice, Is.EqualTo(1234.567891m));
            Assert.That(savedLine.LineExtensionAmount, Is.EqualTo(3855.97m));
            Assert.That(saved.TaxInclusiveAmount, Is.EqualTo(119m));
            Assert.That(saved.CustomerSnapshot.IdentificationNumber, Is.EqualTo("222222222222"));
            Assert.That(saved.IssueTime, Is.EqualTo(new TimeOnly(5, 0)));
            Assert.That(saved.RowVersion, Is.EqualTo(1));
        });
    }

    private static string? ConstraintOf(DbUpdateException? exception) => TestDatabase.ViolatedConstraint(exception);

    private static async Task SaveAsync(params object[] rows)
    {
        await using var session = ModelDatabase.Open(companyId: null);
        session.Db.AddRange(rows);
        await session.Db.SaveChangesAsync();
    }
}
