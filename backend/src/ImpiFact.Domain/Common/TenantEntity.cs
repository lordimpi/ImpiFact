namespace ImpiFact.Domain.Common;

/// <summary>
/// Fila de una empresa sin llave propia (por ejemplo, una relación o una configuración de una sola fila por
/// empresa). Lleva las columnas comunes de las tablas de tenant (tenant: cada empresa cliente con sus datos
/// aislados; Modelo de datos detallado, sección 2): <c>company_id</c>, <c>created_at</c>, <c>updated_at</c> y
/// <c>row_version</c>. El contexto de base de datos llena las tres últimas al guardar.
/// </summary>
public abstract class TenantRecord : ITenantOwned, IAuditable, IVersioned
{
    public Guid CompanyId { get; init; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public long RowVersion { get; private set; }
}

/// <summary>
/// Fila de una empresa con llave propia <c>id uuid</c>. La llave es un uuid v7 (identificador único que empieza
/// por la hora) que genera la aplicación al agregar la fila si viene vacía.
/// </summary>
public abstract class TenantEntity : TenantRecord
{
    public Guid Id { get; init; }
}

/// <summary>
/// Fila administrativa de la plataforma, sin <c>company_id</c> (revendedores, usuarios, empresas). Guarda las
/// fechas de creación y cambio y la versión de fila, como las tablas de Laravel del PHP con sus marcas de tiempo.
/// </summary>
public abstract class PlatformEntity : IAuditable, IVersioned
{
    public Guid Id { get; init; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public long RowVersion { get; private set; }
}
