using Dapper;
using ImpiFact.Application.Abstractions;
using ImpiFact.Domain.Identity;
using ImpiFact.Infrastructure;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Persistence.Seeding;
using ImpiFact.Infrastructure.Tenancy;
using ImpiFact.Integration.Tests.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.IdentityData;

/// <summary>
/// The <c>UserCompanyActive</c> migration adds <c>user_companies.is_active</c>: every membership that existed before
/// the migration comes out active, on each database engine. It runs on its own server because it migrates down and up.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class UserCompanyActiveMigrationTests
{
    private const string Migration = "_UserCompanyActive";

    [Test]
    public async Task Memberships_that_existed_before_the_migration_come_out_active()
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

        var companyId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        db.Add(ModelGraph.NewCompany(companyId, "900765432"));
        db.Add(new User { Id = userId, Email = $"{userId:N}@example.com", PasswordHash = "x", Name = "Antes", SecurityStamp = "s" });
        db.Add(new UserCompany { UserId = userId, CompanyId = companyId, RoleId = PlatformSeed.SellerRoleId });
        await db.SaveChangesAsync();

        var migrations = db.Database.GetMigrations().ToList();
        var index = migrations.FindIndex(name => name.EndsWith(Migration, StringComparison.Ordinal));
        Assert.That(index, Is.GreaterThan(0), $"The {Migration} migration is missing in ImpiFact.Infrastructure.{TestDatabase.ProviderName}.");
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(migrations[index - 1]);
        var withoutColumn = await ColumnCountAsync(server.GetConnectionString());
        await migrator.MigrateAsync();

        await using var connection = TestDatabase.Connect(server.GetConnectionString());
        var isActive = await connection.QuerySingleAsync<bool>("select is_active from user_companies where user_id = @userId", new { userId });
        Assert.Multiple(async () =>
        {
            Assert.That(withoutColumn, Is.Zero, "Down removes the column.");
            Assert.That(await ColumnCountAsync(server.GetConnectionString()), Is.EqualTo(1));
            Assert.That(isActive, Is.True);
        });
    }

    private static async Task<long> ColumnCountAsync(string connectionString)
    {
        await using var connection = TestDatabase.Connect(connectionString);
        var schema = TestDatabase.IsSqlServer ? "'dbo'" : TestDatabase.IsMySql ? "database()" : "'public'";
        return await connection.QuerySingleAsync<long>(
            "select count(*) from information_schema.columns " +
            $"where table_schema = {schema} and table_name = 'user_companies' and column_name = 'is_active'");
    }
}
