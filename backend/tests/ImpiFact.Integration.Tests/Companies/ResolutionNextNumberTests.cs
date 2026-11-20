using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Domain.Companies;
using ImpiFact.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using static ImpiFact.Integration.Tests.Companies.CompanyApiTests;

namespace ImpiFact.Integration.Tests.Companies;

/// <summary>
/// <c>PUT /api/v1/companies/{nit}/resolutions/{id}/next-number</c>: la empresa fija el próximo número de una de sus
/// resoluciones, por ejemplo si viene de otro sistema de facturación o ya usó números del rango. El número debe estar
/// dentro del rango y nunca puede bajar, porque bajarlo podría repetir un número ya enviado a la DIAN.
/// </summary>
[Category("Integration")]
public sealed class ResolutionNextNumberTests
{
    private const long RangeFrom = 990000000;
    private const long RangeTo = 995000000;

    private static CompanyApiFactory Api => CompaniesHost.Api;

    [Test]
    public async Task Moves_the_next_number_forward_and_the_list_shows_it()
    {
        var (company, nit, resolutionId) = await CompanyWithResolutionAsync();

        using var response = await company.PutAsJsonAsync(Path(nit, resolutionId), new { next_number = 990000002 });
        var body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);
        using var json = JsonDocument.Parse(body);
        var sequence = await SequenceAsync(nit, resolutionId);
        using var list = await company.GetAsync($"/api/v1/companies/{nit}/resolutions");
        using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var listed = listJson.RootElement.GetProperty("resolutions").EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == resolutionId);
        Assert.Multiple(() =>
        {
            Assert.That(json.RootElement.GetProperty("success").GetBoolean(), Is.True);
            Assert.That(json.RootElement.GetProperty("message").GetString(), Is.EqualTo("Próximo número actualizado."));
            Assert.That(json.RootElement.GetProperty("resolution_id").GetGuid(), Is.EqualTo(resolutionId));
            Assert.That(json.RootElement.GetProperty("next_number").GetInt64(), Is.EqualTo(990000002));
            Assert.That(sequence.NextNumber, Is.EqualTo(990000002));
            Assert.That(sequence.RowVersion, Is.EqualTo(2), "The UPDATE raises row_version like SaveChanges would.");
            Assert.That(listed.GetProperty("next_number").GetInt64(), Is.EqualTo(990000002));
            Assert.That(listed.GetProperty("remaining").GetInt64(), Is.EqualTo(RangeTo - 990000002 + 1));
        });
    }

    [Test]
    public async Task Setting_the_current_next_number_again_is_accepted_and_changes_nothing()
    {
        var (company, nit, resolutionId) = await CompanyWithResolutionAsync();

        using var response = await company.PutAsJsonAsync(Path(nit, resolutionId), new { next_number = RangeFrom });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await SequenceAsync(nit, resolutionId)).NextNumber, Is.EqualTo(RangeFrom));
    }

    [Test]
    public async Task A_number_below_the_current_next_number_is_refused_and_the_sequence_stays()
    {
        var (company, nit, resolutionId) = await CompanyWithResolutionAsync();
        (await company.PutAsJsonAsync(Path(nit, resolutionId), new { next_number = 990000050 })).Dispose();
        var before = await SequenceAsync(nit, resolutionId);

        using var response = await company.PutAsJsonAsync(Path(nit, resolutionId), new { next_number = 990000010 });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
        var after = await SequenceAsync(nit, resolutionId);
        Assert.Multiple(async () =>
        {
            Assert.That(await FieldError(response, "next_number"), Is.EqualTo(
                "El próximo número no puede ser menor que 990000050, el próximo número actual: bajarlo podría repetir un " +
                "número ya enviado a la DIAN."));
            Assert.That(after.NextNumber, Is.EqualTo(990000050));
            Assert.That(after.RowVersion, Is.EqualTo(before.RowVersion), "The refused change is rolled back.");
        });
    }

    [TestCase(RangeFrom - 1)]
    [TestCase(RangeTo + 1)]
    public async Task A_number_outside_the_range_of_the_resolution_is_refused(long number)
    {
        var (company, nit, resolutionId) = await CompanyWithResolutionAsync();

        using var response = await company.PutAsJsonAsync(Path(nit, resolutionId), new { next_number = number });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
        Assert.Multiple(async () =>
        {
            Assert.That(await FieldError(response, "next_number"),
                Is.EqualTo("El próximo número debe estar dentro del rango de la resolución, entre 990000000 y 995000000."));
            Assert.That((await SequenceAsync(nit, resolutionId)).NextNumber, Is.EqualTo(RangeFrom));
        });
    }

    [Test]
    public async Task The_last_number_of_the_range_is_accepted()
    {
        var (company, nit, resolutionId) = await CompanyWithResolutionAsync();

        using var response = await company.PutAsJsonAsync(Path(nit, resolutionId), new { next_number = RangeTo });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await SequenceAsync(nit, resolutionId)).NextNumber, Is.EqualTo(RangeTo));
    }

    [Test]
    public async Task The_next_number_is_required()
    {
        var (company, nit, resolutionId) = await CompanyWithResolutionAsync();

        using var response = await company.PutAsJsonAsync(Path(nit, resolutionId), new { });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
        Assert.That(await FieldError(response, "next_number"), Is.EqualTo("El campo next number es obligatorio."));
    }

    [Test]
    public async Task A_resolution_of_another_company_answers_404_like_a_missing_one()
    {
        var (company, nit, _) = await CompanyWithResolutionAsync();
        var (_, _, foreignResolutionId) = await CompanyWithResolutionAsync();

        using var foreign = await company.PutAsJsonAsync(Path(nit, foreignResolutionId), new { next_number = 990000005 });
        using var missing = await company.PutAsJsonAsync(Path(nit, Guid.CreateVersion7()), new { next_number = 990000005 });

        Assert.Multiple(async () =>
        {
            Assert.That(foreign.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            using var json = await Json(foreign);
            Assert.That(json.RootElement.GetProperty("code").GetString(), Is.EqualTo("resolution.not_found"));
        });
        Assert.That((await SequenceAsyncById(foreignResolutionId)).NextNumber, Is.EqualTo(RangeFrom));
    }

    [Test]
    public async Task Another_nit_answers_403_and_the_route_needs_config_write()
    {
        var (company, nit, resolutionId) = await CompanyWithResolutionAsync();
        var (_, otherNit) = await CreateCompanyAsync();
        Guid companyId;
        await using (var scope = Api.OpenScope(null, null, ActorType.System))
        {
            companyId = await CompanyIdAsync(scope.Db, nit);
        }

        using var reader = await Api.ClientWithNewKeyAsync(companyId, ApiScopes.DocumentsRead, ApiScopes.DocumentsWrite);
        using var foreignNit = await company.PutAsJsonAsync(Path(otherNit, resolutionId), new { next_number = 990000005 });
        using var withoutScope = await reader.PutAsJsonAsync(Path(nit, resolutionId), new { next_number = 990000005 });

        Assert.Multiple(() =>
        {
            Assert.That(foreignNit.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(withoutScope.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        });
        Assert.That((await SequenceAsync(nit, resolutionId)).NextNumber, Is.EqualTo(RangeFrom));
    }

    private static string Path(string nit, Guid resolutionId) => $"/api/v1/companies/{nit}/resolutions/{resolutionId}/next-number";

    private static async Task<(HttpClient Company, string Nit, Guid ResolutionId)> CompanyWithResolutionAsync()
    {
        var (company, nit) = await CreateCompanyAsync();
        using var saved = await company.PutAsJsonAsync($"/api/v1/companies/{nit}/resolution", new
        {
            type_document_id = 1, prefix = "SETP", resolution = "18760000001", resolution_date = "2019-01-19",
            technical_key = "fc8eac422eba16e22ffd8c6f94b3f40a6e38162c", from = RangeFrom, to = RangeTo,
            date_from = "2019-01-19", date_to = "2030-01-19",
        });
        Assert.That(saved.StatusCode, Is.EqualTo(HttpStatusCode.OK), await saved.Content.ReadAsStringAsync());
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        var companyId = await CompanyIdAsync(scope.Db, nit);
        var resolution = await Rows<Resolution>(scope.Db, companyId).SingleAsync();
        return (company, nit, resolution.Id);
    }

    private static async Task<NumberingSequence> SequenceAsync(string nit, Guid resolutionId)
    {
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        var companyId = await CompanyIdAsync(scope.Db, nit);
        var scopeKey = NumberingScope.ForResolution(resolutionId);
        return await Rows<NumberingSequence>(scope.Db, companyId).AsNoTracking().SingleAsync(row => row.ScopeKey == scopeKey);
    }

    private static async Task<NumberingSequence> SequenceAsyncById(Guid resolutionId)
    {
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        var scopeKey = NumberingScope.ForResolution(resolutionId);
        return await scope.Db.Set<NumberingSequence>().IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.ScopeKey == scopeKey);
    }

    private static async Task<string?> FieldError(HttpResponseMessage response, string field)
    {
        using var json = await Json(response);
        return json.RootElement.GetProperty("errors").GetProperty(field)[0].GetString();
    }
}
