namespace ImpiFact.Integration.Tests.Model;

/// <summary>
/// Ningún secreto en claro en la base (Modelo de datos detallado, criterio 7 de la sección 11): en
/// <c>company_software</c>, <c>company_mail_settings</c> y <c>api_keys</c>, toda columna que nombra un secreto
/// (PIN, contraseña, token, llave) guarda su cifrado (<c>*_cipher</c>) o su resumen (<c>*_hash</c>).
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class SecretColumnTests
{
    private static readonly string[] SecretWords = ["pin", "password", "secret", "token", "key", "pass"];

    /// <summary>Columnas que contienen una de esas palabras sin ser secretas.</summary>
    private static readonly string[] NotSecret =
    [
        "api_keys.key_prefix", // inicio visible de la clave para reconocerla en el panel
    ];

    [TestCase("company_software", "pin_cipher")]
    [TestCase("company_mail_settings", "password_cipher")]
    [TestCase("api_keys", "key_hash")]
    public async Task Secrets_are_stored_only_as_cipher_or_hash(string table, string protectedColumn)
    {
        var columns = await Sql.QueryAsync<string>(
            "select column_name from information_schema.columns where table_schema = @schema and table_name = @table",
            new { schema = TestDatabase.DefaultSchema, table });

        var plaintext = columns.Where(column =>
            SecretWords.Any(word => column.Split('_').Contains(word, StringComparer.Ordinal))
            && !column.EndsWith("_cipher", StringComparison.Ordinal)
            && !column.EndsWith("_hash", StringComparison.Ordinal)
            && !NotSecret.Contains($"{table}.{column}"));

        Assert.Multiple(() =>
        {
            Assert.That(columns, Does.Contain(protectedColumn));
            Assert.That(plaintext, Is.Empty);
        });
    }
}
