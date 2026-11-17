using System.Text;
using ImpiFact.Application.Features.Documents.Email;

namespace ImpiFact.Application.Tests.Features.Documents.Email;

/// <summary>
/// Lo que la solicitud guardada de la emisión pide del correo (<c>sendmail</c>, <c>sendmailtome</c>,
/// <c>email_cc_list</c>, <c>email_pos_customer</c>, <c>atacheddocument_name_prefix</c> y el HTML propio), leído como
/// lo lee ApiDian (la implementación de referencia en PHP): un valor cuenta como verdadero con las reglas de PHP.
/// </summary>
public sealed class DocumentEmailRequestTests
{
    [Test]
    public void It_reads_every_email_field_of_the_stored_request()
    {
        var request = Parse("""
            {
              "sendmail": true, "sendmailtome": 1,
              "email_cc_list": [ { "email": "cc1@example.com" }, "cc2@example.com" ],
              "email_pos_customer": "pos@example.com", "atacheddocument_name_prefix": "PRE-",
              "html_header": "<p>H</p>", "html_body": "<p>B</p>", "html_buttons": "<p>K</p>", "html_footer": "<p>F</p>"
            }
            """);

        Assert.Multiple(() =>
        {
            Assert.That(request.SendMail, Is.True);
            Assert.That(request.SendMailToMe, Is.True);
            Assert.That(request.CcList, Is.EqualTo(new[] { "cc1@example.com", "cc2@example.com" }));
            Assert.That(request.PosCustomerEmail, Is.EqualTo("pos@example.com"));
            Assert.That(request.AttachedDocumentNamePrefix, Is.EqualTo("PRE-"));
            Assert.That(request.HtmlHeader, Is.EqualTo("<p>H</p>"));
            Assert.That(request.HtmlBody, Is.EqualTo("<p>B</p>"));
            Assert.That(request.HtmlButtons, Is.EqualTo("<p>K</p>"));
            Assert.That(request.HtmlFooter, Is.EqualTo("<p>F</p>"));
        });
    }

    [TestCase("false")]
    [TestCase("0")]
    [TestCase("\"0\"")]
    [TestCase("\"\"")]
    [TestCase("null")]
    [TestCase("[]")]
    public void A_value_PHP_considers_empty_does_not_ask_for_the_email(string value) =>
        Assert.That(Parse($$"""{ "sendmail": {{value}} }""").SendMail, Is.False);

    [TestCase("true")]
    [TestCase("1")]
    [TestCase("\"1\"")]
    [TestCase("\"false\"")]
    public void A_value_PHP_considers_true_asks_for_the_email(string value) =>
        Assert.That(Parse($$"""{ "sendmail": {{value}} }""").SendMail, Is.True);

    [Test]
    public void A_request_without_the_email_fields_asks_for_nothing()
    {
        var request = Parse("""{ "number": 1 }""");

        Assert.Multiple(() =>
        {
            Assert.That(request.SendMail, Is.False);
            Assert.That(request.SendMailToMe, Is.False);
            Assert.That(request.CcList, Is.Empty);
            Assert.That(request.PosCustomerEmail, Is.Null);
            Assert.That(request.AttachedDocumentNamePrefix, Is.Null);
        });
    }

    [TestCase(null)]
    [TestCase("not json")]
    [TestCase("[1, 2]")]
    public void A_missing_or_unreadable_request_asks_for_nothing(string? body) =>
        Assert.That(DocumentEmailRequest.Parse(body is null ? null : Encoding.UTF8.GetBytes(body)).SendMail, Is.False);

    private static DocumentEmailRequest Parse(string json) => DocumentEmailRequest.Parse(Encoding.UTF8.GetBytes(json));
}
