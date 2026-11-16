using System.Text;
using ImpiFact.Application.Features.Documents.Issue;
using ImpiFact.Dian.Ubl;

namespace ImpiFact.Application.Tests.Features.Documents.Issue;

/// <summary>Los secretos de la emisión no se filtran por accidente: ni al registrar un objeto ni al guardar la solicitud.</summary>
public sealed class SecretHandlingTests
{
    [Test]
    public void Software_credentials_never_print_the_pin()
    {
        var credentials = new SoftwareCredentials("soft-1", "86420");

        Assert.That(credentials.ToString(), Does.Not.Contain("86420"));
        Assert.That(credentials.ToString(), Does.Contain("soft-1"));
    }

    [TestCase("smtp_parameters")]
    [TestCase("SMTP_PARAMETERS")]
    [TestCase("Smtp_Parameters")]
    public void The_stored_request_drops_secret_fields_whatever_their_case(string field)
    {
        var body = Encoding.UTF8.GetBytes("{\"number\":1,\"" + field + "\":{\"password\":\"clave-smtp\"}}");

        var stored = Encoding.UTF8.GetString(IssueDocumentJson.ForStorage(body));

        Assert.That(stored, Does.Not.Contain("clave-smtp"));
        Assert.That(stored, Does.Contain("\"number\":1"));
    }
}
