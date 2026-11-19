using System.Text;
using Dapper;
using ImpiFact.Application.Abstractions.Persistence;
using ImpiFact.Application.Features.Masters;
using ImpiFact.Application.Features.Masters.Customers;
using ImpiFact.Application.Features.Masters.Products;
using ImpiFact.Domain.Masters;
using ImpiFact.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Infrastructure.Masters;

/// <summary>
/// Clientes (<c>customers</c>). El listado va con Dapper (una librería que ejecuta SQL escrito a mano) y lleva
/// <c>company_id</c> en el WHERE, porque los filtros globales de EF Core (Entity Framework) no aplican ahí; leer uno,
/// revisar la unicidad y escribir van con EF Core dentro de la transacción del caso de uso, con el filtro por empresa
/// además de la condición explícita.
/// </summary>
/// <remarks>
/// La página siguiente se pide por clave (columna de orden, id), con comparaciones que usan la misma intercalación que el
/// ORDER BY: no salta ni repite filas. Las columnas de orden salen de una lista fija, nunca del texto del cliente. El SQL
/// es común a los tres motores; lo que cambia (la búsqueda sin mayúsculas, el límite, los booleanos) lo pone el
/// dialecto del motor (<see cref="ISqlDialect"/>).
/// </remarks>
internal sealed class CustomerStore(IReadConnectionFactory connections, ImpiFactDbContext db, ISqlDialect dialect) : ICustomerStore
{
    private static readonly Dictionary<string, string> SortColumns = new(StringComparer.Ordinal)
    {
        ["name"] = "c.name",
        ["identification_number"] = "c.identification_number",
    };

    public async Task<IReadOnlyList<CustomerResponse>> ListAsync(MasterPage page, CancellationToken ct)
    {
        var parameters = new DynamicParameters(new { page.CompanyId, page.Take });
        var sql = new StringBuilder("""
            select c.id as Id, c.type_document_identification_id as TypeDocumentIdentificationId,
                   c.identification_number as IdentificationNumber, c.dv as Dv, c.name as Name, c.phone as Phone,
                   c.address as Address, c.email as Email, c.municipality_id as MunicipalityId,
                   c.type_organization_id as TypeOrganizationId, c.type_regime_id as TypeRegimeId,
                   c.type_liability_id as TypeLiabilityId, c.merchant_registration as MerchantRegistration, c.is_active as IsActive
            from customers c
            where c.company_id = @CompanyId and c.deleted_at is null
            """);
        MasterSql.AppendStatus(sql, dialect, "c", page.Status);
        if (page.Search is { } search)
        {
            sql.Append($" and ({dialect.LikeIgnoreCase("c.identification_number", "@Pattern")} or {dialect.LikeIgnoreCase("c.name", "@Pattern")})");
            parameters.Add("Pattern", MasterSql.ContainsPattern(search));
        }

        MasterSql.AppendKeyset(sql, parameters, dialect, SortColumns[page.SortField], "c.id", page);
        await using var connection = await connections.OpenAsync(ct);
        var rows = await connection.QueryAsync<CustomerResponse>(new CommandDefinition(sql.ToString(), parameters, cancellationToken: ct));
        return rows.AsList();
    }

    public Task<Customer?> FindAsync(Guid companyId, Guid id, CancellationToken ct) =>
        db.Set<Customer>().FirstOrDefaultAsync(customer => customer.CompanyId == companyId && customer.Id == id, ct);

    public Task<bool> IsIdentificationTakenAsync(
        Guid companyId, int typeDocumentIdentificationId, string identificationNumber, Guid? exceptId, CancellationToken ct) =>
        // El índice único cubre también las filas con borrado lógico: se miran todas.
        db.Set<Customer>().IgnoreQueryFilters([QueryFilters.SoftDelete])
            .AnyAsync(
                customer => customer.CompanyId == companyId
                    && customer.TypeDocumentIdentificationId == typeDocumentIdentificationId
                    && customer.IdentificationNumber == identificationNumber
                    && (exceptId == null || customer.Id != exceptId),
                ct);

    public void Add(Customer customer) => db.Set<Customer>().Add(customer);
}

/// <summary>Productos (<c>items</c>), con las mismas reglas que <see cref="CustomerStore"/>.</summary>
internal sealed class ProductStore(IReadConnectionFactory connections, ImpiFactDbContext db, ISqlDialect dialect) : IProductStore
{
    private static readonly Dictionary<string, string> SortColumns = new(StringComparer.Ordinal)
    {
        ["name"] = "i.name",
        ["code"] = "i.code",
    };

    public async Task<IReadOnlyList<ProductResponse>> ListAsync(MasterPage page, CancellationToken ct)
    {
        var parameters = new DynamicParameters(new { page.CompanyId, page.Take });
        var sql = new StringBuilder("""
            select i.id as Id, i.code as Code, i.name as Name, i.description as Description, i.unit_measure_id as UnitMeasureId,
                   i.type_item_identification_id as TypeItemIdentificationId, i.sale_unit_price as UnitPrice, i.tax_id as TaxId,
                   i.tax_percent as TaxPercent, i.tax_per_unit_amount as TaxPerUnitAmount, i.is_active as IsActive
            from items i
            where i.company_id = @CompanyId and i.deleted_at is null
            """);
        MasterSql.AppendStatus(sql, dialect, "i", page.Status);
        if (page.Search is { } search)
        {
            sql.Append($" and ({dialect.LikeIgnoreCase("i.code", "@Pattern")} or {dialect.LikeIgnoreCase("i.name", "@Pattern")})");
            parameters.Add("Pattern", MasterSql.ContainsPattern(search));
        }

        MasterSql.AppendKeyset(sql, parameters, dialect, SortColumns[page.SortField], "i.id", page);
        await using var connection = await connections.OpenAsync(ct);
        var rows = await connection.QueryAsync<ProductResponse>(new CommandDefinition(sql.ToString(), parameters, cancellationToken: ct));
        return rows.AsList();
    }

    public Task<Item?> FindAsync(Guid companyId, Guid id, CancellationToken ct) =>
        db.Set<Item>().FirstOrDefaultAsync(item => item.CompanyId == companyId && item.Id == id, ct);

    public Task<bool> IsCodeTakenAsync(Guid companyId, string code, Guid? exceptId, CancellationToken ct) =>
        db.Set<Item>().IgnoreQueryFilters([QueryFilters.SoftDelete])
            .AnyAsync(item => item.CompanyId == companyId && item.Code == code && (exceptId == null || item.Id != exceptId), ct);

    public void Add(Item item) => db.Set<Item>().Add(item);
}

/// <summary>Trozos de SQL que comparten los listados de maestros.</summary>
internal static class MasterSql
{
    public static void AppendStatus(StringBuilder sql, ISqlDialect dialect, string alias, MasterStatus status)
    {
        if (status != MasterStatus.All)
        {
            sql.Append($" and {alias}.is_active = {dialect.Bool(status == MasterStatus.Active)}");
        }
    }

    /// <summary>
    /// Agrega la condición de la página siguiente y el orden: lo que va después de la última fila mostrada, por (columna,
    /// id), de menor a mayor o al revés.
    /// </summary>
    /// <remarks>
    /// <c>(columna, id) &gt; (@AfterValue, @AfterId)</c> escrito sin comparación de filas, que SQL Server no tiene. La
    /// primera condición (<c>columna &gt;= @AfterValue</c>) es la que deja recorrer el índice desde el cursor; la segunda
    /// desempata por id las filas con el mismo valor. Las columnas de orden no admiten nulos.
    /// </remarks>
    public static void AppendKeyset(StringBuilder sql, DynamicParameters parameters, ISqlDialect dialect, string column, string id, MasterPage page)
    {
        var direction = page.Descending ? "desc" : "asc";
        if (page.After is { } after)
        {
            AppendAfter(sql, column, id, page.Descending ? "<" : ">", "@AfterValue", "@AfterId");
            parameters.Add("AfterValue", after.Value);
            parameters.Add("AfterId", after.Id);
        }

        sql.Append($" order by {column} {direction}, {id} {direction} {dialect.Paging("@Take")}");
    }

    /// <summary>
    /// Agrega <c>and (columna, id) &lt;operador&gt; (valor, id)</c> en la forma que entienden los tres motores, con
    /// <paramref name="comparison"/> «&lt;» o «&gt;».
    /// </summary>
    public static void AppendAfter(StringBuilder sql, string column, string id, string comparison, string value, string afterId) =>
        sql.Append($" and {column} {comparison}= {value} and ({column} {comparison} {value} or {id} {comparison} {afterId})");

    /// <summary>Patrón de ILIKE que busca el texto en cualquier parte, con los comodines del texto escapados.</summary>
    public static string ContainsPattern(string search) =>
        "%" + search.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal) + "%";
}
