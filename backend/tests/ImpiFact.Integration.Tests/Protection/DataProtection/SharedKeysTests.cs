using System.Security.Cryptography;
using Dapper;
using ImpiFact.Application.Abstractions.Protection;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.Protection.DataProtection;

/// <summary>
/// <see cref="ISecretProtector"/> sobre Data Protection con las llaves en la tabla <c>data_protection_keys</c>
/// (Arquitectura detallada, sección 19.2; ADR-25). Varias instancias de la API comparten las llaves a través de
/// la base, y las llaves quedan cifradas con la llave maestra local.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class SharedKeysTests
{
    private ProtectionScratch scratch = null!;
    private string connectionString = null!;

    [SetUp]
    public async Task CreateDatabase()
    {
        scratch = new ProtectionScratch();
        connectionString = await KeysDatabase.CreateDatabaseAsync();
    }

    [TearDown]
    public void DeleteScratch() => scratch.Dispose();

    [Test]
    public void A_secret_protected_by_one_instance_is_read_by_another_that_shares_the_database()
    {
        string cipher;
        using (var first = Instance())
        {
            cipher = first.GetRequiredService<ISecretProtector>().Protect(SecretPurposes.SoftwarePin, "12345");
        }

        using var second = Instance();
        var plain = second.GetRequiredService<ISecretProtector>().Unprotect(SecretPurposes.SoftwarePin, cipher);

        Assert.Multiple(() =>
        {
            Assert.That(plain, Is.EqualTo("12345"));
            Assert.That(cipher, Does.Not.Contain("12345"));
        });
    }

    [Test]
    public async Task Keys_are_persisted_in_the_table_encrypted_with_the_local_master_key()
    {
        using (var instance = Instance())
        {
            instance.GetRequiredService<ISecretProtector>().Protect(SecretPurposes.SmtpPassword, "smtp-password");
        }

        await using var connection = TestDatabase.Connect(connectionString);
        var rows = (await connection.QueryAsync<(string? FriendlyName, string Xml)>(
            "select friendly_name, xml from data_protection_keys")).ToList();

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(rows[0].FriendlyName, Does.StartWith("key-"));
            Assert.That(rows[0].Xml, Does.Contain("encryptedSecret").And.Contain("LocalAesGcmXmlDecryptor"));
            Assert.That(rows[0].Xml, Does.Not.Contain("<masterKey"), "The key material must not be stored in clear.");
        });
    }

    [Test]
    public void An_instance_with_a_different_master_key_cannot_read_the_keys()
    {
        string cipher;
        using (var first = Instance())
        {
            cipher = first.GetRequiredService<ISecretProtector>().Protect(SecretPurposes.SoftwarePin, "12345");
        }

        var otherKey = scratch.WriteMasterKey("other.key", RandomNumberGenerator.GetBytes(32));
        using var intruder = Instance(otherKey);

        Assert.Throws(Is.InstanceOf<CryptographicException>(),
            () => intruder.GetRequiredService<ISecretProtector>().Unprotect(SecretPurposes.SoftwarePin, cipher));
    }

    [Test]
    public void A_secret_only_opens_with_the_purpose_it_was_protected_with()
    {
        using var instance = Instance();
        var protector = instance.GetRequiredService<ISecretProtector>();
        var cipher = protector.Protect(SecretPurposes.SoftwarePin, "12345");

        Assert.Throws(Is.InstanceOf<CryptographicException>(), () => protector.Unprotect(SecretPurposes.SmtpPassword, cipher));
    }

    [Test]
    public async Task The_table_matches_the_data_model()
    {
        await using var connection = TestDatabase.Connect(connectionString);
        // En MySQL information_schema ve todas las bases del servidor: se mira solo la de esta prueba. El largo de longtext
        // (4294967295) no cabe en un int y no interesa.
        var mysql = TestDatabase.IsMySql;
        var inThisDatabase = mysql ? " and table_schema = database()" : "";
        var length = mysql ? "if(data_type = 'longtext', null, character_maximum_length)" : "character_maximum_length";
        var columns = (await connection.QueryAsync<(string Column, string Type, int? Length, string Nullable)>(
                $"select column_name, data_type, {length}, is_nullable from information_schema.columns " +
                $"where table_name = 'data_protection_keys'{inThisDatabase} order by ordinal_position"))
            .ToList();
        var primaryKey = await connection.ExecuteScalarAsync<string>(
            "select constraint_name from information_schema.table_constraints " +
            $"where table_name = 'data_protection_keys' and constraint_type = 'PRIMARY KEY'{inThisDatabase}");

        Assert.Multiple(() =>
        {
            // En SQL Server el texto es nvarchar (-1 es nvarchar(max)), como el resto del modelo.
            // En MySQL el texto sin largo es longtext y la llave primaria siempre se llama PRIMARY.
            Assert.That(columns, Is.EquivalentTo(TestDatabase.IsSqlServer
                ? new (string, string, int?, string)[] { ("id", "int", null, "NO"), ("friendly_name", "nvarchar", 200, "YES"), ("xml", "nvarchar", -1, "YES") }
                : mysql
                    ? [("id", "int", null, "NO"), ("friendly_name", "varchar", 200, "YES"), ("xml", "longtext", null, "YES")]
                    : [("id", "integer", null, "NO"), ("friendly_name", "character varying", 200, "YES"), ("xml", "text", null, "YES")]));
            Assert.That(primaryKey, Is.EqualTo(mysql ? "PRIMARY" : "pk_data_protection_keys"));
        });
    }

    private ServiceProvider Instance(string? masterKeyFile = null) =>
        ProtectionServices.Build(ProtectionServices.ValidSettings(scratch.LocalAesGcmSettings(masterKeyFile), connectionString));
}
