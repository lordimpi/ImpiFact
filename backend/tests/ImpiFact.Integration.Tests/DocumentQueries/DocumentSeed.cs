using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Domain.Companies;
using ImpiFact.Domain.Documents;
using Npgsql;

namespace ImpiFact.Integration.Tests.DocumentQueries;

/// <summary>
/// Datos de las pruebas de consultas. La empresa se guarda con EF como proceso del sistema; los documentos, sus líneas
/// e impuestos se insertan con SQL directo y el estado que pida cada prueba, porque en esta fase ningún documento
/// llega todavía a <c>Accepted</c> por el flujo real (eso pasa en la fase 4).
/// </summary>
internal static class DocumentSeed
{
    private static readonly TimeSpan Colombia = TimeSpan.FromHours(-5);
    private static int sequence;

    /// <summary>Una empresa nueva con un NIT (Número de Identificación Tributaria) que no choca con otras pruebas.</summary>
    public static async Task<SeededCompany> CompanyAsync(string? name = null)
    {
        var id = Guid.CreateVersion7();
        var nit = (800_000_000 + Interlocked.Increment(ref sequence)).ToString(CultureInfo.InvariantCulture);
        await using var scope = DocumentQueriesHost.Api.OpenScope(null, null, ActorType.System);
        scope.Db.Add(new Company
        {
            Id = id,
            IdentificationNumber = nit,
            Dv = "1",
            Name = name ?? "EMPRESA " + nit + " SAS",
            Email = $"company{nit}@example.com",
            Phone = "3000000000",
            Address = "Calle 1",
            LanguageId = 79,
            CountryId = 46,
            MunicipalityId = 149,
            TaxId = 1,
            TypeCurrencyId = 35,
            TypeOperationId = 10,
            TypeDocumentIdentificationId = 6,
            TypeOrganizationId = 1,
            TypeRegimeId = 1,
        });
        await scope.Db.SaveChangesAsync();
        return new SeededCompany(id, nit, name ?? "EMPRESA " + nit + " SAS");
    }

    /// <summary>Un cliente HTTP con una API key nueva de la empresa y los alcances indicados.</summary>
    public static Task<HttpClient> ClientAsync(SeededCompany company, params string[] scopes) =>
        DocumentQueriesHost.Api.ClientWithNewKeyAsync(company.Id, scopes.Length == 0 ? [ApiScopes.DocumentsRead] : scopes);

    /// <summary>Inserta un documento emitido en <paramref name="issuedAt"/> (UTC); la fecha y hora del XML salen en hora Colombia.</summary>
    public static async Task<Guid> DocumentAsync(
        SeededCompany company,
        int typeId,
        long number,
        DocumentStatus status,
        DateTimeOffset issuedAt,
        string? cufe = null,
        Guid? id = null,
        string prefix = "SETP",
        string? customerName = null,
        string? customerIdentification = null,
        EmailStatus emailStatus = EmailStatus.NotSent)
    {
        var documentId = id ?? Guid.CreateVersion7();
        var local = issuedAt.ToOffset(Colombia);
        var customer = JsonNode.Parse(CustomerJson)!.AsObject();
        customer["name"] = customerName ?? (string?)customer["name"];
        customer["identification_number"] = customerIdentification ?? (string?)customer["identification_number"];
        await using var connection = new NpgsqlConnection(DocumentQueriesHost.ConnectionString);
        await connection.ExecuteAsync(
            """
            insert into documents (id, company_id, document_type_id, type_environment_id, prefix, number, full_number, status, cufe,
                uuid_algorithm, issue_date, issue_time, issued_at, customer_snapshot, currency_id, line_extension_amount,
                tax_exclusive_amount, tax_inclusive_amount, allowance_total_amount, charge_total_amount, prepaid_amount, payable_amount,
                notes, email_status, created_by_type, created_at, updated_at, row_version)
            values (@Id, @CompanyId, @TypeId, 2, @Prefix, @Number, @FullNumber, @Status, @Cufe,
                'CUFE-SHA384', cast(@IssueDate as date), cast(@IssueTime as time), @IssuedAt, cast(@Customer as jsonb), 35, 100.00,
                100.00, 119.00, 0, 0, 0, 119.00,
                'nota de prueba', @EmailStatus, 2, now(), now(), 1)
            """,
            new
            {
                Id = documentId,
                CompanyId = company.Id,
                TypeId = typeId,
                Prefix = prefix,
                Number = number,
                FullNumber = prefix + number.ToString(CultureInfo.InvariantCulture),
                Status = (short)status,
                Cufe = cufe,
                IssueDate = local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                IssueTime = local.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                IssuedAt = issuedAt.ToUniversalTime(),
                Customer = customer.ToJsonString(),
                EmailStatus = (short)emailStatus,
            });
        return documentId;
    }

    /// <summary>
    /// Inserta <paramref name="count"/> facturas aceptadas con una sola sentencia (<c>generate_series</c>), una por
    /// segundo desde <paramref name="start"/>, para probar topes sin miles de viajes a la base.
    /// </summary>
    public static async Task ManyDocumentsAsync(SeededCompany company, int count, DateTimeOffset start)
    {
        await using var connection = new NpgsqlConnection(DocumentQueriesHost.ConnectionString);
        await connection.ExecuteAsync(
            """
            insert into documents (id, company_id, document_type_id, type_environment_id, prefix, number, full_number, status,
                uuid_algorithm, issue_date, issue_time, issued_at, customer_snapshot, currency_id, line_extension_amount,
                tax_exclusive_amount, tax_inclusive_amount, allowance_total_amount, charge_total_amount, prepaid_amount, payable_amount,
                created_by_type, created_at, updated_at, row_version)
            select gen_random_uuid(), @CompanyId, 1, 2, 'BULK', n, 'BULK' || n, 4,
                'CUFE-SHA384', cast(@IssueDate as date), time '00:00:00', @Start + n * interval '1 second', cast(@Customer as jsonb), 35, 100.00,
                100.00, 119.00, 0, 0, 0, 119.00,
                2, now(), now(), 1
            from generate_series(1, @Count) as n
            """,
            new
            {
                CompanyId = company.Id,
                IssueDate = start.ToOffset(Colombia).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Start = start.ToUniversalTime(),
                Customer = CustomerJson,
                Count = count,
            },
            // Con toda la suite corriendo en paralelo, insertar 50.000 filas puede pasar de los 30 s por defecto.
            commandTimeout: 300);
    }

    /// <summary>Agrega una línea con IVA (impuesto 1) del 19 % y el mismo impuesto en la cabecera.</summary>
    public static async Task LineWithVatAsync(SeededCompany company, Guid documentId)
    {
        var lineId = Guid.CreateVersion7();
        await using var connection = new NpgsqlConnection(DocumentQueriesHost.ConnectionString);
        await connection.ExecuteAsync(
            """
            insert into document_lines (id, company_id, document_id, line_number, code, description, type_item_identification_id,
                unit_measure_id, quantity, base_quantity, unit_price, line_extension_amount, free_of_charge, created_at, updated_at, row_version)
            values (@LineId, @CompanyId, @DocumentId, 1, 'A1', 'Producto de prueba', 1, 70, 2.000000, 1.000000, 50.000000, 100.00, false,
                now(), now(), 1);
            insert into document_line_taxes (id, company_id, document_id, line_id, tax_id, percent, taxable_amount, tax_amount,
                created_at, updated_at, row_version)
            values (@LineTaxId, @CompanyId, @DocumentId, @LineId, 1, 19.0000, 100.00, 19.00, now(), now(), 1);
            insert into document_taxes (id, company_id, document_id, tax_id, percent, taxable_amount, tax_amount, created_at, updated_at, row_version)
            values (@TaxId, @CompanyId, @DocumentId, 1, 19.0000, 100.00, 19.00, now(), now(), 1);
            """,
            new { LineId = lineId, LineTaxId = Guid.CreateVersion7(), TaxId = Guid.CreateVersion7(), CompanyId = company.Id, DocumentId = documentId });
    }

    public static async Task<JsonDocument> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    public static async Task<JsonDocument> GetJsonAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        Assert.That((int)response.StatusCode, Is.EqualTo(200), body);
        return JsonDocument.Parse(body);
    }

    private const string CustomerJson = """
        {"type_document_identification_id":3,"identification_number":"222222222222","dv":null,"name":"Consumidor final",
         "phone":"3000000000","address":"Calle 1","email":"cliente@example.com","municipality_id":149,"type_organization_id":2,
         "type_regime_id":2,"type_liability_id":117,"merchant_registration":null}
        """;
}

internal sealed record SeededCompany(Guid Id, string Nit, string Name);
