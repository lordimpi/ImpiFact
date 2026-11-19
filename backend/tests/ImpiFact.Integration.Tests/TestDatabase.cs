using System.Data.Common;
using DotNet.Testcontainers.Containers;
using ImpiFact.Infrastructure.MySql;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Postgres;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Npgsql;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;

namespace ImpiFact.Integration.Tests;

/// <summary>
/// Motor de base de datos de las pruebas, elegido por configuración como en la API, el Worker y el Migrator: los hosts de
/// prueba registran todos los motores disponibles (<see cref="Providers"/>) y escriben <see cref="ProviderName"/> en
/// <c>Database:Provider</c>. Las pruebas que levantan la API leen la misma variable de entorno por su configuración.
/// </summary>
/// <remarks>
/// Para correr las pruebas con otro motor se define la variable de entorno <c>Database__Provider</c> (por ejemplo
/// <c>MySql</c>). Sin ella se usa PostgreSQL. Cada prueba crea su contenedor con <see cref="CreateContainer"/> y, si
/// necesita SQL directo, abre la conexión con <see cref="OpenConnectionAsync"/>.
/// </remarks>
public static class TestDatabase
{
    /// <summary>Variable de entorno con el motor; es la misma clave <c>Database:Provider</c> de los hosts.</summary>
    public const string ProviderVariable = "Database__Provider";

    private const string DefaultProvider = "Postgres";

    /// <summary>Todos los motores con proyecto en la solución, igual que en los hosts.</summary>
    public static IDatabaseProvider[] Providers =>
    [
        new PostgresDatabaseProvider(),
        new MySqlDatabaseProvider(),
    ];

    /// <summary>Nombre del motor con que corren las pruebas.</summary>
    public static string ProviderName =>
        Environment.GetEnvironmentVariable(ProviderVariable) is { Length: > 0 } name ? name.Trim() : DefaultProvider;

    /// <summary>Si las pruebas corren con MySQL.</summary>
    public static bool IsMySql => string.Equals(ProviderName, "MySql", StringComparison.OrdinalIgnoreCase);

    /// <summary>Si las pruebas corren con PostgreSQL.</summary>
    public static bool IsPostgres => string.Equals(ProviderName, DefaultProvider, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Fuente de trazas del controlador del motor de las pruebas: «Npgsql» o «MySqlConnector». Cada controlador emite una
    /// actividad por comando por sí mismo.
    /// </summary>
    public static string DriverActivitySource => IsMySql ? MySqlDatabaseProvider.DriverActivitySourceName : "Npgsql";

    /// <summary>
    /// Marca como omitida (con el motivo, visible en el resultado) una prueba que solo tiene sentido en PostgreSQL, por
    /// ejemplo porque revisa una migración que solo existe allí o lee su catálogo (<c>pg_*</c>).
    /// </summary>
    public static void RequirePostgres(string reason)
    {
        if (!IsPostgres)
        {
            Assert.Ignore($"PostgreSQL only: {reason}");
        }
    }

    /// <summary>Un contenedor desechable del motor de las pruebas, sin arrancar.</summary>
    public static TestDatabaseContainer CreateContainer() => IsMySql
        // root para que las pruebas puedan crear bases y leer performance_schema, como el dueño de la base de PostgreSQL.
        ? new TestDatabaseContainer(new MySqlBuilder("mysql:8.4")
            .WithUsername("root")
            // Datos en memoria y sin esperar al disco en cada confirmación: las migraciones (cientos de CREATE TABLE e
            // INDEX) pasan de minutos a segundos. Es solo para el contenedor de pruebas, que se borra al terminar.
            .WithTmpfsMount("/var/lib/mysql")
            .WithCommand("--skip-log-bin", "--innodb-flush-log-at-trx-commit=0", "--innodb-doublewrite=OFF", "--sync-binlog=0")
            .Build())
        : new TestDatabaseContainer(new PostgreSqlBuilder("postgres:18.1-alpine").Build());

    /// <summary>
    /// Una conexión sin abrir con el controlador del motor de las pruebas, para SQL escrito en la prueba (Dapper la abre
    /// y la cierra sola). En MySQL lee los uuid <c>char(36)</c> como <see cref="Guid"/> y las fechas como UTC, igual que
    /// el proveedor.
    /// </summary>
    public static DbConnection CreateConnection(string connectionString) => IsMySql
        ? new MySqlConnection(new MySqlConnectionStringBuilder(connectionString)
        {
            GuidFormat = MySqlGuidFormat.Char36,
            DateTimeKind = MySqlDateTimeKind.Utc,
        }.ConnectionString)
        : new NpgsqlConnection(connectionString);

    /// <summary>Una conexión abierta con el controlador del motor de las pruebas.</summary>
    public static async Task<DbConnection> OpenConnectionAsync(string connectionString)
    {
        var connection = CreateConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }

    /// <summary>La misma cadena de conexión apuntando a otra base del mismo servidor.</summary>
    public static string WithDatabase(string connectionString, string database) => IsMySql
        ? new MySqlConnectionStringBuilder(connectionString) { Database = database }.ConnectionString
        : new NpgsqlConnectionStringBuilder(connectionString) { Database = database }.ConnectionString;

    /// <summary>
    /// Opciones de un contexto de EF Core de prueba conectado al motor de las pruebas, sin los ajustes del modelo de cada
    /// proveedor (para contextos con pocas tablas).
    /// </summary>
    public static DbContextOptions<TContext> ContextOptions<TContext>(string connectionString)
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>();
        if (IsMySql)
        {
            // Las dos opciones que exige el proveedor de EF de MySQL (el de la aplicación las pone en su origen de datos).
            var mysql = new MySqlConnectionStringBuilder(connectionString) { AllowUserVariables = true, UseAffectedRows = false };
            options.UseMySql(mysql.ConnectionString, new MySqlServerVersion(new Version(8, 4, 0)));
        }
        else
        {
            options.UseNpgsql(connectionString);
        }

        return options.Options;
    }

    /// <summary>Crea otra base vacía en el mismo servidor y devuelve su cadena de conexión.</summary>
    /// <param name="name">Nombre de la base: letras minúsculas, dígitos y guion bajo.</param>
    public static async Task<string> CreateDatabaseAsync(string connectionString, string name)
    {
        await using (var connection = await OpenConnectionAsync(connectionString))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"create database {name}";
            await command.ExecuteNonQueryAsync();
        }

        return WithDatabase(connectionString, name);
    }

    /// <summary>Restricción violada si el error es de un índice único o una llave foránea; nulo si es otro error.</summary>
    public static string? ViolatedConstraint(Exception? exception) => exception?.InnerException switch
    {
        PostgresException { SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation } postgres =>
            postgres.ConstraintName,
        // MySQL no trae el nombre aparte: lo dice el mensaje, «… for key 'documents.ux_documents_cufe'».
        MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry } mysql => KeyName(mysql.Message),
        _ => null,
    };

    /// <summary>Si el error es la violación de una llave foránea (una referencia a una fila que no existe).</summary>
    public static bool IsForeignKeyViolation(Exception? exception) => exception?.InnerException switch
    {
        PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation } => true,
        MySqlException { ErrorCode: MySqlErrorCode.NoReferencedRow2 or MySqlErrorCode.RowIsReferenced2 } => true,
        _ => false,
    };

    private static string? KeyName(string message)
    {
        var start = message.LastIndexOf("key '", StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        var key = message[(start + 5)..].TrimEnd('\'');
        return key[(key.LastIndexOf('.') + 1)..];
    }
}

/// <summary>
/// Trozos de SQL de las pruebas que cambian de un motor a otro, como <c>ISqlDialect</c> en la aplicación. Solo para el SQL
/// que escriben las propias pruebas para preparar o revisar datos.
/// </summary>
public static class TestSql
{
    /// <summary>La expresión como texto: <c>x::text</c> en PostgreSQL, <c>cast(x as char)</c> en MySQL (un uuid o un JSON).</summary>
    public static string Text(string expression) => TestDatabase.IsMySql ? $"cast({expression} as char)" : $"{expression}::text";

    /// <summary>Texto de una propiedad de primer nivel de una columna JSON.</summary>
    public static string JsonText(string column, string property) =>
        TestDatabase.IsMySql ? $"{column} ->> '$.{property}'" : $"{column} ->> '{property}'";

    /// <summary>
    /// El esquema de las tablas de la aplicación para <c>information_schema</c>: <c>public</c> en PostgreSQL, la base de la
    /// conexión en MySQL (allí esquema y base son lo mismo).
    /// </summary>
    public static string CurrentSchema => TestDatabase.IsMySql ? "database()" : "'public'";

    /// <summary>El instante de hace <paramref name="minutes"/> minutos en el reloj del servidor (UTC).</summary>
    public static string MinutesAgo(int minutes) => TestDatabase.IsMySql
        ? $"utc_timestamp(6) - interval {minutes} minute"
        : $"now() - interval '{minutes} minute'";
}

/// <summary>Un contenedor de base de datos del motor de las pruebas (PostgreSQL o MySQL).</summary>
public sealed class TestDatabaseContainer(IContainer container) : IAsyncDisposable
{
    public Task StartAsync(CancellationToken ct = default) => container.StartAsync(ct);

    public string GetConnectionString() => ((IDatabaseContainer)container).GetConnectionString();

    public ValueTask DisposeAsync() => container.DisposeAsync();
}
