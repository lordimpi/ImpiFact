using System.Net;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Integration.Tests.Companies;
using ImpiFact.Integration.Tests.DianSend;
using static ImpiFact.Integration.Tests.Masters.MastersHost;

namespace ImpiFact.Integration.Tests.Masters;

/// <summary>
/// <c>GET /api/v1/customers/lookup</c>: consulta en vivo a la DIAN (servicio GetAcquirer) con el certificado y el ambiente
/// de la empresa, como la consulta de rangos de numeración. La DIAN es la falsa del host.
/// </summary>
[Category("Integration")]
public sealed class AcquirerLookupApiTests
{
    private const string HabilitationUrl = "https://vpfe-hab.dian.gov.co/WcfDianCustomerServices.svc";

    private static FakeDian Dian => MastersHost.Api.Dian;

    [Test]
    public async Task A_registered_acquirer_returns_its_name_and_email_and_the_dian_gets_the_identification_code()
    {
        var company = await CompanyAsync(withCertificate: true);
        using var client = await ClientAsync(company);
        var before = Dian.Requests.Count;
        Dian.Reply(AcquirerSamples.Found);

        var (status, body) = await SendAsync(client, HttpMethod.Get, "/api/v1/customers/lookup?type=6&number=900123456");

        var request = Dian.Requests.Skip(before).Single();
        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(200), body.ToString());
            Assert.That(body.GetProperty("found").GetBoolean(), Is.True);
            Assert.That(body.GetProperty("status_code").GetString(), Is.EqualTo("200"));
            Assert.That(body.GetProperty("message").GetString(), Is.EqualTo("Consulta exitosa."));
            Assert.That(body.GetProperty("name").GetString(), Is.EqualTo("CLIENTE DE PRUEBA SAS"));
            Assert.That(body.GetProperty("email").GetString(), Is.EqualTo("facturacion@cliente-prueba.com"));
            Assert.That(request.Operation, Is.EqualTo("GetAcquirer"));
            Assert.That(request.Parameter("identificationType"), Is.EqualTo("31"), "The DIAN code of NIT, not the catalog id.");
            Assert.That(request.Parameter("identificationNumber"), Is.EqualTo("900123456"));
            Assert.That(request.Url.ToString(), Is.EqualTo(HabilitationUrl), "A company without an environment asks the habilitation service.");
        });
    }

    [Test]
    public async Task An_unregistered_acquirer_is_not_found_without_an_error()
    {
        var company = await CompanyAsync(withCertificate: true);
        using var client = await ClientAsync(company);
        Dian.Reply(AcquirerSamples.NotFound);

        var (status, body) = await SendAsync(client, HttpMethod.Get, "/api/v1/customers/lookup?type=3&number=1020304050");

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(200), body.ToString());
            Assert.That(body.GetProperty("found").GetBoolean(), Is.False);
            Assert.That(body.GetProperty("status_code").GetString(), Is.EqualTo("404"));
            Assert.That(body.GetProperty("name").ValueKind, Is.EqualTo(System.Text.Json.JsonValueKind.Null));
            Assert.That(Dian.Requests[^1].Parameter("identificationType"), Is.EqualTo("13"));
        });
    }

    [Test]
    public async Task A_soap_fault_is_a_dian_rejection()
    {
        var company = await CompanyAsync(withCertificate: true);
        using var client = await ClientAsync(company);
        Dian.Reply(DianReplies.ClientFault, HttpStatusCode.InternalServerError);

        var (status, body) = await SendAsync(client, HttpMethod.Get, "/api/v1/customers/lookup?type=6&number=900123456");

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(422));
            Assert.That(body.GetProperty("code").GetString(), Is.EqualTo("dian.fault"));
            Assert.That(body.GetProperty("message").GetString(), Does.StartWith("La DIAN rechazó la consulta: "));
        });
    }

    [Test]
    public async Task Another_dian_status_is_a_dian_rejection_with_its_message()
    {
        var company = await CompanyAsync(withCertificate: true);
        using var client = await ClientAsync(company);
        Dian.Reply(AcquirerSamples.Failed);

        var (status, body) = await SendAsync(client, HttpMethod.Get, "/api/v1/customers/lookup?type=6&number=900123456");

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(422));
            Assert.That(body.GetProperty("code").GetString(), Is.EqualTo("dian.rejected"));
            Assert.That(body.GetProperty("message").GetString(), Is.EqualTo("La DIAN no pudo completar la consulta: Error interno."));
        });
    }

    [Test]
    public async Task A_service_that_is_down_answers_503()
    {
        var company = await CompanyAsync(withCertificate: true);
        using var client = await ClientAsync(company);
        Dian.Reply(DianReplies.ServiceUnavailablePage);

        using var response = await client.GetAsync("/api/v1/customers/lookup?type=6&number=900123456");
        var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Multiple(() =>
        {
            Assert.That((int)response.StatusCode, Is.EqualTo(503));
            Assert.That(response.Headers.RetryAfter, Is.Not.Null);
            Assert.That(body.GetProperty("code").GetString(), Is.EqualTo("dian.unavailable"));
        });
    }

    [Test]
    public async Task Without_an_active_certificate_or_with_an_expired_one_the_dian_is_not_called()
    {
        var withoutCertificate = await CompanyAsync();
        var expired = await CompanyAsync(withCertificate: true, certificateNotAfter: CompaniesHost.Now);
        using var first = await ClientAsync(withoutCertificate);
        using var second = await ClientAsync(expired);
        var before = Dian.Requests.Count;

        var (missing, missingBody) = await SendAsync(first, HttpMethod.Get, "/api/v1/customers/lookup?type=6&number=900123456");
        var (old, oldBody) = await SendAsync(second, HttpMethod.Get, "/api/v1/customers/lookup?type=6&number=900123456");

        Assert.Multiple(() =>
        {
            Assert.That(missing, Is.EqualTo(404));
            Assert.That(missingBody.GetProperty("code").GetString(), Is.EqualTo("certificate.not_found"));
            Assert.That(old, Is.EqualTo(422));
            Assert.That(oldBody.GetProperty("code").GetString(), Is.EqualTo("certificate.expired"));
            Assert.That(Dian.Requests, Has.Count.EqualTo(before));
        });
    }

    [TestCase("number=900123456", "type")]
    [TestCase("type=6", "number")]
    [TestCase("type=6&number=90-01", "number")]
    [TestCase("type=999&number=900123456", "type")]
    public async Task Invalid_parameters_are_a_422_without_calling_the_dian(string query, string field)
    {
        var company = await CompanyAsync(withCertificate: true);
        using var client = await ClientAsync(company);
        var before = Dian.Requests.Count;

        var (status, body) = await SendAsync(client, HttpMethod.Get, "/api/v1/customers/lookup?" + query);

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(422));
            Assert.That(body.GetProperty("errors").TryGetProperty(field, out _), Is.True, body.ToString());
            Assert.That(Dian.Requests, Has.Count.EqualTo(before));
        });
    }

    [Test]
    public async Task Reading_scope_alone_is_not_enough()
    {
        var company = await CompanyAsync(withCertificate: true);
        using var client = await ClientAsync(company, ApiScopes.DocumentsRead);

        var (status, _) = await SendAsync(client, HttpMethod.Get, "/api/v1/customers/lookup?type=6&number=900123456");

        Assert.That(status, Is.EqualTo(403));
    }

    [Test]
    public async Task The_logs_never_hold_the_certificate_or_the_identification()
    {
        var company = await CompanyAsync(withCertificate: true);
        using var client = await ClientAsync(company);
        Dian.Reply(AcquirerSamples.Found);

        await SendAsync(client, HttpMethod.Get, "/api/v1/customers/lookup?type=6&number=987654321");

        var logs = string.Join("\n", MastersHost.Api.Logs.Entries);
        Assert.Multiple(() =>
        {
            Assert.That(logs, Does.Contain("DIAN GetAcquirer for company"));
            Assert.That(logs, Does.Not.Contain("987654321"));
            Assert.That(logs, Does.Not.Contain("BEGIN CERTIFICATE").And.Not.Contain("X509Certificate"));
        });
    }
}

/// <summary>Respuestas de GetAcquirer de la DIAN falsa, con la forma de las muestras de <c>ImpiFact.Dian.Tests</c>.</summary>
public static class AcquirerSamples
{
    public const string Found = """
        <s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope" xmlns:a="http://www.w3.org/2005/08/addressing"><s:Header><a:Action s:mustUnderstand="1">http://wcf.dian.colombia/IWcfDianCustomerServices/GetAcquirerResponse</a:Action></s:Header><s:Body><GetAcquirerResponse xmlns="http://wcf.dian.colombia"><GetAcquirerResult xmlns:b="http://schemas.datacontract.org/2004/07/ExchangeEmailResponse" xmlns:i="http://www.w3.org/2001/XMLSchema-instance"><b:Message>Consulta exitosa.</b:Message><b:ReceiverEmail>facturacion@cliente-prueba.com</b:ReceiverEmail><b:ReceiverName>CLIENTE DE PRUEBA SAS</b:ReceiverName><b:StatusCode>200</b:StatusCode></GetAcquirerResult></GetAcquirerResponse></s:Body></s:Envelope>
        """;

    public const string NotFound = """
        <s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope"><s:Body><GetAcquirerResponse xmlns="http://wcf.dian.colombia"><GetAcquirerResult xmlns:b="http://schemas.datacontract.org/2004/07/ExchangeEmailResponse" xmlns:i="http://www.w3.org/2001/XMLSchema-instance"><b:Message>No se encontró información para el adquiriente.</b:Message><b:ReceiverEmail i:nil="true"/><b:ReceiverName i:nil="true"/><b:StatusCode>404</b:StatusCode></GetAcquirerResult></GetAcquirerResponse></s:Body></s:Envelope>
        """;

    public const string Failed = """
        <s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope"><s:Body><GetAcquirerResponse xmlns="http://wcf.dian.colombia"><GetAcquirerResult xmlns:b="http://schemas.datacontract.org/2004/07/ExchangeEmailResponse" xmlns:i="http://www.w3.org/2001/XMLSchema-instance"><b:Message>Error interno.</b:Message><b:StatusCode>500</b:StatusCode></GetAcquirerResult></GetAcquirerResponse></s:Body></s:Envelope>
        """;
}
