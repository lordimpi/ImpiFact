using ImpiFact.Application.Features.Documents.Email;

namespace ImpiFact.Application.Tests.Features.Documents.Email;

/// <summary>
/// A quién va el correo del documento aceptado, como ApiDian (InvoiceController, líneas 668 a 686): solo si la
/// solicitud lo pidió (<c>sendmail</c>); nunca al consumidor final 222222222222 salvo que llegue
/// <c>email_pos_customer</c>; copia al emisor con <c>sendmailtome</c> y a cada dirección de <c>email_cc_list</c>.
/// </summary>
public sealed class DocumentEmailRecipientsTests
{
    private const string Customer = "900111222";
    private const string CustomerEmail = "buyer@example.com";
    private const string CompanyEmail = "seller@example.com";

    [Test]
    public void Without_sendmail_no_email_is_sent()
    {
        var recipients = Resolve(Request() with { SendMailToMe = true, CcList = ["cc@example.com"] });

        Assert.Multiple(() =>
        {
            Assert.That(recipients.Skip, Is.EqualTo(EmailSkipReason.NotRequested));
            Assert.That(recipients.To, Is.Empty);
            Assert.That(recipients.Bcc, Is.Empty);
        });
    }

    [Test]
    public void The_final_consumer_gets_no_email_and_neither_do_the_copies()
    {
        var recipients = DocumentEmailRecipients.Resolve(
            Request(sendMail: true) with { SendMailToMe = true, CcList = ["cc@example.com"] },
            DocumentEmailRecipients.FinalConsumerIdentification, CustomerEmail, CompanyEmail);

        Assert.Multiple(() =>
        {
            Assert.That(recipients.Skip, Is.EqualTo(EmailSkipReason.FinalConsumer));
            Assert.That(recipients.To, Is.Empty);
            Assert.That(recipients.Bcc, Is.Empty);
        });
    }

    [Test]
    public void The_final_consumer_with_email_pos_customer_gets_the_email_at_that_address()
    {
        var recipients = DocumentEmailRecipients.Resolve(
            Request(sendMail: true) with { PosCustomerEmail = "pos@example.com" },
            DocumentEmailRecipients.FinalConsumerIdentification, CustomerEmail, CompanyEmail);

        Assert.Multiple(() =>
        {
            Assert.That(recipients.Skip, Is.Null);
            Assert.That(recipients.To, Is.EqualTo(new[] { "pos@example.com" }));
            Assert.That(recipients.Bcc, Is.Empty);
        });
    }

    [Test]
    public void The_buyer_gets_the_email_at_the_address_of_the_document()
    {
        var recipients = Resolve(Request(sendMail: true));

        Assert.Multiple(() =>
        {
            Assert.That(recipients.Skip, Is.Null);
            Assert.That(recipients.To, Is.EqualTo(new[] { CustomerEmail }));
            Assert.That(recipients.Bcc, Is.Empty);
        });
    }

    [Test]
    public void Email_pos_customer_replaces_the_buyer_address()
    {
        var recipients = Resolve(Request(sendMail: true) with { PosCustomerEmail = "pos@example.com" });

        Assert.That(recipients.To, Is.EqualTo(new[] { "pos@example.com" }));
    }

    [Test]
    public void Sendmailtome_and_the_cc_list_go_as_hidden_copies()
    {
        var recipients = Resolve(Request(sendMail: true) with { SendMailToMe = true, CcList = ["cc1@example.com", "cc2@example.com"] });

        Assert.Multiple(() =>
        {
            Assert.That(recipients.To, Is.EqualTo(new[] { CustomerEmail }));
            Assert.That(recipients.Bcc, Is.EqualTo(new[] { CompanyEmail, "cc1@example.com", "cc2@example.com" }));
        });
    }

    [Test]
    public void Repeated_and_invalid_copies_are_dropped()
    {
        var recipients = Resolve(Request(sendMail: true) with
        {
            SendMailToMe = true,
            CcList = ["SELLER@example.com", "not an address", "", "buyer@EXAMPLE.com", "cc@example.com", "cc@example.com"],
        });

        Assert.That(recipients.Bcc, Is.EqualTo(new[] { CompanyEmail, "cc@example.com" }));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("not an address")]
    public void A_buyer_without_a_valid_address_cannot_get_the_email(string? customerEmail)
    {
        var recipients = DocumentEmailRecipients.Resolve(Request(sendMail: true) with { SendMailToMe = true }, Customer, customerEmail, CompanyEmail);

        Assert.Multiple(() =>
        {
            Assert.That(recipients.Skip, Is.EqualTo(EmailSkipReason.NoRecipient));
            Assert.That(recipients.To, Is.Empty);
            Assert.That(recipients.Bcc, Is.Empty);
        });
    }

    private static EmailRecipients Resolve(DocumentEmailRequest request) =>
        DocumentEmailRecipients.Resolve(request, Customer, CustomerEmail, CompanyEmail);

    private static DocumentEmailRequest Request(bool sendMail = false) => DocumentEmailRequest.None with { SendMail = sendMail };
}
