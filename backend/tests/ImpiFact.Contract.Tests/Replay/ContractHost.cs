using ImpiFact.Application.Abstractions;
using ImpiFact.Infrastructure.Persistence.Seeding;
using ImpiFact.Integration.Tests.Companies;
using ImpiFact.Integration.Tests.DianSend;
using ImpiFact.Integration.Tests.DocumentUtilities;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace ImpiFact.Contract.Tests.Replay;

/// <summary>
/// PostgreSQL desechable en Docker (Testcontainers), la API real encima y el Worker real sobre la misma base, una sola
/// vez para las pruebas de contrato. La API y el Worker comparten la llave maestra, la carpeta de certificados y la de
/// archivos, como en un despliegue. Ninguno llama a la DIAN (la autoridad tributaria de Colombia) real: cada uno tiene
/// su DIAN falsa, que responde «aceptado» a todo envío y consulta (<see cref="AcceptEverything"/>).
/// </summary>
[SetUpFixture]
[Category("Contract")]
public sealed class ContractHost
{
    /// <summary>ZipKey que la DIAN falsa devuelve a SendTestSetAsync.</summary>
    public const string ZipKey = "4c1b2a3d-0e5f-4a6b-8c7d-9e0f1a2b3c4d";

    private static PostgreSqlContainer? container;
    private static CompanyApiFactory? api;
    private static DianSendHost? worker;

    public static CompanyApiFactory Api => api ?? throw new InvalidOperationException("The contract API host is not running.");

    public static DianSendHost Worker => worker ?? throw new InvalidOperationException("The contract Worker host is not running.");

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        container = new PostgreSqlBuilder("postgres:18.1-alpine").Build();
        await container.StartAsync();
        api = new CompanyApiFactory(container.GetConnectionString());
        await using (var scope = api.OpenScope(companyId: null, userId: null, ActorType.System))
        {
            await scope.Db.Database.MigrateAsync();
            await new CatalogSynchronizer(scope.Db).SynchronizeAsync(CancellationToken.None);
        }

        worker = DianSendHost.Create(api.SharedSettings, container.GetConnectionString());
        worker.Dian.Route = AcceptEverything;
        api.Dian.Route = AcceptEverything;
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        if (worker is not null)
        {
            await worker.DisposeAsync();
        }

        if (api is not null)
        {
            await api.DisposeAsync();
        }

        if (container is not null)
        {
            await container.DisposeAsync();
        }
    }

    /// <summary>La DIAN falsa: acepta todo documento y responde los rangos de numeración de la resolución de prueba.</summary>
    private static HttpResponseMessage? AcceptEverything(FakeDianRequest request) => request.Operation switch
    {
        "SendBillSync" => FakeDian.Response(DianReplies.SendBillSync(true, "00")),
        "GetStatus" => FakeDian.Response(DianReplies.GetStatus(true, "00")),
        "SendTestSetAsync" => FakeDian.Response(DianReplies.SendTestSetAsync(ZipKey)),
        "GetStatusZip" => FakeDian.Response(DianReplies.GetStatusZip(true, "00")),
        "GetNumberingRange" => FakeDian.Response(NumberingRangeSamples.TwoRanges),
        _ => null,
    };
}
