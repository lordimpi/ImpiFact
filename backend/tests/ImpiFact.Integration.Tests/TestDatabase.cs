using System.Data.Common;
using System.Text.RegularExpressions;
using ImpiFact.Infrastructure.Options;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Postgres;
using ImpiFact.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;

namespace ImpiFact.Integration.Tests;

/// <summary>
/// Motor de base de datos de las pruebas, elegido por configuración como en la API, el Worker y el Migrator: los hosts de
/// prueba registran todos los motores disponibles (<see cref="Providers"/>) y escriben <see cref="ProviderName"/> en
/// <c>Database:Provider</c>. Las pruebas que levantan la API leen la misma variable de entorno por su configuración.
/// </summary>
/// <remarks>
/// Para correr las pruebas con otro motor se define la variable de entorno <c>Database__Provider</c> (por ejemplo
/// <c>SqlServer</c>). Sin ella se usa PostgreSQL. Cada prueba pide su servidor con <see cref="CreateServer"/>, que es del
/// motor elegido, y abre sus conexiones directas con <see cref="Connect"/>.
/// </remarks>
public static partial class TestDatabase
{
    /// <summary>Variable de entorno con el motor; es la misma clave <c>Database:Provider</c> de los hosts.</summary>
    public const string ProviderVariable = "Database__Provider";

    private const string DefaultProvider = "Postgres";

    /// <summary>Todos los motores con proyecto en la solución, igual que en los hosts.</summary>
    public static IDatabaseProvider[] Providers =>
    [
        new PostgresDatabaseProvider(),
        new SqlServerDatabaseProvider(),
    ];

    /// <summary>El final del mensaje con que los hosts rechazan un <c>Database:Provider</c> que falta o no existe.</summary>
    public static string AvailableProvidersMessage => $"Available providers: {string.Join(", ", Providers.Select(provider => provider.Name))}.";

    /// <summary>Nombre del motor con que corren las pruebas.</summary>
    public static string ProviderName =>
        Environment.GetEnvironmentVariable(ProviderVariable) is { Length: > 0 } name ? name.Trim() : DefaultProvider;

    /// <summary>El motor con que corren las pruebas.</summary>
    public static IDatabaseProvider Provider =>
        Providers.Single(provider => string.Equals(provider.Name, ProviderName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Si las pruebas corren con SQL Server.</summary>
    public static bool IsSqlServer => Is("SqlServer");

    /// <summary>
    /// Cadena de conexión a un puerto donde nunca hay un servidor (el 1 de la propia máquina), en el formato del motor
    /// elegido y con un plazo de conexión de un segundo.
    /// </summary>
    public static string UnreachableConnectionString => IsSqlServer
        ? "Server=127.0.0.1,1;Database=impifact_unreachable;User Id=impifact;Password=unreachable;Connect Timeout=1;TrustServerCertificate=True"
        : "Host=127.0.0.1;Port=1;Database=impifact_unreachable;Timeout=1";

    /// <summary>Un servidor desechable del motor elegido, todavía sin arrancar.</summary>
    public static TestDatabaseServer CreateServer() => IsSqlServer ? new SqlServerTestServer() : new PostgresTestServer();

    /// <summary>Una conexión cerrada del motor elegido, para leer o preparar datos con SQL directo (Dapper la abre sola).</summary>
    public static DbConnection Connect(string connectionString) =>
        IsSqlServer ? new SqlConnection(connectionString) : new NpgsqlConnection(connectionString);

    /// <summary>
    /// Opciones de un contexto de EF Core (Entity Framework) conectado como en producción, con el personalizador del
    /// modelo del motor, para los contextos de prueba que se arman sin los servicios de la aplicación. Los servicios del
    /// motor (por ejemplo el origen de datos de Npgsql) viven lo que dura la corrida.
    /// </summary>
    public static DbContextOptions<TContext> ContextOptions<TContext>(string connectionString)
        where TContext : DbContext
    {
        var services = new ServiceCollection();
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new DatabaseOptions { Provider = ProviderName, ConnectionString = connectionString }));
        Provider.AddServices(services);
        var builder = new DbContextOptionsBuilder<TContext>();
        Provider.ConfigureDbContext(services.BuildServiceProvider(), builder);
        return builder.Options;
    }

    /// <summary>Esquema donde EF Core crea las tablas: <c>public</c> en PostgreSQL, <c>dbo</c> en SQL Server.</summary>
    public static string DefaultSchema => IsSqlServer ? "dbo" : "public";

    /// <summary>El instante actual en el SQL directo de las pruebas: <c>now()</c> o <c>sysdatetimeoffset()</c>.</summary>
    public static string Now => IsSqlServer ? "sysdatetimeoffset()" : "now()";

    /// <summary>Un parámetro de texto JSON para una columna JSON: <c>jsonb</c> en PostgreSQL, texto en SQL Server.</summary>
    public static string Json(string parameter) => IsSqlServer ? parameter : $"cast({parameter} as jsonb)";

    /// <summary>
    /// Un instante leído con SQL directo, en UTC: Npgsql devuelve <c>timestamptz</c> como <see cref="DateTime"/> UTC y
    /// SqlClient devuelve <c>datetimeoffset</c> como <see cref="DateTimeOffset"/>.
    /// </summary>
    public static DateTimeOffset Instant(object value) => value switch
    {
        DateTimeOffset instant => instant.ToUniversalTime(),
        DateTime utc => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)),
        _ => throw new ArgumentException($"{value.GetType()} is not an instant.", nameof(value)),
    };

    /// <summary>
    /// Una columna de instante para leerla como <see cref="DateTime"/> UTC con Dapper, como lo hace Infrastructure con
    /// <c>ISqlDialect.Instant</c>: SqlClient entrega <c>datetimeoffset</c>, que Dapper no convierte a <c>DateTime</c>.
    /// </summary>
    public static string Utc(string column) => IsSqlServer ? $"cast({column} as datetime2)" : column;

    /// <summary>Texto de una propiedad de primer nivel de una columna JSON.</summary>
    public static string JsonText(string column, string property) =>
        IsSqlServer ? $"json_value({column}, '$.{property}')" : $"{column} ->> '{property}'";

    /// <summary>Un uuid como texto en minúsculas con guiones, como lo escribe .NET.</summary>
    public static string UuidText(string column) => IsSqlServer ? $"lower(convert(nvarchar(36), {column}))" : $"{column}::text";

    /// <summary>
    /// El orden en que la base ordena los uuid (el desempate de los listados por clave): PostgreSQL compara sus 16 bytes
    /// de izquierda a derecha, como el texto en hexadecimal; SQL Server compara un uniqueidentifier empezando por sus
    /// últimos 6 bytes (<see cref="System.Data.SqlTypes.SqlGuid"/> sigue la misma regla).
    /// </summary>
    public static IComparer<Guid> IdOrder => IsSqlServer
        ? Comparer<Guid>.Create((left, right) => new System.Data.SqlTypes.SqlGuid(left).CompareTo(new System.Data.SqlTypes.SqlGuid(right)))
        : Comparer<Guid>.Create((left, right) => string.CompareOrdinal(left.ToString("N"), right.ToString("N")));

    /// <summary>Un nombre de columna que es palabra reservada en SQL Server, como <c>percent</c>.</summary>
    public static string Identifier(string name) => IsSqlServer ? $"[{name}]" : name;

    /// <summary>Solo la primera fila, al final de la consulta (después del <c>order by</c>).</summary>
    public static string First => IsSqlServer ? "offset 0 rows fetch next 1 rows only" : "limit 1";

    /// <summary>El instante de hace <paramref name="minutes"/> minutos en el SQL directo de las pruebas.</summary>
    public static string MinutesAgo(int minutes) =>
        IsSqlServer ? $"dateadd(minute, -{minutes}, sysdatetimeoffset())" : $"now() - interval '{minutes} minute'";

    /// <summary>Literal booleano para el SQL directo de las pruebas (SQL Server no tiene <c>true</c> ni <c>false</c>).</summary>
    public static string Bool(bool value) => IsSqlServer ? (value ? "1" : "0") : (value ? "true" : "false");

    /// <summary>
    /// Nombre del índice o la restricción única que rechazó el guardado, o nulo si el error es otro. PostgreSQL lo da en
    /// <c>ConstraintName</c>; SQL Server solo en el mensaje (errores 2601 y 2627).
    /// </summary>
    public static string? UniqueViolationName(DbUpdateException? exception) => exception?.InnerException switch
    {
        PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres => postgres.ConstraintName,
        SqlException { Number: 2601 or 2627 } sql => SqlServerObjectName().Match(sql.Message) is { Success: true } match ? match.Groups["name"].Value : null,
        _ => null,
    };

    /// <summary>Si el guardado falló por una llave foránea: 23503 en PostgreSQL, 547 con «FOREIGN KEY» en SQL Server.</summary>
    public static bool IsForeignKeyViolation(DbUpdateException? exception) => exception?.InnerException switch
    {
        PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation } => true,
        SqlException { Number: 547 } sql => sql.Message.Contains("FOREIGN KEY", StringComparison.Ordinal),
        _ => false,
    };

    private static bool Is(string provider) => string.Equals(ProviderName, provider, StringComparison.OrdinalIgnoreCase);

    // «… with unique index 'ux_documents_cufe'.» (2601) o «Violation of UNIQUE KEY constraint 'ux_…'.» (2627).
    [GeneratedRegex(@"(?:unique index|constraint) '(?<name>[^']+)'")]
    private static partial Regex SqlServerObjectName();

    /// <summary>
    /// PostgreSQL en su propio contenedor de Docker, con la misma imagen que <c>deploy/docker-compose.yml</c> y un puerto
    /// al azar, así no choca con un PostgreSQL local.
    /// </summary>
    private sealed class PostgresTestServer : TestDatabaseServer
    {
        private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:18.1-alpine").Build();

        public override Task StartAsync() => container.StartAsync();

        public override string GetConnectionString() => container.GetConnectionString();

        public override string ConnectionStringFor(string database) =>
            new NpgsqlConnectionStringBuilder(GetConnectionString()) { Database = database }.ConnectionString;

        public override async Task CreateDatabaseAsync(string database)
        {
            await using var connection = new NpgsqlConnection(GetConnectionString());
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"create database {database}", connection);
            await command.ExecuteNonQueryAsync();
        }

        public override ValueTask DisposeAsync() => container.DisposeAsync();
    }

    /// <summary>
    /// Una base propia en un SQL Server compartido por toda la corrida: el contenedor tarda varios segundos en arrancar y
    /// pide unos 2 GB de memoria, así que se levanta una sola vez y cada servidor de prueba crea su base. Testcontainers
    /// lo elimina al terminar el proceso.
    /// </summary>
    /// <remarks>
    /// Cada base se crea con <c>READ_COMMITTED_SNAPSHOT ON</c>, como la deja la migración inicial: una lectura no espera a
    /// quien escribe, igual que en PostgreSQL.
    /// </remarks>
    private sealed class SqlServerTestServer : TestDatabaseServer
    {
        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static MsSqlContainer? shared;

        private readonly string database = "impifact_" + Guid.NewGuid().ToString("N");
        private string? master;

        public override async Task StartAsync()
        {
            master = (await SharedAsync()).GetConnectionString();
            await CreateDatabaseAsync(database);
        }

        public override string GetConnectionString() => ConnectionStringFor(database);

        public override string ConnectionStringFor(string name) =>
            new SqlConnectionStringBuilder(master ?? throw new InvalidOperationException("The SQL Server test server is not started."))
            {
                InitialCatalog = name,
            }.ConnectionString;

        public override async Task CreateDatabaseAsync(string name)
        {
            await using var connection = new SqlConnection(master);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE [{name}]; ALTER DATABASE [{name}] SET READ_COMMITTED_SNAPSHOT ON;";
            await command.ExecuteNonQueryAsync();
        }

        // La base queda en el contenedor compartido hasta el final de la corrida; solo se sueltan las conexiones abiertas.
        public override ValueTask DisposeAsync()
        {
            if (master is not null)
            {
                using var connection = new SqlConnection(GetConnectionString());
                SqlConnection.ClearPool(connection);
            }

            return ValueTask.CompletedTask;
        }

        private static async Task<MsSqlContainer> SharedAsync()
        {
            await Gate.WaitAsync();
            try
            {
                if (shared is null)
                {
                    var container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
                    await container.StartAsync();
                    shared = container;
                }

                return shared;
            }
            finally
            {
                Gate.Release();
            }
        }
    }
}

/// <summary>
/// Un servidor de base de datos desechable para las pruebas, con la misma forma que un contenedor de Testcontainers:
/// se arranca, da su cadena de conexión y se desecha al terminar.
/// </summary>
public abstract class TestDatabaseServer : IAsyncDisposable
{
    public abstract Task StartAsync();

    /// <summary>Cadena de conexión a la base de este servidor, que ya existe y está vacía.</summary>
    public abstract string GetConnectionString();

    /// <summary>Cadena de conexión a otra base del mismo servidor, exista o no.</summary>
    public abstract string ConnectionStringFor(string database);

    /// <summary>Crea una base vacía en el mismo servidor.</summary>
    public abstract Task CreateDatabaseAsync(string database);

    public abstract ValueTask DisposeAsync();
}
