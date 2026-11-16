using ImpiFact.Dian.Attached;

namespace ImpiFact.Dian.Tests.Attached;

// El archivo del AttachedDocument (contenedor electrónico) se llama como el XmlFileName que devuelve la
// DIAN, cambiando el prefijo fv (factura), nc (nota crédito) o nd (nota débito) por ad.
public class AttachedDocumentFileNameTests
{
    [TestCase("fv09000000010002600000001", "ad09000000010002600000001")]
    [TestCase("nc09000000010002600000012", "ad09000000010002600000012")]
    [TestCase("nd09000000010002600000123", "ad09000000010002600000123")]
    [TestCase("fv09000000010002600000001.xml", "ad09000000010002600000001.xml")]
    public void Replaces_the_document_prefix_with_ad(string dianXmlFileName, string expected)
    {
        Assert.That(AttachedDocumentFileName.FromDianXmlFileName(dianXmlFileName), Is.EqualTo(expected));
    }

    // El PHP reemplaza fv, nc y nd en cualquier parte del nombre; los nombres de la DIAN solo los llevan al
    // inicio (el resto son dígitos), así que basta con el prefijo.
    [Test]
    public void Only_the_prefix_is_replaced()
    {
        Assert.That(AttachedDocumentFileName.FromDianXmlFileName("fvnc0nd"), Is.EqualTo("adnc0nd"));
    }

    [TestCase("")]
    [TestCase("z09000000010002600000001")]
    [TestCase("FV09000000010002600000001")]
    [TestCase("ad09000000010002600000001")]
    public void Rejects_a_name_without_a_document_prefix(string dianXmlFileName)
    {
        var error = Assert.Throws<ArgumentException>(() => AttachedDocumentFileName.FromDianXmlFileName(dianXmlFileName));
        Assert.That(error!.ParamName, Is.EqualTo("dianXmlFileName"));
    }
}
