using System.Net;
using System.Text.Json;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Integration.Tests.Companies;
using ImpiFact.Integration.Tests.DianSend;
using static ImpiFact.Integration.Tests.DocumentUtilities.UtilitiesHost;

namespace ImpiFact.Integration.Tests.DocumentUtilities;

/// <summary>
/// <c>POST /api/ubl2.1/numbering-range</c> (alias <c>GET /api/v1/companies/{nit}/numbering-ranges</c>): la única consulta
/// en vivo a la DIAN desde la API. Responde como el PHP (<c>message</c>, <c>ResponseDian</c>,
/// <c>certificate_days_left</c>) con los rangos leídos del sobre.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class NumberingRangeApiTests
{
    private const string HabilitationUrl = "https://vpfe-hab.dian.gov.co/WcfDianCustomerServices.svc";

    private static FakeDian Dian => UtilitiesHost.Api.Dian;

    [Test]
    public async Task The_legacy_route_answers_the_php_shape_with_the_ranges_the_dian_returned()
    {
        var company = await CompanyAsync();
        var client = await ClientAsync(company, ApiScopes.ConfigWrite);
        var before = Dian.Requests.Count;
        Dian.Reply(NumberingRangeSamples.TwoRanges);

        var (status, body) = await SendAsync(client, HttpMethod.Post, "/api/ubl2.1/numbering-range", """{"IDSoftware":"soft-123","GuardarEn":"C:\\temp"}""");

        var request = Dian.Requests.Skip(before).Single();
        var result = body.GetProperty("ResponseDian").GetProperty("Envelope").GetProperty("Body")
            .GetProperty("GetNumberingRangeResponse").GetProperty("GetNumberingRangeResult");
        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(200), body.ToString());
            Assert.That(body.GetProperty("success").GetBoolean(), Is.True);
            Assert.That(body.GetProperty("message").GetString(), Is.EqualTo("Consulta generada con éxito"));
            Assert.That(body.GetProperty("certificate_days_left").GetInt32(), Is.EqualTo(3637));
            Assert.That(body.GetProperty("operation_code").GetString(), Is.EqualTo("100"));
            Assert.That(result.GetProperty("OperationCode").GetString(), Is.EqualTo("100"));
            Assert.That(result.GetProperty("ResponseList").GetProperty("NumberRangeResponse").GetProperty("Prefix").GetString(), Is.EqualTo("SETP"));
            Assert.That(body.GetProperty("numbering_ranges").GetRawText(), Is.EqualTo(
                """[{"resolution_number":"18760000001","resolution_date":"2019-01-19","prefix":"SETP","from":990000000,"to":995000000,"valid_date_from":"2019-01-19","valid_date_to":"2030-01-19","technical_key":"fc8eac422eba16e22ffd8c6f94b3f40a6e38162c"}]"""));
            Assert.That(request.Url.ToString(), Is.EqualTo(HabilitationUrl));
            Assert.That(request.Operation, Is.EqualTo("GetNumberingRange"));
            Assert.That(request.Parameter("accountCode"), Is.EqualTo(company.Nit));
            Assert.That(request.Parameter("accountCodeT"), Is.EqualTo(company.Nit));
            Assert.That(request.Parameter("softwareCode"), Is.EqualTo("soft-123"));
        });
    }

    [Test]
    public async Task The_canonical_route_uses_the_nit_of_the_route_and_the_software_of_the_query()
    {
        var company = await CompanyAsync();
        var client = await ClientAsync(company, ApiScopes.ConfigWrite);
        var before = Dian.Requests.Count;
        Dian.Reply(NumberingRangeSamples.NotAuthorized);

        var (status, body) = await SendAsync(client, HttpMethod.Get, $"/api/v1/companies/{company.Nit}/numbering-ranges?software_id=soft-9");

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(200), body.ToString());
            Assert.That(body.GetProperty("success").GetBoolean(), Is.False);
            Assert.That(body.GetProperty("operation_code").GetString(), Is.EqualTo("301"));
            Assert.That(body.GetProperty("operation_description").GetString(), Is.EqualTo("Nit no autorizado."));
            Assert.That(body.GetProperty("numbering_ranges").GetArrayLength(), Is.Zero);
            Assert.That(Dian.Requests.Skip(before).Single().Parameter("softwareCode"), Is.EqualTo("soft-9"));
        });
    }

    [Test]
    public async Task Another_company_nit_is_forbidden_without_calling_the_dian()
    {
        var company = await CompanyAsync();
        var other = await CompanyAsync();
        var client = await ClientAsync(company, ApiScopes.ConfigWrite);
        var before = Dian.Requests.Count;

        var (status, body) = await SendAsync(client, HttpMethod.Get, $"/api/v1/companies/{other.Nit}/numbering-ranges?software_id=soft-9");

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(403));
            Assert.That(body.GetProperty("code").GetString(), Is.EqualTo("company.forbidden"));
            Assert.That(Dian.Requests, Has.Count.EqualTo(before));
        });
    }

    [Test]
    public async Task The_software_id_is_required()
    {
        var company = await CompanyAsync();
        var client = await ClientAsync(company, ApiScopes.ConfigWrite);

        var (status, body) = await SendAsync(client, HttpMethod.Post, "/api/ubl2.1/numbering-range", "{}");

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(422));
            Assert.That(body.GetProperty("errors").GetProperty("IDSoftware")[0].GetString(), Is.EqualTo("El campo IDSoftware es obligatorio."));
        });
    }

    [Test]
    public async Task A_company_without_an_active_certificate_gets_404()
    {
        var company = await CompanyAsync(withCertificate: false);
        var client = await ClientAsync(company, ApiScopes.ConfigWrite);

        var (status, body) = await SendAsync(client, HttpMethod.Post, "/api/ubl2.1/numbering-range", """{"IDSoftware":"soft-1"}""");

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(404));
            Assert.That(body.GetProperty("code").GetString(), Is.EqualTo("certificate.not_found"));
        });
    }

    [Test]
    public async Task An_expired_certificate_is_refused_like_the_php()
    {
        var company = await CompanyAsync(certificateNotAfter: CompaniesHost.Now);
        var client = await ClientAsync(company, ApiScopes.ConfigWrite);

        var (status, body) = await SendAsync(client, HttpMethod.Post, "/api/ubl2.1/numbering-range", """{"IDSoftware":"soft-1"}""");

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(422));
            Assert.That(body.GetProperty("code").GetString(), Is.EqualTo("certificate.expired"));
            Assert.That(body.GetProperty("message").GetString(), Is.EqualTo("El certificado digital ya se encuentra vencido..."));
        });
    }

    [Test]
    public async Task A_soap_fault_is_a_dian_rejection()
    {
        var company = await CompanyAsync();
        var client = await ClientAsync(company, ApiScopes.ConfigWrite);
        Dian.Reply(DianReplies.ClientFault, HttpStatusCode.InternalServerError);

        var (status, body) = await SendAsync(client, HttpMethod.Post, "/api/ubl2.1/numbering-range", """{"IDSoftware":"soft-1"}""");

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(422));
            Assert.That(body.GetProperty("code").GetString(), Is.EqualTo("dian.fault"));
            Assert.That(body.GetProperty("success").GetBoolean(), Is.False);
            Assert.That(body.GetProperty("message").GetString(),
                Is.EqualTo("La DIAN rechazó la consulta: The creator of this fault did not specify a Reason."));
        });
    }

    [Test]
    public async Task A_service_that_is_down_answers_503_with_the_php_text()
    {
        var company = await CompanyAsync();
        var client = await ClientAsync(company, ApiScopes.ConfigWrite);
        Dian.Reply(DianReplies.ServiceUnavailablePage);

        using var response = await client.PostAsync(
            "/api/ubl2.1/numbering-range", new StringContent("""{"IDSoftware":"soft-1"}""", System.Text.Encoding.UTF8, "application/json"));
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Multiple(() =>
        {
            Assert.That((int)response.StatusCode, Is.EqualTo(503));
            Assert.That(response.Headers.RetryAfter, Is.Not.Null);
            Assert.That(body.GetProperty("code").GetString(), Is.EqualTo("dian.unavailable"));
            Assert.That(body.GetProperty("message").GetString(),
                Is.EqualTo("El servicio de la DIAN no está disponible en este momento. Por favor, inténtelo más tarde."));
        });
    }

    [Test]
    public async Task Documents_scopes_are_not_enough()
    {
        var company = await CompanyAsync();
        var client = await ClientAsync(company, ApiScopes.DocumentsRead, ApiScopes.DocumentsWrite);

        var (status, _) = await SendAsync(client, HttpMethod.Post, "/api/ubl2.1/numbering-range", """{"IDSoftware":"soft-1"}""");

        Assert.That(status, Is.EqualTo(403));
    }
}

/// <summary>Respuestas de GetNumberingRange de la DIAN falsa, con la forma de las muestras de <c>ImpiFact.Dian.Tests</c>.</summary>
internal static class NumberingRangeSamples
{
    public const string TwoRanges = """
        <s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope" xmlns:a="http://www.w3.org/2005/08/addressing"><s:Header><a:Action s:mustUnderstand="1">http://wcf.dian.colombia/IWcfDianCustomerServices/GetNumberingRangeResponse</a:Action></s:Header><s:Body><GetNumberingRangeResponse xmlns="http://wcf.dian.colombia"><GetNumberingRangeResult xmlns:b="http://schemas.datacontract.org/2004/07/NumberRangeResponseList" xmlns:i="http://www.w3.org/2001/XMLSchema-instance"><b:OperationCode>100</b:OperationCode><b:OperationDescription>Acción completada OK.</b:OperationDescription><b:ResponseList xmlns:c="http://schemas.datacontract.org/2004/07/NumberRangeResponse"><c:NumberRangeResponse><c:ResolutionNumber>18760000001</c:ResolutionNumber><c:ResolutionDate>2019-01-19</c:ResolutionDate><c:Prefix>SETP</c:Prefix><c:FromNumber>990000000</c:FromNumber><c:ToNumber>995000000</c:ToNumber><c:ValidDateFrom>2019-01-19</c:ValidDateFrom><c:ValidDateTo>2030-01-19</c:ValidDateTo><c:TechnicalKey>fc8eac422eba16e22ffd8c6f94b3f40a6e38162c</c:TechnicalKey></c:NumberRangeResponse></b:ResponseList></GetNumberingRangeResult></GetNumberingRangeResponse></s:Body></s:Envelope>
        """;

    public const string NotAuthorized = """
        <s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope"><s:Body><GetNumberingRangeResponse xmlns="http://wcf.dian.colombia"><GetNumberingRangeResult xmlns:b="http://schemas.datacontract.org/2004/07/NumberRangeResponseList" xmlns:i="http://www.w3.org/2001/XMLSchema-instance"><b:OperationCode>301</b:OperationCode><b:OperationDescription>Nit no autorizado.</b:OperationDescription><b:ResponseList i:nil="true"/></GetNumberingRangeResult></GetNumberingRangeResponse></s:Body></s:Envelope>
        """;
}
