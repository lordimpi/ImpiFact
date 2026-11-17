using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Abstractions.Storage;
using ImpiFact.Domain.Documents;
using ImpiFact.Domain.Platform;
using ImpiFact.Integration.Tests.Companies;
using ImpiFact.Integration.Tests.Protection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.DocumentIssue;

/// <summary>
/// Emisión de punta a punta por la API y con PostgreSQL: numeración (supuesto B2), idempotencia, errores 422, 401 y 403,
/// aislamiento entre empresas, certificado, resolución y lo que queda guardado (documento en <c>Queued</c>, archivos y
/// mensaje de la bandeja de salida). Cada prueba crea su propia empresa por la API de Company.
/// </summary>
[Category("Integration")]
public sealed class DocumentIssueApiTests
{
    private static CompanyApiFactory Api => DocumentIssueHost.Api;

    private static string Invoice => Path.Combine(GoldenIssueTests.GoldenRoot(), "invoice", "single-line-iva19");

    private static string CreditNote => Path.Combine(GoldenIssueTests.GoldenRoot(), "credit-note", "partial-return-with-reference");

    [Test]
    public async Task Without_number_the_sequence_assigns_consecutive_numbers_and_the_document_is_queued_with_files_and_outbox()
    {
        using var issuer = await NewIssuerAsync();
        var body = Edited(Invoice, json => json.Remove("number"));

        using var first = await PostAsync(issuer.Client, "invoice", body);
        using var second = await PostAsync(issuer.Client, "invoice", body);

        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK), await first.Content.ReadAsStringAsync());
        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var id = await DocumentIdAsync(first);
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        var documents = await CompanyApiTests.Rows<Document>(scope.Db, issuer.CompanyId).OrderBy(row => row.Number).ToListAsync();
        var files = await CompanyApiTests.Rows<DocumentFile>(scope.Db, issuer.CompanyId).Where(row => row.DocumentId == id).ToListAsync();
        var outbox = await scope.Db.Set<OutboxMessage>().Where(row => row.CompanyId == issuer.CompanyId).ToListAsync();
        Assert.Multiple(async () =>
        {
            Assert.That(documents.Select(row => row.Number), Is.EqualTo(new[] { 990000000L, 990000001L }));
            Assert.That(documents.Select(row => row.Status), Is.All.EqualTo(DocumentStatus.Queued));
            Assert.That(documents[0].FullNumber, Is.EqualTo("SETP990000000"));
            Assert.That(files.Select(row => row.Kind), Is.EquivalentTo(new[] { DocumentFileKind.SignedXml, DocumentFileKind.RequestJson }));
            Assert.That(await CompanyApiTests.Rows<DocumentLine>(scope.Db, issuer.CompanyId).CountAsync(row => row.DocumentId == id), Is.EqualTo(1));
            Assert.That(await CompanyApiTests.Rows<DocumentTax>(scope.Db, issuer.CompanyId).CountAsync(row => row.DocumentId == id), Is.EqualTo(1));
            Assert.That(await CompanyApiTests.Rows<DocumentLineTax>(scope.Db, issuer.CompanyId).CountAsync(row => row.DocumentId == id), Is.EqualTo(1));
            Assert.That(outbox.Select(row => row.Type), Is.All.EqualTo("SendDocumentToDian"));
            Assert.That(outbox.Select(row => row.Payload["document_id"]!.GetValue<string>()), Does.Contain(id.ToString("D")));
        });
        Assert.That(GoldenIssueTests.Verifies(await GoldenIssueTests.StoredSignedXmlAsync(id)), Is.True);
    }

    [Test]
    public async Task Concurrent_issues_without_number_get_distinct_numbers()
    {
        using var issuer = await NewIssuerAsync();
        var body = Edited(Invoice, json => json.Remove("number"));

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => PostAsync(issuer.Client, "invoice", body)));

        try
        {
            var statuses = responses.Select(response => response.StatusCode).ToList();
            // El UPDATE del consecutivo bloquea su fila hasta que cada transacción termina, así las solicitudes se atienden en fila.
            Assert.That(statuses, Has.All.EqualTo(HttpStatusCode.OK));
            await using var scope = Api.OpenScope(null, null, ActorType.System);
            var numbers = await CompanyApiTests.Rows<Document>(scope.Db, issuer.CompanyId).Select(row => row.Number).ToListAsync();
            Assert.That(numbers, Is.Unique);
            Assert.That(numbers.Order(), Is.EqualTo(Enumerable.Range(0, 6).Select(offset => 990000000L + offset)));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    // Hallazgo de la revisión de T2: dos resoluciones con el mismo prefijo comparten los números del índice único. El
    // consecutivo de la segunda salta los que ya usó la primera en vez de chocar (y revertirse) una y otra vez.
    [Test]
    public async Task Sequence_skips_numbers_already_used_by_another_resolution_with_the_same_prefix()
    {
        var second = new Dictionary<string, object>((Dictionary<string, object>)IssuerSetup.GoldenResolutions[0]) { ["resolution"] = "18760000002" };
        using var issuer = await NewIssuerAsync(resolutions: [.. IssuerSetup.GoldenResolutions, second]);

        using var first = await PostAsync(issuer.Client, "invoice", Edited(Invoice, json => json.Remove("number")));
        using var other = await PostAsync(issuer.Client, "invoice", Edited(Invoice, json => { json.Remove("number"); json["resolution_number"] = "18760000002"; }));
        using var ambiguous = await PostAsync(issuer.Client, "invoice", Edited(Invoice, json => { json.Remove("number"); json.Remove("resolution_number"); }));

        using var firstJson = await Json(first);
        using var otherJson = await Json(other);
        Assert.That(firstJson.RootElement.GetProperty("document").GetProperty("number").GetInt64(), Is.EqualTo(990000000L), firstJson.RootElement.ToString());
        Assert.That(otherJson.RootElement.GetProperty("document").GetProperty("number").GetInt64(), Is.EqualTo(990000001L), otherJson.RootElement.ToString());
        Assert.That(await FieldError(ambiguous, "resolution_number"), Is.EqualTo("El campo resolution number es obligatorio."));
    }

    [Test]
    public async Task Client_number_repeated_with_the_same_body_returns_the_same_document_and_another_body_is_a_conflict()
    {
        using var issuer = await NewIssuerAsync();
        var body = File.ReadAllBytes(Path.Combine(Invoice, "input.json"));

        using var first = await PostAsync(issuer.Client, "invoice", body);
        using var again = await PostAsync(issuer.Client, "invoice", body);
        using var other = await PostAsync(issuer.Client, "invoice", Edited(Invoice, json => json["notes"] = "otra"));

        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        using var againJson = await Json(again);
        Assert.That(againJson.RootElement.GetProperty("document").GetProperty("id").GetGuid(), Is.EqualTo(await DocumentIdAsync(first)));
        Assert.That(againJson.RootElement.GetProperty("document").GetProperty("replayed").GetBoolean(), Is.True);
        Assert.That(other.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(await Code(other), Is.EqualTo("document.number_in_use"));
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        Assert.That(await CompanyApiTests.Rows<Document>(scope.Db, issuer.CompanyId).CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task Same_idempotency_key_returns_the_same_document_and_a_different_body_is_a_conflict()
    {
        using var issuer = await NewIssuerAsync();
        var body = Edited(Invoice, json => json.Remove("number"));

        using var first = await PostAsync(issuer.Client, "invoice", body, idempotencyKey: "pedido-42");
        using var again = await PostAsync(issuer.Client, "invoice", body, idempotencyKey: "pedido-42");
        using var other = await PostAsync(issuer.Client, "invoice", Edited(Invoice, json => { json.Remove("number"); json["notes"] = "x"; }), idempotencyKey: "pedido-42");

        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await DocumentIdAsync(again), Is.EqualTo(await DocumentIdAsync(first)));
        Assert.That(other.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(await Code(other), Is.EqualTo("idempotency.key_reused"));
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        Assert.That(await CompanyApiTests.Rows<Document>(scope.Db, issuer.CompanyId).CountAsync(), Is.EqualTo(1));
        Assert.That(await CompanyApiTests.Rows<IdempotencyKey>(scope.Db, issuer.CompanyId).CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task Invalid_requests_answer_422_with_the_laravel_messages_and_nothing_is_stored()
    {
        using var issuer = await NewIssuerAsync();

        using var missingName = await PostAsync(issuer.Client, "invoice", Edited(Invoice, json => json["customer"]!.AsObject().Remove("name")));
        using var notJson = await PostAsync(issuer.Client, "invoice", Encoding.UTF8.GetBytes("{ no es json"));
        using var badTotals = await PostAsync(issuer.Client, "invoice", Edited(Invoice, json => json["legal_monetary_totals"]!["payable_amount"] = "1.00"));

        Assert.That(missingName.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
        using (var json = await Json(missingName))
        {
            Assert.That(json.RootElement.GetProperty("errors").GetProperty("customer.name")[0].GetString(), Is.EqualTo("El campo customer.name es obligatorio."));
            Assert.That(json.RootElement.GetProperty("success").GetBoolean(), Is.False);
        }

        Assert.That(notJson.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
        Assert.That(badTotals.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        Assert.That(await CompanyApiTests.Rows<Document>(scope.Db, issuer.CompanyId).AnyAsync(), Is.False);
    }

    // Riesgo de seguridad: ni el PIN del software, ni el certificado, ni la contraseña SMTP del cuerpo llegan a los
    // registros, a la respuesta o a la solicitud guardada.
    [Test]
    public async Task Secrets_never_reach_the_logs_the_response_or_the_stored_request()
    {
        using var issuer = await NewIssuerAsync();
        const string smtpPassword = "smtp-secreto-que-no-se-guarda";
        var body = Edited(Invoice, json => json["smtp_parameters"] = new JsonObject { ["host"] = "smtp.example.com", ["password"] = smtpPassword });

        using var response = await PostAsync(issuer.Client, "invoice", body);

        var text = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), text);
        var id = await DocumentIdAsync(response);
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        var request = await CompanyApiTests.Rows<DocumentFile>(scope.Db, issuer.CompanyId)
            .SingleAsync(row => row.DocumentId == id && row.Kind == DocumentFileKind.RequestJson);
        await using var stream = await scope.Services.GetRequiredService<IBlobStorage>().OpenReadAsync(request.StorageKey, CancellationToken.None);
        using var reader = new StreamReader(stream);
        var stored = await reader.ReadToEndAsync();
        var logs = string.Join(Environment.NewLine, Api.Logs.Entries);
        Assert.Multiple(() =>
        {
            Assert.That(stored, Does.Not.Contain(smtpPassword));
            Assert.That(logs, Does.Not.Contain(smtpPassword));
            Assert.That(logs, Does.Not.Contain(TestPfx.Password));
            Assert.That(logs, Does.Not.Contain("company-software.pin"));
            Assert.That(text, Does.Not.Contain(TestPfx.Password));
            Assert.That(text, Does.Not.Contain("PinCipher").IgnoreCase);
        });
    }

    [Test]
    public async Task Issuing_needs_a_company_credential_with_documents_write()
    {
        using var issuer = await NewIssuerAsync();
        var body = File.ReadAllBytes(Path.Combine(Invoice, "input.json"));
        using var readOnly = await Api.ClientWithNewKeyAsync(issuer.CompanyId, ApiScopes.DocumentsRead);
        using var platform = await Api.ClientWithNewKeyAsync(null, ApiScopes.Platform);
        using var anonymous = Api.CreateClient();

        using var forbidden = await PostAsync(readOnly, "invoice", body);
        using var platformResponse = await PostAsync(platform, "invoice", body);
        using var unauthenticated = await PostAsync(anonymous, "invoice", body);

        Assert.That(forbidden.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(platformResponse.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(unauthenticated.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    // El cuerpo no trae el NIT del emisor: la empresa es siempre la de la credencial, y nada de otra empresa se usa ni se ve.
    [Test]
    public async Task Companies_are_isolated_from_each_other()
    {
        using var first = await NewIssuerAsync();
        using var second = await NewIssuerAsync();
        var invoice = File.ReadAllBytes(Path.Combine(Invoice, "input.json"));
        using var firstInvoice = await PostAsync(first.Client, "invoice", invoice);
        string cufe;
        using (var json = await Json(firstInvoice))
        {
            cufe = json.RootElement.GetProperty("cufe").GetString()!;
        }

        using var sameNumber = await PostAsync(second.Client, "invoice", invoice);
        using var note = await PostAsync(second.Client, "credit-note", Edited(CreditNote, json => json["billing_reference"]!["uuid"] = cufe));
        using var foreignDetail = await second.Client.GetAsync($"/api/v1/documents/{await DocumentIdAsync(firstInvoice)}");

        Assert.That(sameNumber.StatusCode, Is.EqualTo(HttpStatusCode.OK), "Another company may use the same prefix and number.");
        Assert.That(note.StatusCode, Is.EqualTo(HttpStatusCode.OK), await note.Content.ReadAsStringAsync());
        Assert.That(foreignDetail.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        var noteRow = await CompanyApiTests.Rows<Document>(scope.Db, second.CompanyId).SingleAsync(row => row.DocumentTypeId == 4);
        Assert.That(noteRow.ReferenceDocumentId, Is.Null, "A note never links to another company's invoice.");
    }

    [Test]
    public async Task Certificate_missing_or_expired_and_software_missing_are_rejected()
    {
        using var noCertificate = await NewIssuerAsync(withCertificate: false);
        using var noSoftware = await NewIssuerAsync(withSoftware: false);
        using var expiring = await NewIssuerAsync();
        var body = File.ReadAllBytes(Path.Combine(Invoice, "input.json"));

        using var missing = await PostAsync(noCertificate.Client, "invoice", body);
        using var software = await PostAsync(noSoftware.Client, "invoice", body);
        Api.Clock.Advance(TimeSpan.FromDays(3650));
        HttpResponseMessage expired;
        try
        {
            expired = await PostAsync(expiring.Client, "invoice", body);
        }
        finally
        {
            Api.Clock.Advance(TimeSpan.FromDays(-3650));
        }

        using (expired)
        {
            Assert.That(expired.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(await Code(expired), Is.EqualTo("certificate.expired"));
        }

        Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
        Assert.That(await Code(missing), Is.EqualTo("certificate.missing"));
        Assert.That(await Code(software), Is.EqualTo("software.missing"));
    }

    [Test]
    public async Task Resolution_missing_number_out_of_range_and_date_outside_the_validity_are_rejected()
    {
        using var withoutResolutions = await NewIssuerAsync(resolutions: []);
        using var issuer = await NewIssuerAsync();

        using var missing = await PostAsync(withoutResolutions.Client, "invoice", File.ReadAllBytes(Path.Combine(Invoice, "input.json")));
        using var outOfRange = await PostAsync(issuer.Client, "invoice", Edited(Invoice, json => json["number"] = 995000001));
        using var huge = await PostAsync(issuer.Client, "invoice", Edited(Invoice, json => json["number"] = long.MaxValue));
        using var expired = await PostAsync(issuer.Client, "invoice", Edited(Invoice, json => json["date"] = "2030-01-20"));
        using var next = await PostAsync(issuer.Client, "invoice", Edited(Invoice, json => json.Remove("number")));

        Assert.That(await FieldError(missing, "type_document_id"), Is.EqualTo("La resolución no está configurada."));
        Assert.That(await FieldError(outOfRange, "number"),
            Is.EqualTo("number tiene que estar entre 990000000 - 995000000 o no se establecio el atributo que define el minimo y maximo."));
        Assert.That(huge.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
        Assert.That(await FieldError(expired, "date"), Is.EqualTo("date debe ser una fecha anterior o igual a 2030-01-19."));
        using var nextJson = await Json(next);
        Assert.That(nextJson.RootElement.GetProperty("document").GetProperty("number").GetInt64(), Is.EqualTo(990000000L),
            "Rejected numbers never move the sequence.");
    }

    [Test]
    public async Task Test_set_route_and_canonical_alias_use_the_same_use_case()
    {
        using var issuer = await NewIssuerAsync();

        using var testSet = await PostAsync(issuer.Client, "invoice/0d2e2883-eb8d-4d5c-a4a6-1bd40f5a2b10", Edited(Invoice, json => json.Remove("number")));
        using var canonical = await issuer.Client.PostAsync("/api/v1/documents/credit-notes", Content(File.ReadAllBytes(Path.Combine(CreditNote, "input.json"))));

        Assert.That(testSet.StatusCode, Is.EqualTo(HttpStatusCode.OK), await testSet.Content.ReadAsStringAsync());
        Assert.That(canonical.StatusCode, Is.EqualTo(HttpStatusCode.OK), await canonical.Content.ReadAsStringAsync());
        using (var json = await Json(canonical))
        {
            Assert.That(json.RootElement.TryGetProperty("success", out _), Is.False, "The PHP note response has no success field.");
            Assert.That(json.RootElement.GetProperty("cude").GetString(), Has.Length.EqualTo(96));
            Assert.That(json.RootElement.GetProperty("ResponseDian").ValueKind, Is.EqualTo(JsonValueKind.Null));
        }

        var id = await DocumentIdAsync(testSet);
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        var messages = await scope.Db.Set<OutboxMessage>().Where(row => row.CompanyId == issuer.CompanyId).ToListAsync();
        var message = messages.Single(row => row.Payload["document_id"]!.GetValue<string>() == id.ToString("D"));
        Assert.That(message.Payload["test_set_id"]!.GetValue<string>(), Is.EqualTo("0d2e2883-eb8d-4d5c-a4a6-1bd40f5a2b10"));
    }

    /// <summary>
    /// La respuesta lleva los nombres de archivo del PHP (<c>urlinvoicexml</c>, <c>urlinvoicepdf</c> y
    /// <c>urlinvoiceattached</c>, inventario de reglas 3.8), los mismos que entiende la ruta <c>download</c>. El XML
    /// firmado ya se puede descargar con su nombre; el PDF y el AttachedDocument existen cuando el Worker los genera.
    /// </summary>
    [TestCase("invoice", "invoice", "single-line-iva19", "FES")]
    [TestCase("credit-note", "credit-note", "partial-return-with-reference", "NCS")]
    [TestCase("debit-note", "debit-note", "price-adjustment-with-reference", "NDS")]
    public async Task The_response_names_the_php_files_of_the_document(string route, string kind, string name, string tag)
    {
        using var issuer = await NewIssuerAsync();

        using var response = await PostAsync(issuer.Client, route, Edited(Dir(kind, name), json => json.Remove("number")));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        string fullNumber;
        string xmlName;
        using (var json = await Json(response))
        {
            var root = json.RootElement;
            fullNumber = root.GetProperty("document").GetProperty("full_number").GetString()!;
            xmlName = root.GetProperty("urlinvoicexml").GetString()!;
            Assert.Multiple(() =>
            {
                Assert.That(xmlName, Is.EqualTo($"{tag}-{fullNumber}.xml"));
                Assert.That(root.GetProperty("urlinvoicepdf").GetString(), Is.EqualTo($"{tag}-{fullNumber}.pdf"));
                Assert.That(root.GetProperty("urlinvoiceattached").GetString(), Is.EqualTo($"Attachment-{fullNumber}.xml"));
            });
        }

        using var download = await issuer.Client.GetAsync($"/api/ubl2.1/download/{issuer.Nit}/{xmlName}");
        Assert.That(download.StatusCode, Is.EqualTo(HttpStatusCode.OK), await download.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// La DIAN da el <c>testSetId</c> como un GUID. Otra palabra en su lugar no es un set de pruebas: por ejemplo
    /// <c>invoice/preeliminar-view</c>, la vista previa del PHP (colección Postman, carpeta 02), no debe emitir ni
    /// mandar nada a la DIAN.
    /// </summary>
    [TestCase("invoice/preeliminar-view")]
    [TestCase("credit-note/not-a-test-set")]
    [TestCase("debit-note/not-a-test-set")]
    public async Task A_test_set_segment_that_is_not_a_guid_does_not_issue_anything(string route)
    {
        using var issuer = await NewIssuerAsync();

        using var response = await PostAsync(issuer.Client, route, Edited(Invoice, json => json.Remove("number")));

        await using var scope = Api.OpenScope(null, null, ActorType.System);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound), await response.Content.ReadAsStringAsync());
        Assert.That(await CompanyApiTests.Rows<Document>(scope.Db, issuer.CompanyId).CountAsync(), Is.Zero);
    }

    private static async Task<Issuer> NewIssuerAsync(bool withCertificate = true, bool withSoftware = true, IEnumerable<object>? resolutions = null)
    {
        var (nit, dv) = CompaniesHost.NewNit();
        return await IssuerSetup.CreateAsync(nit, dv, withCertificate, withSoftware, resolutions);
    }

    private static ByteArrayContent Content(byte[] body)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
    }

    // Contrato del PHP: GET invoice/current_number/{type}/{prefix?}/{ignore_state_document_id?} → {number, success, prefix}.
    // En ImpiFact el número sale del consecutivo, que nunca reutiliza números; por eso el tercer parámetro no cambia nada.
    [Test]
    public async Task Current_number_reports_the_next_consecutive_number_of_the_resolution()
    {
        using var issuer = await NewIssuerAsync();

        using var before = await issuer.Client.GetAsync("/api/ubl2.1/invoice/current_number/1");
        (await PostAsync(issuer.Client, "invoice", Edited(Invoice, json => json.Remove("number")))).Dispose();
        using var after = await issuer.Client.GetAsync("/api/ubl2.1/invoice/current_number/1/SETP/true");
        using var credit = await issuer.Client.GetAsync("/api/ubl2.1/invoice/current_number/4/null");
        using var canonical = await issuer.Client.GetAsync("/api/v1/documents/current-number/1?prefix=SETP");
        using var beforeJson = await Json(before);
        using var afterJson = await Json(after);
        using var creditJson = await Json(credit);
        using var canonicalJson = await Json(canonical);

        Assert.Multiple(() =>
        {
            Assert.That(before.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(beforeJson.RootElement.GetProperty("number").GetInt64(), Is.EqualTo(990000000));
            Assert.That(beforeJson.RootElement.GetProperty("success").GetBoolean(), Is.True);
            Assert.That(beforeJson.RootElement.GetProperty("prefix").GetString(), Is.EqualTo("SETP"));
            Assert.That(afterJson.RootElement.GetProperty("number").GetInt64(), Is.EqualTo(990000001));
            Assert.That(creditJson.RootElement.GetProperty("prefix").GetString(), Is.EqualTo("NC"));
            Assert.That(creditJson.RootElement.GetProperty("number").GetInt64(), Is.EqualTo(1));
            Assert.That(canonicalJson.RootElement.GetProperty("number").GetInt64(), Is.EqualTo(990000001));
            Assert.That(afterJson.RootElement.GetProperty("range_exceeded").GetBoolean(), Is.False);
        });
    }

    [Test]
    public async Task Current_number_of_a_prefix_without_resolution_answers_404_with_the_php_message()
    {
        using var issuer = await NewIssuerAsync();

        using var response = await issuer.Client.GetAsync("/api/ubl2.1/invoice/current_number/1/OTRO");
        using var json = await Json(response);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(json.RootElement.GetProperty("message").GetString(), Is.EqualTo("La resolución no está configurada."));
    }

    private static string Dir(string kind, string name) => Path.Combine(GoldenIssueTests.GoldenRoot(), kind, name);

    // Datos mal formados nunca deben terminar en un error interno (500): son errores del cliente (422).
    [TestCase("credit-note", "partial-return-with-reference", "billing_reference", "issue_date")]
    [TestCase("credit-note", "without-reference-invoice-period", "invoice_period", "start_date")]
    [TestCase("credit-note", "without-reference-invoice-period", "invoice_period", "end_date")]
    public async Task An_empty_date_inside_a_nested_object_answers_422(string kind, string name, string parent, string field)
    {
        using var issuer = await NewIssuerAsync();

        using var response = await PostAsync(issuer.Client, kind, Edited(Dir(kind, name), json => json[parent]![field] = ""));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
    }

    [Test]
    public async Task An_empty_per_unit_amount_on_a_fixed_value_tax_answers_422()
    {
        using var issuer = await NewIssuerAsync();

        using var response = await PostAsync(issuer.Client, "invoice", Edited(Dir("invoice", "inc-bags-fixed-value"), json =>
        {
            json["tax_totals"]![1]!["per_unit_amount"] = "";
            json["invoice_lines"]![1]!["tax_totals"]![0]!["per_unit_amount"] = "";
        }));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
    }

    [Test]
    public async Task A_property_repeated_at_the_root_answers_422()
    {
        using var issuer = await NewIssuerAsync();
        var text = Encoding.UTF8.GetString(Edited(Invoice, json => json.Remove("number")));

        using var response = await PostAsync(issuer.Client, "invoice", Encoding.UTF8.GetBytes("{\"notes\":\"a\",\"notes\":\"b\"," + text[1..]));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string route, byte[] body, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/ubl2.1/{route}") { Content = Content(body) };
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return await client.SendAsync(request);
    }

    private static byte[] Edited(string caseDir, Action<JsonObject> change)
    {
        var node = JsonNode.Parse(File.ReadAllBytes(Path.Combine(caseDir, "input.json")))!.AsObject();
        change(node);
        return Encoding.UTF8.GetBytes(node.ToJsonString());
    }

    private static async Task<JsonDocument> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private static async Task<Guid> DocumentIdAsync(HttpResponseMessage response)
    {
        using var json = await Json(response);
        return json.RootElement.GetProperty("document").GetProperty("id").GetGuid();
    }

    private static async Task<string?> Code(HttpResponseMessage response)
    {
        using var json = await Json(response);
        return json.RootElement.GetProperty("code").GetString();
    }

    private static async Task<string?> FieldError(HttpResponseMessage response, string field)
    {
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), await response.Content.ReadAsStringAsync());
        using var json = await Json(response);
        return json.RootElement.GetProperty("errors").GetProperty(field)[0].GetString();
    }
}
