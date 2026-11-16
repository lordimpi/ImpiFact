namespace ImpiFact.Domain.Common;

// Marcas que una entidad implementa para que el contexto de base de datos le aplique las reglas comunes del
// modelo (Modelo de datos detallado, sección 2). Solo piden lectura: el contexto y su interceptor escriben los
// valores a través de EF Core (Entity Framework, el mapeador de objetos a tablas), así la entidad puede tener
// setters privados.

/// <summary>
/// Dato de una empresa (columna <c>company_id</c>). El contexto le aplica el filtro global de tenant: una
/// consulta solo ve las filas de la empresa de la solicitud y, sin empresa, no ve ninguna.
/// </summary>
public interface ITenantOwned
{
    Guid CompanyId { get; }
}

/// <summary>Fila con borrado lógico (columna <c>deleted_at</c>): se marca la fecha en lugar de borrarla y el filtro la oculta.</summary>
public interface ISoftDeletable
{
    DateTimeOffset? DeletedAt { get; }
}

/// <summary>Fila con fechas de creación y último cambio (<c>created_at</c>, <c>updated_at</c>), que llena el interceptor de guardado.</summary>
public interface IAuditable
{
    DateTimeOffset CreatedAt { get; }

    DateTimeOffset UpdatedAt { get; }
}

/// <summary>
/// Fila con versión (<c>row_version</c>) para la concurrencia optimista: cada guardado exige que la versión en la
/// base sea la que se leyó y la sube en uno. Si otro la cambió antes, el guardado falla en lugar de pisarlo.
/// </summary>
public interface IVersioned
{
    long RowVersion { get; }
}
