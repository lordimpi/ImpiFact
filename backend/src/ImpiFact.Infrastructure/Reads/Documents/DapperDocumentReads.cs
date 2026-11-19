using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Dapper;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Persistence;
using ImpiFact.Domain.Documents;
using ImpiFact.Infrastructure.Persistence.Conversions;

namespace ImpiFact.Infrastructure.Reads.Documents;

/// <summary>
/// Consultas de documentos con Dapper, una librería que ejecuta SQL escrito a mano y llena objetos (Modelo de datos
/// detallado, sección 6). Toda consulta lleva <c>company_id</c> en el WHERE: los filtros globales de EF Core no aplican
/// aquí y una prueba de arquitectura revisa cada cadena SQL. Índices que usan:
/// <list type="bullet">
/// <item>
/// Rangos de fecha, listados y conteos: <c>ix_documents_company_issued</c> (company_id, issued_at DESC). El listado del
/// panel recorre ese índice hacia atrás desde el cursor y se detiene al juntar la página; los demás filtros (tipo,
/// estados, adquiriente, número) se evalúan en las filas que recorre. Con filtros que casi no coinciden recorre más
/// filas de la empresa, pero nunca las de otra; el conteo (<c>include_total</c>) y la exportación recorren todas las que
/// cumplen el rango de fechas.
/// </item>
/// <item>Por CUFE: <c>ux_documents_cufe</c> (company_id, cufe). Por id: la clave primaria.</item>
/// <item>Impuestos y líneas de un documento: los índices (company_id, document_id) de cada tabla hija.</item>
/// </list>
/// </summary>
/// <remarks>
/// SQL de PostgreSQL, el único motor de esta fase: <c>limit</c> y <c>offset</c> cambian en SQL Server. Las listas
/// (<c>in (...)</c>) se arman con un parámetro por valor y no con la expansión de Dapper, que con Npgsql (el controlador
/// de PostgreSQL) envía un arreglo que <c>in</c> no acepta.
/// </remarks>
internal sealed class DapperDocumentReads(IReadConnectionFactory connections) : IDocumentReads
{
    /// <summary>Columnas de la cabecera; los alias son los nombres de <see cref="HeaderRow"/>.</summary>
    private const string HeaderColumns = """
        d.id as Id, d.document_type_id as DocumentTypeId, d.type_environment_id as TypeEnvironmentId, d.prefix as Prefix,
        d.number as Number, d.full_number as FullNumber, d.status as Status, d.cufe as Cufe, d.issue_date as IssueDate,
        d.issue_time as IssueTime, d.issued_at as IssuedAt, d.currency_id as CurrencyId, d.customer_snapshot as CustomerSnapshot,
        d.line_extension_amount as LineExtensionAmount, d.tax_exclusive_amount as TaxExclusiveAmount,
        d.tax_inclusive_amount as TaxInclusiveAmount, d.allowance_total_amount as AllowanceTotalAmount,
        d.charge_total_amount as ChargeTotalAmount, d.prepaid_amount as PrepaidAmount, d.payable_amount as PayableAmount,
        d.email_status as EmailStatus
        """;

    /// <summary>Columnas que solo trae el detalle.</summary>
    private const string DetailColumns = """
        d.uuid_algorithm as UuidAlgorithm, d.resolution_id as ResolutionId, d.reference_document_id as ReferenceDocumentId,
        d.type_operation_id as TypeOperationId, d.exchange_rate as ExchangeRate, d.discrepancy_code as DiscrepancyCode,
        d.discrepancy_description as DiscrepancyDescription, d.notes as Notes, d.head_note as HeadNote, d.foot_note as FootNote,
        d.track_id as TrackId, d.dian_status_code as DianStatusCode, d.dian_status_description as DianStatusDescription,
        d.sent_at as SentAt, d.accepted_at as AcceptedAt
        """;

    // La tabla companies no tiene company_id: su id es la empresa. El parámetro se llama company_id para que la regla
    // «toda consulta lleva company_id» se lea también aquí: solo devuelve la empresa de la credencial.
    private const string CompanySql = """
        select c.id as Id, c.identification_number as IdentificationNumber, c.name as Name
        from companies c
        where c.id = @company_id
        """;

    private const string TaxesSql = """
        select t.tax_id as TaxId, t.percent as Percent, t.taxable_amount as TaxableAmount, t.tax_amount as TaxAmount
        from document_taxes t
        where t.company_id = @CompanyId and t.document_id = @DocumentId
        order by t.tax_id, t.percent
        """;

    private const string LinesSql = """
        select l.id as Id, l.line_number as LineNumber, l.code as Code, l.description as Description, l.notes as Notes,
            l.type_item_identification_id as TypeItemIdentificationId, l.unit_measure_id as UnitMeasureId, l.quantity as Quantity,
            l.base_quantity as BaseQuantity, l.unit_price as UnitPrice, l.line_extension_amount as LineExtensionAmount,
            l.free_of_charge as FreeOfCharge, l.reference_price_id as ReferencePriceId
        from document_lines l
        where l.company_id = @CompanyId and l.document_id = @DocumentId
        order by l.line_number
        """;

    private const string LineTaxesSql = """
        select t.line_id as LineId, t.tax_id as TaxId, t.percent as Percent, t.taxable_amount as TaxableAmount,
            t.tax_amount as TaxAmount, t.per_unit_amount as PerUnitAmount, t.unit_measure_id as UnitMeasureId
        from document_line_taxes t
        where t.company_id = @CompanyId and t.document_id = @DocumentId
        order by t.tax_id, t.percent
        """;

    private const string NextNumberSql = """
        select ns.next_number
        from numbering_sequences ns
        where ns.company_id = @company_id and ns.scope_key = @scope_key
        """;

    public async Task<long?> PeekNextNumberAsync(Guid companyId, string scopeKey, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<long?>(
            new CommandDefinition(NextNumberSql, new { company_id = companyId, scope_key = scopeKey }, cancellationToken: ct));
    }

    public async Task<DocumentCompany?> FindCompanyAsync(Guid companyId, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<CompanyRow>(
            new CommandDefinition(CompanySql, new { company_id = companyId }, cancellationToken: ct));
        return row is null ? null : new DocumentCompany(row.Id, row.IdentificationNumber, row.Name);
    }

    public async Task<IReadOnlyList<DocumentSummary>> ListByTypeAsync(
        Guid companyId, int documentTypeId, IReadOnlyCollection<DocumentStatus> statuses, IssuedRange? range, PageWindow? page, CancellationToken ct)
    {
        if (statuses.Count == 0)
        {
            return [];
        }

        var parameters = new DynamicParameters(new { CompanyId = companyId, DocumentTypeId = documentTypeId });
        var sql = new StringBuilder($"""
            select {HeaderColumns}
            from documents d
            where d.company_id = @CompanyId and d.document_type_id = @DocumentTypeId
            """);
        sql.Append(" and d.status in ").Append(InList(parameters, "Status", statuses.Select(status => (short)status)));
        AppendRange(sql, parameters, range);
        sql.Append(" order by d.issued_at, d.id");
        if (page is { } window)
        {
            // Las páginas numeradas del contrato heredado no se pueden pedir por clave: son de a 100 y usan offset.
            sql.Append(" limit @Take offset @Skip");
            parameters.Add("Take", window.Take);
            parameters.Add("Skip", window.Skip);
        }

        return await QuerySummariesAsync(sql.ToString(), parameters, ct);
    }

    public async Task<IReadOnlyDictionary<int, long>> CountByTypeAsync(
        Guid companyId, IReadOnlyCollection<int> documentTypeIds, IReadOnlyCollection<DocumentStatus> statuses, IssuedRange range, CancellationToken ct)
    {
        if (documentTypeIds.Count == 0 || statuses.Count == 0)
        {
            return new Dictionary<int, long>();
        }

        var parameters = new DynamicParameters(new { CompanyId = companyId });
        var sql = new StringBuilder("""
            select d.document_type_id as DocumentTypeId, count(*) as Total
            from documents d
            where d.company_id = @CompanyId
            """);
        sql.Append(" and d.document_type_id in ").Append(InList(parameters, "Type", documentTypeIds));
        sql.Append(" and d.status in ").Append(InList(parameters, "Status", statuses.Select(status => (short)status)));
        AppendRange(sql, parameters, range);
        sql.Append(" group by d.document_type_id");

        await using var connection = await connections.OpenAsync(ct);
        var rows = await connection.QueryAsync<CountRow>(new CommandDefinition(sql.ToString(), parameters, cancellationToken: ct));
        return rows.ToDictionary(row => row.DocumentTypeId, row => row.Total);
    }

    public async Task<IReadOnlyList<DocumentSummary>> ListAsync(DocumentListFilter filter, CancellationToken ct)
    {
        var (sql, parameters) = ListSql(filter);
        return await QuerySummariesAsync(sql, parameters, ct);
    }

    public async Task<long> CountAsync(DocumentListFilter filter, CancellationToken ct)
    {
        var parameters = new DynamicParameters(new { filter.CompanyId });
        var sql = new StringBuilder("""
            select count(*)
            from documents d
            where d.company_id = @CompanyId
            """);
        AppendListFilters(sql, parameters, filter with { After = null });
        await using var connection = await connections.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(sql.ToString(), parameters, cancellationToken: ct));
    }

    public async IAsyncEnumerable<DocumentSummary> StreamAsync(DocumentListFilter filter, [EnumeratorCancellation] CancellationToken ct)
    {
        var (sql, parameters) = ListSql(filter);
        await using var connection = await connections.OpenAsync(ct);
        await foreach (var row in connection.QueryUnbufferedAsync<HeaderRow>(sql, parameters).WithCancellation(ct))
        {
            yield return Summary(row);
        }
    }

    /// <summary>Consulta del listado: los filtros, el cursor, el orden <c>(issued_at, id)</c> descendente y el límite.</summary>
    private static (string Sql, DynamicParameters Parameters) ListSql(DocumentListFilter filter)
    {
        var parameters = new DynamicParameters(new { filter.CompanyId, filter.Take });
        var sql = new StringBuilder($"""
            select {HeaderColumns}
            from documents d
            where d.company_id = @CompanyId
            """);
        AppendListFilters(sql, parameters, filter);
        sql.Append(" order by d.issued_at desc, d.id desc limit @Take");
        return (sql.ToString(), parameters);
    }

    /// <summary>Condiciones del listado después de <c>company_id</c>; los filtros nulos o vacíos no agregan nada.</summary>
    private static void AppendListFilters(StringBuilder sql, DynamicParameters parameters, DocumentListFilter filter)
    {
        if (filter.DocumentTypeId is { } typeId)
        {
            sql.Append(" and d.document_type_id = @DocumentTypeId");
            parameters.Add("DocumentTypeId", typeId);
        }

        if (filter.Statuses.Count > 0)
        {
            sql.Append(" and d.status in ").Append(InList(parameters, "Status", filter.Statuses.Select(status => (short)status)));
        }

        if (filter.IssuedFrom is { } from)
        {
            sql.Append(" and d.issued_at >= @IssuedFrom");
            parameters.Add("IssuedFrom", from.ToUniversalTime());
        }

        if (filter.IssuedUntil is { } until)
        {
            sql.Append(" and d.issued_at < @IssuedUntil");
            parameters.Add("IssuedUntil", until.ToUniversalTime());
        }

        if (filter.Customer is { } customer)
        {
            // Busca en la copia del adquiriente que quedó en el documento (columna jsonb), sin distinguir mayúsculas. Los
            // comodines de LIKE que escriba el usuario (%, _) se escapan: se buscan como texto.
            sql.Append(" and (d.customer_snapshot ->> 'identification_number' ilike @Customer escape '\\'")
                .Append(" or d.customer_snapshot ->> 'name' ilike @Customer escape '\\')");
            parameters.Add("Customer", "%" + EscapeLike(customer) + "%");
        }

        if (filter.Number is { } number)
        {
            if (number.All(char.IsAsciiDigit) && long.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var digits))
            {
                // Solo dígitos: el número en cualquier prefijo (también el de un documento sin prefijo).
                sql.Append(" and d.number = @Number");
                parameters.Add("Number", digits);
            }
            else
            {
                sql.Append(" and upper(d.full_number) = @FullNumber");
                parameters.Add("FullNumber", number.ToUpperInvariant());
            }
        }

        if (filter.After is { } after)
        {
            // Siguiente página por clave (issued_at, id), del más reciente al más antiguo: lo que va después del último
            // documento mostrado. Escrito sin comparación de tuplas para que sirva en los tres motores.
            sql.Append(" and (d.issued_at < @AfterIssuedAt or (d.issued_at = @AfterIssuedAt and d.id < @AfterId))");
            parameters.Add("AfterIssuedAt", after.IssuedAt.ToUniversalTime());
            parameters.Add("AfterId", after.Id);
        }
    }

    /// <summary>Escapa los comodines de LIKE con la barra invertida.</summary>
    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);

    public Task<DocumentDetail?> FindByIdAsync(Guid companyId, Guid documentId, CancellationToken ct) =>
        FindDetailAsync($"""
            select {HeaderColumns}, {DetailColumns}
            from documents d
            where d.company_id = @CompanyId and d.id = @Key
            """, companyId, documentId, ct);

    public Task<DocumentDetail?> FindByCufeAsync(Guid companyId, string cufe, CancellationToken ct) =>
        FindDetailAsync($"""
            select {HeaderColumns}, {DetailColumns}
            from documents d
            where d.company_id = @CompanyId and d.cufe = @Key
            """, companyId, cufe, ct);

    private async Task<DocumentDetail?> FindDetailAsync(string headerSql, Guid companyId, object key, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        var parameters = new DynamicParameters(new { CompanyId = companyId });
        parameters.Add("Key", key);
        var header = await connection.QuerySingleOrDefaultAsync<HeaderRow>(new CommandDefinition(headerSql, parameters, cancellationToken: ct));
        if (header is null)
        {
            return null;
        }

        var children = new { CompanyId = companyId, DocumentId = header.Id };
        var taxes = await connection.QueryAsync<TaxRow>(new CommandDefinition(TaxesSql, children, cancellationToken: ct));
        var lines = await connection.QueryAsync<LineRow>(new CommandDefinition(LinesSql, children, cancellationToken: ct));
        var lineTaxes = (await connection.QueryAsync<TaxRow>(new CommandDefinition(LineTaxesSql, children, cancellationToken: ct)))
            .ToLookup(tax => tax.LineId);

        return new DocumentDetail(
            Summary(header),
            header.UuidAlgorithm,
            header.ResolutionId,
            header.ReferenceDocumentId,
            header.TypeOperationId,
            header.ExchangeRate,
            header.DiscrepancyCode,
            header.DiscrepancyDescription,
            header.Notes,
            header.HeadNote,
            header.FootNote,
            header.TrackId,
            header.DianStatusCode,
            header.DianStatusDescription,
            Utc(header.SentAt),
            Utc(header.AcceptedAt),
            taxes.Select(Tax).ToList(),
            lines.Select(line => new DocumentLineDetail(
                line.LineNumber, line.Code, line.Description, line.Notes, line.TypeItemIdentificationId, line.UnitMeasureId,
                line.Quantity, line.BaseQuantity, line.UnitPrice, line.LineExtensionAmount, line.FreeOfCharge, line.ReferencePriceId,
                lineTaxes[line.Id].Select(Tax).ToList())).ToList());
    }

    private async Task<IReadOnlyList<DocumentSummary>> QuerySummariesAsync(string sql, DynamicParameters parameters, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        var rows = await connection.QueryAsync<HeaderRow>(new CommandDefinition(sql, parameters, cancellationToken: ct));
        return rows.Select(Summary).ToList();
    }

    /// <summary>Rango de emisión en UTC: desde incluido, hasta excluido.</summary>
    private static void AppendRange(StringBuilder sql, DynamicParameters parameters, IssuedRange? range)
    {
        if (range is not { } issued)
        {
            return;
        }

        sql.Append(" and d.issued_at >= @IssuedFrom and d.issued_at < @IssuedUntil");
        parameters.Add("IssuedFrom", issued.From.ToUniversalTime());
        parameters.Add("IssuedUntil", issued.Until.ToUniversalTime());
    }

    /// <summary>Arma <c>(@Name0, @Name1, …)</c> y agrega un parámetro por valor.</summary>
    private static string InList<T>(DynamicParameters parameters, string name, IEnumerable<T> values)
    {
        var names = values.Select((value, index) =>
        {
            var parameter = $"{name}{index}";
            parameters.Add(parameter, value);
            return "@" + parameter;
        });
        return "(" + string.Join(", ", names) + ")";
    }

    private static DocumentSummary Summary(HeaderRow row)
    {
        var customer = JsonColumn.Deserialize<CustomerSnapshot>(row.CustomerSnapshot);
        return new DocumentSummary(
            row.Id,
            row.DocumentTypeId,
            row.TypeEnvironmentId,
            row.Prefix,
            row.Number,
            row.FullNumber,
            (DocumentStatus)row.Status,
            row.Cufe,
            DateOnly.FromDateTime(row.IssueDate),
            TimeOnly.FromTimeSpan(row.IssueTime),
            Utc(row.IssuedAt),
            row.CurrencyId,
            new DocumentCustomer(
                customer.TypeDocumentIdentificationId,
                customer.IdentificationNumber,
                customer.Dv,
                customer.Name,
                customer.Phone,
                customer.Address,
                customer.Email,
                customer.MunicipalityId,
                customer.TypeOrganizationId,
                customer.TypeRegimeId,
                customer.TypeLiabilityId,
                customer.MerchantRegistration),
            new DocumentTotals(
                row.LineExtensionAmount,
                row.TaxExclusiveAmount,
                row.TaxInclusiveAmount,
                row.AllowanceTotalAmount,
                row.ChargeTotalAmount,
                row.PrepaidAmount,
                row.PayableAmount),
            (EmailStatus)row.EmailStatus);
    }

    private static DocumentTaxAmount Tax(TaxRow row) =>
        new(row.TaxId, row.Percent, row.TaxableAmount, row.TaxAmount, row.PerUnitAmount, row.UnitMeasureId);

    // Npgsql entrega timestamptz como DateTime en UTC.
    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTimeOffset? Utc(DateTime? value) => value is { } instant ? Utc(instant) : null;

    // Filas tal como las devuelve el SQL. Npgsql entrega date como DateTime y time como TimeSpan.

    private sealed class CompanyRow
    {
        public Guid Id { get; init; }

        public string IdentificationNumber { get; init; } = "";

        public string Name { get; init; } = "";
    }

    private sealed class CountRow
    {
        public int DocumentTypeId { get; init; }

        public long Total { get; init; }
    }

    private sealed class HeaderRow
    {
        public Guid Id { get; init; }

        public int DocumentTypeId { get; init; }

        public int TypeEnvironmentId { get; init; }

        public string Prefix { get; init; } = "";

        public long Number { get; init; }

        public string FullNumber { get; init; } = "";

        public short Status { get; init; }

        public string? Cufe { get; init; }

        public DateTime IssueDate { get; init; }

        public TimeSpan IssueTime { get; init; }

        public DateTime IssuedAt { get; init; }

        public int CurrencyId { get; init; }

        public string CustomerSnapshot { get; init; } = "";

        public decimal LineExtensionAmount { get; init; }

        public decimal TaxExclusiveAmount { get; init; }

        public decimal TaxInclusiveAmount { get; init; }

        public decimal AllowanceTotalAmount { get; init; }

        public decimal ChargeTotalAmount { get; init; }

        public decimal PrepaidAmount { get; init; }

        public decimal PayableAmount { get; init; }

        public short EmailStatus { get; init; }

        public string? UuidAlgorithm { get; init; }

        public Guid? ResolutionId { get; init; }

        public Guid? ReferenceDocumentId { get; init; }

        public int? TypeOperationId { get; init; }

        public decimal? ExchangeRate { get; init; }

        public int? DiscrepancyCode { get; init; }

        public string? DiscrepancyDescription { get; init; }

        public string? Notes { get; init; }

        public string? HeadNote { get; init; }

        public string? FootNote { get; init; }

        public string? TrackId { get; init; }

        public string? DianStatusCode { get; init; }

        public string? DianStatusDescription { get; init; }

        public DateTime? SentAt { get; init; }

        public DateTime? AcceptedAt { get; init; }
    }

    private sealed class TaxRow
    {
        public Guid LineId { get; init; }

        public int TaxId { get; init; }

        public decimal? Percent { get; init; }

        public decimal TaxableAmount { get; init; }

        public decimal TaxAmount { get; init; }

        public decimal? PerUnitAmount { get; init; }

        public int? UnitMeasureId { get; init; }
    }

    private sealed class LineRow
    {
        public Guid Id { get; init; }

        public int LineNumber { get; init; }

        public string? Code { get; init; }

        public string Description { get; init; } = "";

        public string? Notes { get; init; }

        public int TypeItemIdentificationId { get; init; }

        public int UnitMeasureId { get; init; }

        public decimal Quantity { get; init; }

        public decimal BaseQuantity { get; init; }

        public decimal UnitPrice { get; init; }

        public decimal LineExtensionAmount { get; init; }

        public bool FreeOfCharge { get; init; }

        public int? ReferencePriceId { get; init; }
    }
}
