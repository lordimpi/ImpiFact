using System.Text;
using ImpiFact.Dian.Ubl;

namespace ImpiFact.Dian.Tests.Ubl;

// UBL es el formato XML estándar de facturas que usa la DIAN. ApiDian (el PHP original) escribe ese XML con
// libxml, la biblioteca XML de PHP; estas pruebas fijan el mismo formato (sangría, escapes, finales de
// línea) para que ImpiFact produzca exactamente los mismos bytes.
public sealed class UblXmlSerializerTests
{
    private const string Declaration = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>\n";

    [Test]
    public void Writes_the_declaration_and_ends_with_a_newline()
    {
        var xml = UblXmlSerializer.Serialize(new UblElement("Invoice", "x"));

        Assert.That(xml, Is.EqualTo(Declaration + "<Invoice>x</Invoice>\n"));
    }

    [Test]
    public void Writes_empty_elements_without_a_space_before_the_slash()
    {
        var root = new UblElement("a")
            .Add(new UblElement("b"))
            .Add("c", "")
            .Add(new UblElement("d").Attr("k", "v"));

        var xml = UblXmlSerializer.Serialize(root);

        Assert.That(xml, Is.EqualTo(Declaration + "<a>\n  <b/>\n  <c/>\n  <d k=\"v\"/>\n</a>\n"));
    }

    [Test]
    public void Indents_children_with_two_spaces_per_level_and_LF_line_ends()
    {
        var root = new UblElement("a").Add(new UblElement("b").Add("c", "1"));

        var xml = UblXmlSerializer.Serialize(root);

        Assert.That(xml, Is.EqualTo(Declaration + "<a>\n  <b>\n    <c>1</c>\n  </b>\n</a>\n"));
        Assert.That(xml, Does.Not.Contain("\r"));
    }

    [Test]
    public void Keeps_attributes_in_insertion_order()
    {
        var root = new UblElement("cbc:ID", "SERV01")
            .Attr("schemeID", "999")
            .Attr("schemeName", "n")
            .Attr("schemeAgencyID", "");

        var xml = UblXmlSerializer.Serialize(root);

        Assert.That(xml, Does.Contain("<cbc:ID schemeID=\"999\" schemeName=\"n\" schemeAgencyID=\"\">SERV01</cbc:ID>"));
    }

    [Test]
    public void Escapes_text_like_libxml()
    {
        var xml = UblXmlSerializer.Serialize(new UblElement("a", "A & B < C > D \"q\" 'a'\r"));

        Assert.That(xml, Does.Contain("<a>A &amp; B &lt; C &gt; D \"q\" 'a'&#13;</a>"));
    }

    [Test]
    public void Escapes_attributes_like_libxml()
    {
        var xml = UblXmlSerializer.Serialize(new UblElement("a").Attr("v", "x&<>\"\n\r\t'"));

        Assert.That(xml, Does.Contain("<a v=\"x&amp;&lt;&gt;&quot;&#10;&#13;&#9;'\"/>"));
    }

    [Test]
    public void Keeps_whitespace_and_newlines_inside_text()
    {
        var xml = UblXmlSerializer.Serialize(new UblElement("a", "\n    1.00\n  "));

        Assert.That(xml, Does.EndWith("<a>\n    1.00\n  </a>\n"));
    }

    [Test]
    public void Writes_non_ascii_as_raw_utf8_without_bom()
    {
        var bytes = UblXmlSerializer.SerializeToUtf8(new UblElement("a", "Bogotá"));

        Assert.That(bytes[0], Is.EqualTo((byte)'<'), "no byte order mark");
        Assert.That(Encoding.UTF8.GetString(bytes), Does.Contain("<a>Bogotá</a>"));
        Assert.That(bytes, Does.Not.Contain((byte)'&'), "no character references");
    }

    [Test]
    public void Rejects_mixed_content()
    {
        Assert.Throws<InvalidOperationException>(() => new UblElement("a", "text").Add(new UblElement("b")));
        Assert.Throws<InvalidOperationException>(() => new UblElement("a").Add(new UblElement("b")).SetText("text"));
    }
}
