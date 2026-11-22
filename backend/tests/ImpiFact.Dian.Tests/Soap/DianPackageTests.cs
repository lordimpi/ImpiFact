using System.IO.Compression;
using System.Text;
using ImpiFact.Dian.Soap;

namespace ImpiFact.Dian.Tests.Soap;

// Pruebas del empaquetado que exige la DIAN: el XML firmado va dentro de un ZIP (archivo comprimido) que se
// envía en Base64 (texto que representa bytes), con nombres de archivo fijos por tipo de documento.
public class DianPackageTests
{
    private static readonly DateTimeOffset ModifiedAt = new(2026, 11, 15, 10, 30, 0, TimeSpan.FromHours(-5));

    [Test]
    public void Xml_entry_name_matches_the_name_the_dian_returned_for_a_real_invoice()
    {
        // XmlFileName de una respuesta real de la DIAN (README de ApiDian): NIT 901846280, año 2025, consecutivo 166.
        Assert.That(DianFileNames.XmlEntry(DianFileKind.Invoice, "901846280", 2025, 166),
            Is.EqualTo("fv09018462800002500000166.xml"));
    }

    [TestCase(DianFileKind.Invoice, "fv")]
    [TestCase(DianFileKind.CreditNote, "nc")]
    [TestCase(DianFileKind.DebitNote, "nd")]
    [TestCase(DianFileKind.SupportDocument, "dse")]
    [TestCase(DianFileKind.SupportDocumentAdjustmentNote, "nds")]
    public void Xml_entry_name_prefix_depends_on_the_document_kind(DianFileKind kind, string prefix)
    {
        Assert.That(DianFileNames.XmlEntry(kind, "900123456", 2026, 1), Is.EqualTo(prefix + "09001234560002600000001.xml"));
    }

    [Test]
    public void Names_pad_nit_to_10_digits_year_to_2_and_counter_to_8()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DianFileNames.XmlEntry(DianFileKind.CreditNote, "123", 2009, 42), Is.EqualTo("nc00000001230000900000042.xml"));
            Assert.That(DianFileNames.XmlEntry(DianFileKind.DebitNote, "1234567890", 2100, 99_999_999),
                Is.EqualTo("nd12345678900000099999999.xml"));
        });
    }

    [Test]
    public void Zip_name_uses_prefix_z_and_no_random_suffix()
    {
        Assert.That(DianFileNames.Zip("901846280", 2025, 166), Is.EqualTo("z09018462800002500000166.zip"));
    }

    [TestCase("")]
    [TestCase("12345678901")]
    [TestCase("90012345-6")]
    [TestCase("9001 2345")]
    public void Invalid_nit_is_rejected(string nit)
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => DianFileNames.XmlEntry(DianFileKind.Invoice, nit, 2026, 1));
            Assert.Throws<ArgumentException>(() => DianFileNames.Zip(nit, 2026, 1));
        });
    }

    [TestCase(-1)]
    [TestCase(100_000_000)]
    public void Counter_outside_8_digits_is_rejected(long counter)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DianFileNames.XmlEntry(DianFileKind.Invoice, "900123456", 2026, counter));
    }

    [Test]
    public void Negative_year_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DianFileNames.Zip("900123456", -1, 1));
    }

    [TestCase("SETP", 990000001, "SETP990000001.xml")]
    [TestCase("", 15, "15.xml")]
    [TestCase(null, 15, "15.xml")]
    public void Soap_file_name_is_prefix_and_number(string? prefix, long number, string expected)
    {
        Assert.That(DianFileNames.SoapFileName(prefix, number), Is.EqualTo(expected));
    }

    [Test]
    public void Zip_holds_one_entry_with_the_given_name_and_identical_bytes()
    {
        var xml = Encoding.UTF8.GetBytes("<Invoice>áéí ñ</Invoice>");

        var zip = DianZip.Create("fv09001234560002600000001.xml", xml, ModifiedAt);

        var entry = SingleEntry(zip, out var content);
        Assert.Multiple(() =>
        {
            Assert.That(entry, Is.EqualTo("fv09001234560002600000001.xml"));
            Assert.That(content, Is.EqualTo(xml));
        });
    }

    [Test]
    public void Zip_from_text_stores_utf8_without_bom()
    {
        var zip = DianZip.Create("fv.xml", "<Invoice>ñ</Invoice>", ModifiedAt);

        SingleEntry(zip, out var content);
        Assert.That(content, Is.EqualTo(new UTF8Encoding(false).GetBytes("<Invoice>ñ</Invoice>")));
    }

    [Test]
    public void Zip_base64_decodes_to_the_same_zip_and_is_deterministic()
    {
        var xml = Encoding.UTF8.GetBytes("<Invoice/>");

        var base64 = DianZip.CreateBase64("fv.xml", xml, ModifiedAt);

        Assert.Multiple(() =>
        {
            Assert.That(Convert.FromBase64String(base64), Is.EqualTo(DianZip.Create("fv.xml", xml, ModifiedAt)));
            Assert.That(DianZip.CreateBase64("fv.xml", xml, ModifiedAt), Is.EqualTo(base64));
        });
    }

    private static string SingleEntry(byte[] zip, out byte[] content)
    {
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        var entry = archive.Entries.Single();
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        content = buffer.ToArray();
        return entry.FullName;
    }
}
