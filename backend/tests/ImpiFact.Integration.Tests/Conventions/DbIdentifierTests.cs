using ImpiFact.Infrastructure.Persistence.Conventions;

namespace ImpiFact.Integration.Tests.Conventions;

/// <summary>
/// Nombres físicos de la base de datos. PostgreSQL corta en silencio todo nombre de más de 63 caracteres, y dos
/// nombres largos podrían quedar iguales; por eso la convención los acorta ella misma con una huella.
/// Son pruebas sin Docker, en un espacio de nombres aparte del que levanta PostgreSQL.
/// </summary>
[TestFixture]
public sealed class DbIdentifierTests
{
    [TestCase("Widgets", "widgets")]
    [TestCase("CompanyId", "company_id")]
    [TestCase("DisplayName", "display_name")]
    [TestCase("DianSubmissions", "dian_submissions")]
    public void Names_are_converted_to_snake_case(string name, string expected) =>
        Assert.That(DbIdentifier.ToSnakeCase(name), Is.EqualTo(expected));

    [Test]
    public void Names_up_to_63_characters_are_kept()
    {
        var name = new string('a', 63);

        Assert.That(DbIdentifier.Shorten(name), Is.EqualTo(name));
    }

    [Test]
    public void Longer_names_are_cut_to_63_characters_with_a_stable_distinct_suffix()
    {
        var first = "ix_document_lines_" + new string('x', 60) + "_first";
        var second = "ix_document_lines_" + new string('x', 60) + "_second";

        Assert.Multiple(() =>
        {
            Assert.That(DbIdentifier.Shorten(first), Has.Length.EqualTo(63));
            Assert.That(DbIdentifier.Shorten(first), Does.StartWith("ix_document_lines_"));
            Assert.That(DbIdentifier.Shorten(first), Is.EqualTo(DbIdentifier.Shorten(first)));
            Assert.That(DbIdentifier.Shorten(first), Is.Not.EqualTo(DbIdentifier.Shorten(second)));
        });
    }
}
