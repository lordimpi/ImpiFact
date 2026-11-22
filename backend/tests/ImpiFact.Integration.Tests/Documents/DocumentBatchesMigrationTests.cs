using Dapper;
using ImpiFact.Application.Abstractions;
using ImpiFact.Infrastructure;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Persistence.Seeding;
using ImpiFact.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.Documents;

/// <summary>
/// The <c>DocumentBatches</c> migration (phase 7a, T8) creates <c>document_batches</c> and the nullable
/// <c>documents.batch_id</c> that links each document to its batch, and its Down removes both, on each database engine. It
/// runs on its own server because it migrates down and up.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class DocumentBatchesMigrationTests
{
    private const string Migration = "_DocumentBatches";

    private static readonly (string Table, string Column)[] Columns =
    [
        ("documents", "batch_id"),
        ("document_batches", "id"),
        ("document_batches", "company_id"),
        ("document_batches", "name"),
        ("document_batches", "status"),
        ("document_batches", "zip_key"),
        ("document_batches", "entries"),
        ("document_batches", "row_version"),
    ];

    [Test]
    public async Task The_batch_table_and_the_link_of_the_documents_are_added_and_removed()
    {
        await using var server = TestDatabase.CreateServer();
        await server.StartAsync();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = TestDatabase.ProviderName,
                ["Database:ConnectionString"] = server.GetConnectionString(),
            })
            .Build();
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddInfrastructure(configuration, TestDatabase.Providers);
        await using var services = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(null, userId: null, ActorType.System);
        var db = scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>();
        await db.Database.MigrateAsync();
        await new CatalogSynchronizer(db).SynchronizeAsync(CancellationToken.None);

        var migrations = db.Database.GetMigrations().ToList();
        var index = migrations.FindIndex(name => name.EndsWith(Migration, StringComparison.Ordinal));
        Assert.That(index, Is.GreaterThan(0), $"The {Migration} migration is missing in ImpiFact.Infrastructure.{TestDatabase.ProviderName}.");
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(migrations[index - 1]);
        var withoutColumns = await ColumnCountAsync(server.GetConnectionString());
        await migrator.MigrateAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(withoutColumns, Is.Zero, "Down removes the table and the column.");
            Assert.That(await ColumnCountAsync(server.GetConnectionString()), Is.EqualTo(Columns.Length));
        });
    }

    private static async Task<long> ColumnCountAsync(string connectionString)
    {
        await using var connection = TestDatabase.Connect(connectionString);
        var schema = TestDatabase.IsSqlServer ? "'dbo'" : TestDatabase.IsMySql ? "database()" : "'public'";
        var pairs = string.Join(" or ", Columns.Select(c => $"(table_name = '{c.Table}' and column_name = '{c.Column}')"));
        return await connection.QuerySingleAsync<long>(
            $"select count(*) from information_schema.columns where table_schema = {schema} and ({pairs})");
    }
}
