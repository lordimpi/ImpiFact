using System.Text.Json;
using System.Text.RegularExpressions;

namespace ImpiFact.Contract.Tests.Postman;

/// <summary>
/// La copia de la colección y las reglas que la recorren, sin Docker: que la copia es la colección completa del PRD
/// (sección 9, 185 solicitudes en 14 carpetas), que no trae credenciales, que el alcance de la fase 4 nunca toma lo
/// prohibido y que la lista de diferencias conocidas solo nombra solicitudes o rutas que existen.
/// </summary>
[TestFixture]
public sealed partial class CollectionSnapshotTests
{
    private static readonly PostmanCollection Collection = PostmanCollection.LoadSnapshot();

    [Test]
    public void The_snapshot_is_the_whole_collection_of_the_prd()
    {
        var perFolder = Collection.TopFolders.Select(folder => Collection.Requests.Count(request => request.Folders[0] == folder));

        Assert.Multiple(() =>
        {
            Assert.That(Collection.Name, Is.EqualTo("FacturaLatam ApiDianV2.1"));
            Assert.That(Collection.Requests, Has.Count.EqualTo(185));
            Assert.That(Collection.TopFolders.Select(folder => folder[..2]), Is.EqualTo(new[]
            {
                "01", "02", "03", "04", "05", "06", "07", "08", "09", "10", "11", "12", "13", "14",
            }));
            Assert.That(perFolder, Is.EqualTo(new[] { 21, 47, 5, 19, 26, 2, 16, 3, 7, 4, 11, 16, 4, 4 }));
        });
    }

    [Test]
    public void The_snapshot_carries_no_credentials()
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "collection", PostmanCollection.SnapshotFileName));

        Assert.Multiple(() =>
        {
            Assert.That(JsonWebToken().IsMatch(text), Is.False, "A JSON web token or a Laravel cookie is left.");
            Assert.That(Pkcs12().IsMatch(text), Is.False, "A PKCS#12 certificate in base64 is left.");
            Assert.That(BearerValue().Matches(text).Select(match => match.Value).Distinct(), Is.All.Not.Match("[A-Za-z0-9]{20,}"));
            Assert.That(Collection.Requests.SelectMany(request => request.Headers)
                .Where(header => header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) || header.Key.Equals("X-Admin-Token", StringComparison.OrdinalIgnoreCase))
                .Select(header => header.Value), Is.All.Empty);
        });
    }

    [Test]
    public void The_phase_four_scope_never_takes_what_the_architecture_forbids()
    {
        var inScope = Collection.Requests.Where(request => ContractScope.OutOfScopeReason(request) is null).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(inScope.Where(request => !ContractScope.PhaseSevenRequests.Contains(request.Name)).Select(request => request.TopFolder).Distinct(),
                Is.SubsetOf(ContractScope.PhaseFourFolders));
            Assert.That(inScope.Where(request => ContractScope.PhaseSevenRequests.Contains(request.Name)).Select(request => request.Name),
                Is.EquivalentTo(ContractScope.PhaseSevenRequests), "Every phase 7a request is in the collection once.");
            Assert.That(inScope.Select(request => request.ApiPath), Has.None.Match("payroll|eqdoc|send-email-employee|support-document|sd-credit-note|contingency"));
            Assert.That(inScope.Select(request => request.Name), Has.None.Match("(?i)n[oó]mina|soporte|equivalente|contingencia"));
            Assert.That(inScope.Select(request => request.Name), Is.Unique, "Known deviations name requests, so names must be unique.");
            Assert.That(inScope, Has.Count.EqualTo(94), "91 requests of the first delivery and the 3 phase 7a invoice variants.");
        });
    }

    [Test]
    public void Every_body_in_scope_is_json_once_its_comments_are_stripped()
    {
        var bodies = Collection.Requests.Where(request => ContractScope.OutOfScopeReason(request) is null && request.Body is not null);

        Assert.Multiple(() =>
        {
            foreach (var request in bodies)
            {
                Assert.DoesNotThrow(() => JsonDocument.Parse(request.JsonBody!).Dispose(), request.Title);
            }
        });
    }

    [Test]
    public void Comments_are_stripped_outside_strings_only()
    {
        const string body = "{\n  \"url\": \"http://a.b/c\", // comentario\n  /* bloque */ \"n\": 1 // fin\n}";

        Assert.That(JsonComments.Strip(body), Is.EqualTo("{\n  \"url\": \"http://a.b/c\", \n   \"n\": 1 \n}"));
    }

    [Test]
    public void Every_known_deviation_names_a_request_or_route_in_scope()
    {
        var inScope = Collection.Requests.Where(request => ContractScope.OutOfScopeReason(request) is null).ToList();
        var names = inScope.Select(request => request.Name).ToHashSet(StringComparer.Ordinal);
        var routes = inScope.Select(ExpectedContract.RouteOf).ToHashSet(StringComparer.Ordinal);

        Assert.That(KnownDeviations.All.Where(deviation => !names.Contains(deviation.Request) && !routes.Contains(deviation.Request))
            .Select(deviation => deviation.Request), Is.Empty);
    }

    [Test]
    public void Routes_without_an_example_borrow_the_one_of_the_same_route()
    {
        var contract = new ExpectedContract(Collection);
        var debitNote = Collection.Requests.First(request => request.ApiPath == "/api/ubl2.1/debit-note");
        var invoiceWithoutExample = Collection.Requests.First(request => request.ApiPath == "/api/ubl2.1/invoice" && request.Examples.Count == 0);

        Assert.Multiple(() =>
        {
            Assert.That(contract.KeysFor(debitNote), Does.Contain("cude").And.Not.Contain("success"));
            Assert.That(contract.KeysFor(invoiceWithoutExample), Does.Contain("cufe").And.Contain("urlinvoicexml").And.Contain("ResponseDian"));
            Assert.That(ExpectedContract.RouteOf(debitNote), Is.EqualTo("POST /api/ubl2.1/debit-note"));
        });
    }

    [GeneratedRegex("eyJ[A-Za-z0-9_-]{20,}")]
    private static partial Regex JsonWebToken();

    [GeneratedRegex("MII[A-Za-z0-9+/]{60,}")]
    private static partial Regex Pkcs12();

    [GeneratedRegex("Bearer [^\"\\s<]+")]
    private static partial Regex BearerValue();
}
