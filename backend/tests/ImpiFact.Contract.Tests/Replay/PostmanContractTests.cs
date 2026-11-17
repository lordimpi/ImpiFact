using System.Globalization;
using System.Text;
using ImpiFact.Contract.Tests.Postman;
using ImpiFact.Contract.Tests.Replay;

namespace ImpiFact.Contract.Tests.Replay;

/// <summary>
/// La puerta de la fase 4 (PRD, sección 12): «Peticiones Postman de la primera entrega en verde». Reproduce en orden las
/// solicitudes de la colección que entran en la fase (<see cref="ContractScope"/>) contra la API real con la DIAN
/// falsa, y compara cada respuesta con el contrato (<see cref="ExpectedContract"/>): código 2xx y las claves del primer
/// nivel del ejemplo del PHP. Las diferencias aceptadas están en <see cref="KnownDeviations"/>. Al final escribe una
/// tabla por carpeta con las que pasaron, las diferencias conocidas y las que quedan fuera de la fase.
/// </summary>
[TestFixture]
[Category("Contract")]
public sealed class PostmanContractTests
{
    /// <summary>Rutas que borran la empresa de la sesión: se reproducen al final para no dejar sin empresa a las demás.</summary>
    private static readonly string[] DestructiveRoutes = ["/delete/company/"];

    [Test]
    public async Task The_first_delivery_requests_of_the_collection_honor_the_php_contract()
    {
        var collection = PostmanCollection.LoadSnapshot();
        var contract = new ExpectedContract(collection);
        var replay = new ContractReplay(ContractHost.Api, ContractHost.Worker);
        var results = new List<RequestResult>();
        var used = new HashSet<KnownDeviation>();

        var ordered = collection.Requests
            .OrderBy(request => DestructiveRoutes.Any(route => request.ApiPath.Contains(route, StringComparison.Ordinal)))
            .ThenBy(request => request.Index);
        foreach (var request in ordered)
        {
            if (ContractScope.OutOfScopeReason(request) is { } reason)
            {
                results.Add(new RequestResult(request, Verdict.Skipped, reason));
                continue;
            }

            var outcome = await replay.RunAsync(request);
            var missing = outcome.IsSuccess
                ? contract.KeysFor(request).Where(key => outcome.Body is null || !outcome.Body.ContainsKey(key)).ToList()
                : [];
            var deviation = Covering(request, outcome, missing);
            if (deviation is not null)
            {
                used.Add(deviation);
                results.Add(new RequestResult(request, Verdict.Deviation, deviation.Reason));
                continue;
            }

            var problems = new List<string>(outcome.Problems);
            if (!outcome.IsSuccess)
            {
                problems.Insert(0, $"HTTP {outcome.Status} instead of 2xx");
            }

            if (missing.Count > 0)
            {
                problems.Insert(0, $"missing keys: {string.Join(", ", missing)}");
            }

            results.Add(problems.Count == 0
                ? new RequestResult(request, Verdict.Passed, "")
                : new RequestResult(request, Verdict.Failed, $"{string.Join("; ", problems)} ({request.Method} {outcome.Path}): {Excerpt(outcome.Text)}"));
        }

        TestContext.Out.WriteLine(Summary(collection, results));
        var failures = results.Where(result => result.Verdict == Verdict.Failed)
            .Select(result => $"{result.Request.Title}: {result.Detail}")
            .Concat(KnownDeviations.All.Where(deviation => !used.Contains(deviation))
                .Select(deviation => $"Known deviation no longer happens, remove it: {deviation.Request} ({deviation.Reason})"))
            .ToList();
        Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
    }

    /// <summary>La diferencia conocida que explica la respuesta, si hay una.</summary>
    private static KnownDeviation? Covering(PostmanRequest request, ReplayOutcome outcome, IReadOnlyList<string> missing)
    {
        if (outcome.Problems.Count > 0)
        {
            return null;
        }

        var route = ExpectedContract.RouteOf(request);
        return KnownDeviations.All
            .Where(deviation => deviation.Request == request.Name || deviation.Request == route)
            .FirstOrDefault(deviation => outcome.IsSuccess
                ? deviation.Status is null && missing.Count > 0 && missing.All(deviation.MissingKeys.Contains)
                : deviation.Status == outcome.Status);
    }

    private static string Summary(PostmanCollection collection, List<RequestResult> results)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"Contract tests: {collection.Name}, {collection.Requests.Count} requests");
        text.AppendLine("| Folder | Passed | Known deviation | Failed | Skipped (out of phase 4) |");
        text.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var folder in collection.TopFolders)
        {
            var inFolder = results.Where(result => result.Request.Folders[0] == folder).ToList();
            text.AppendLine(CultureInfo.InvariantCulture,
                $"| {folder} | {Count(inFolder, Verdict.Passed)} | {Count(inFolder, Verdict.Deviation)} | {Count(inFolder, Verdict.Failed)} | {Count(inFolder, Verdict.Skipped)} |");
        }

        text.AppendLine(CultureInfo.InvariantCulture,
            $"| Total | {Count(results, Verdict.Passed)} | {Count(results, Verdict.Deviation)} | {Count(results, Verdict.Failed)} | {Count(results, Verdict.Skipped)} |");
        text.AppendLine();
        foreach (var result in results.Where(result => result.Verdict is Verdict.Deviation or Verdict.Failed))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"- {result.Verdict}: {result.Request.Title}: {result.Detail}");
        }

        return text.ToString();
    }

    private static int Count(IEnumerable<RequestResult> results, Verdict verdict) => results.Count(result => result.Verdict == verdict);

    private static string Excerpt(string text) => text.Length <= 600 ? text : text[..600] + "…";

    private enum Verdict
    {
        Passed,
        Deviation,
        Failed,
        Skipped,
    }

    private sealed record RequestResult(PostmanRequest Request, Verdict Verdict, string Detail);
}
