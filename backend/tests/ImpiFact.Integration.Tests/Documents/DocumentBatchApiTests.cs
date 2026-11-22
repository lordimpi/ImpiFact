using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Application.Features.Documents.Batches;
using ImpiFact.Application.Features.Documents.Delivery;
using ImpiFact.Domain.Billing;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Documents;
using ImpiFact.Domain.Platform;
using ImpiFact.Integration.Tests.Companies;
using ImpiFact.Integration.Tests.DianSend;
using Microsoft.EntityFrameworkCore;
using static ImpiFact.Integration.Tests.DocumentIssue.ContingencyInvoiceIssueApiTests;

// Same namespace as the issue tests so that DocumentIssueHost (a namespace-scoped SetUpFixture) starts the API for it.
namespace ImpiFact.Integration.Tests.DocumentIssue;

/// <summary>
/// Batches through the real API and the real Worker on the same database, against a simulated DIAN (phase 7a, T8; S2 item 3:
/// no Postman, the PHP is the reference; php-rules 15). Documents added to a batch are issued by the normal pipeline and wait
/// signed; the send makes one SendBillAsync with a ZIP of their signed XML and keeps the ZipKey (the PHP lost it, php-rules
/// 7, row 21); GetStatusZip gives each document its own verdict, with the side effects of a document sent alone.
/// </summary>
[Category("Integration")]
public sealed class DocumentBatchApiTests
{
    private const string BatchesRoute = "/api/v1/documents/batches";
    private const string ZipKey = "8b1849d4-884b-4245-9094-08e76be927a3";

    private static int planSequence = 2300;

    private static CompanyApiFactory Api => DocumentIssueHost.Api;

    [Test]
    public async Task Documents_added_to_a_batch_are_issued_signed_and_wait_queued_without_any_dian_call()
    {
        await using var worker = await WorkerAsync();
        using var issuer = await NewIssuerAsync();
        var batch = await CreateBatchAsync(issuer, "lote-espera");

        using var invoice = await AddAsync(issuer, batch, "invoice", Body("invoice", "single-line-iva19"));
        using var note = await AddAsync(issuer, batch, "credit-note", Body("credit-note", "without-reference-invoice-period"));
        await worker.RunOutboxAsync();

        var invoiceText = await invoice.Content.ReadAsStringAsync();
        Assert.That(invoice.StatusCode, Is.EqualTo(HttpStatusCode.OK), invoiceText);
        Assert.That(note.StatusCode, Is.EqualTo(HttpStatusCode.OK), await note.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(invoiceText);
        var documents = await DocumentsAsync(issuer);
        using var detail = await GetBatchAsync(issuer, batch);
        Assert.Multiple(async () =>
        {
            Assert.That(json.RootElement.GetProperty("message").GetString(), Does.EndWith("generada y agregada con éxito al lote lote-espera"));
            Assert.That(json.RootElement.GetProperty("document").GetProperty("batch_id").GetGuid(), Is.EqualTo(batch));
            Assert.That(json.RootElement.GetProperty("cufe").GetString(), Has.Length.EqualTo(96));
            Assert.That(documents.Select(row => (row.DocumentTypeId, row.Status, row.BatchId)),
                Is.EquivalentTo(new[] { (1, DocumentStatus.Queued, (Guid?)batch), (4, DocumentStatus.Queued, (Guid?)batch) }));
            Assert.That(await MessagesAsync(issuer, OutboxMessageTypes.SendDocumentToDian), Is.Empty, "A batched document has no message of its own.");
            Assert.That(worker.Dian.Requests, Is.Empty);
            Assert.That(detail.RootElement.GetProperty("status").GetString(), Is.EqualTo("Open"));
            Assert.That(detail.RootElement.GetProperty("document_count").GetInt32(), Is.EqualTo(2));
            Assert.That(detail.RootElement.GetProperty("documents").GetArrayLength(), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task A_document_added_to_a_batch_follows_the_validation_of_its_issue_route_and_stores_nothing_when_invalid()
    {
        using var issuer = await NewIssuerAsync();
        var batch = await CreateBatchAsync(issuer);

        using var response = await AddAsync(issuer, batch, "invoice-mandate", Edited("invoice-mandate", "third-party-line", json => json["type_operation_id"] = 10));

        var text = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), text);
        using var json = JsonDocument.Parse(text);
        using var detail = await GetBatchAsync(issuer, batch);
        Assert.Multiple(async () =>
        {
            Assert.That(json.RootElement.GetProperty("errors").GetProperty("type_operation_id")[0].GetString(), Is.EqualTo("type operation id es inválido."));
            Assert.That(await DocumentsAsync(issuer), Is.Empty);
            Assert.That(detail.RootElement.GetProperty("document_count").GetInt32(), Is.Zero);
        });
    }

    [TestCase("invoice-transport")]
    [TestCase("support-document")]
    [TestCase("invoice-contingency-4")]
    public async Task A_type_the_php_does_not_batch_answers_422(string type)
    {
        using var issuer = await NewIssuerAsync();
        var batch = await CreateBatchAsync(issuer);

        using var response = await AddAsync(issuer, batch, type, Body("invoice", "single-line-iva19"));

        var text = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), text);
        using var json = JsonDocument.Parse(text);
        Assert.That(json.RootElement.GetProperty("errors").GetProperty("type")[0].GetString(), Does.StartWith($"El tipo {type} no se puede agregar a un lote."));
    }

    [Test]
    public async Task Sending_a_batch_makes_one_send_bill_async_with_the_signed_xml_of_each_document_and_keeps_its_zip_key()
    {
        await using var worker = await WorkerAsync();
        using var issuer = await NewIssuerAsync();
        var batch = await CreateBatchAsync(issuer);
        using (await AddAsync(issuer, batch, "invoice", Body("invoice", "single-line-iva19")))
        using (await AddAsync(issuer, batch, "credit-note", Body("credit-note", "without-reference-invoice-period")))
        {
        }

        worker.Dian.Reply(BatchReplies.SendBillAsync(ZipKey));
        using var sent = await SendAsync(issuer, batch);
        Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.OK), await sent.Content.ReadAsStringAsync());
        await worker.RunOutboxAsync();

        var request = worker.Dian.Requests.Single();
        var zip = Convert.FromBase64String(request.Parameter("contentFile")!);
        var entries = ZipEntries(zip);
        var documents = await DocumentsAsync(issuer);
        var stored = await BatchRowAsync(batch);
        var nit = issuer.Nit.PadLeft(10, '0');
        Assert.Multiple(async () =>
        {
            Assert.That(request.Operation, Is.EqualTo("SendBillAsync"));
            Assert.That(request.Parameter("fileName"), Is.EqualTo($"z{nit}00026{1:D8}.zip"), "Annex 1.9, section 6.8: the ZIP name with the package consecutive.");
            Assert.That(entries.Keys, Is.EquivalentTo(new[] { $"fv{nit}00026{1:D8}.xml", $"nc{nit}00026{1:D8}.xml" }));
            foreach (var document in documents)
            {
                Assert.That(entries.Values, Has.Some.EqualTo(await SignedXmlAsync(worker, document)), $"The ZIP carries the stored signed XML of {document.FullNumber}.");
                Assert.That((document.Status, document.TrackId), Is.EqualTo((DocumentStatus.Sent, (string?)null)));
            }

            Assert.That((stored.Status, stored.ZipKey, stored.ZipFileName), Is.EqualTo((DocumentBatchStatus.Sent, ZipKey, request.Parameter("fileName"))));
            Assert.That(await MessagesAsync(issuer, OutboxMessageTypes.PollDocumentBatchStatus), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task Get_status_zip_gives_each_document_its_own_verdict_with_the_effects_of_a_single_document()
    {
        await using var worker = await WorkerAsync();
        using var issuer = await NewIssuerAsync();
        await AssignInvoicePlanAsync(issuer, quantity: 10);
        var batch = await CreateBatchAsync(issuer);
        using (await AddAsync(issuer, batch, "invoice", Body("invoice", "single-line-iva19")))
        using (await AddAsync(issuer, batch, "credit-note", Body("credit-note", "without-reference-invoice-period")))
        {
        }

        worker.Dian.Reply(BatchReplies.SendBillAsync(ZipKey));
        using (await SendAsync(issuer, batch))
        {
        }

        await worker.RunOutboxAsync();
        var invoice = (await DocumentsAsync(issuer)).Single(row => row.DocumentTypeId == 1);
        var note = (await DocumentsAsync(issuer)).Single(row => row.DocumentTypeId == 4);
        var noteFile = Path.GetFileNameWithoutExtension((await BatchRowAsync(batch)).Entries!.Single(entry => entry.DocumentId == note.Id).FileName);

        // The first answer is still in process; the invoice is named by its code, the note only by its file.
        worker.Dian.Reply(BatchReplies.GetStatusZipInProcess());
        worker.Dian.Reply(BatchReplies.GetStatusZip(
            new BatchVerdict(invoice.Cufe!.ToUpperInvariant(), null, true, "00"),
            new BatchVerdict(null, noteFile, false, "99", DianReplies.Rejection)));
        worker.Clock.Advance(DianPollScheduleFirstZipPoll);
        await worker.RunOutboxAsync();
        Assert.That((await BatchRowAsync(batch)).Status, Is.EqualTo(DocumentBatchStatus.Sent), "In process: the batch keeps waiting.");
        worker.Clock.Advance(TimeSpan.FromMinutes(1));
        await worker.RunOutboxAsync();

        var accepted = await worker.DocumentAsync(invoice.Id);
        var rejected = await worker.DocumentAsync(note.Id);
        using var detail = await GetBatchAsync(issuer, batch);
        var rejectedJson = detail.RootElement.GetProperty("documents").EnumerateArray().Single(row => row.GetProperty("id").GetGuid() == note.Id);
        Assert.Multiple(async () =>
        {
            Assert.That(worker.Dian.Requests.Select(row => row.Operation), Is.EqualTo(new[] { "SendBillAsync", "GetStatusZip", "GetStatusZip" }));
            Assert.That(accepted.Status, Is.EqualTo(DocumentStatus.Accepted));
            Assert.That((await worker.FilesAsync(invoice.Id)).Select(file => file.Kind), Does.Contain(DocumentFileKind.AttachedDocument));
            Assert.That((await worker.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.RenderPdf)).Select(message => message.Payload["document_id"]!.GetValue<Guid>()),
                Is.EqualTo(new[] { invoice.Id }), "The PDF and then the email follow the acceptance, as for a single document.");
            Assert.That(await UsedAsync(issuer), Is.EqualTo(1L), "Only the accepted document counts in the plan.");
            Assert.That(rejected.Status, Is.EqualTo(DocumentStatus.Rejected));
            Assert.That(rejectedJson.GetProperty("error_messages").EnumerateArray().Select(row => row.GetString()), Is.EqualTo(new[] { DianReplies.Rejection }));
            Assert.That((await worker.NotificationsAsync(issuer.CompanyId)).Select(row => row.Type), Is.EqualTo(new[] { DianNotificationTypes.Rejected }));
            Assert.That(detail.RootElement.GetProperty("status").GetString(), Is.EqualTo("Completed"));
            Assert.That(detail.RootElement.GetProperty("zip_key").GetString(), Is.EqualTo(ZipKey));
        });

        // Nothing is asked or sent again once every document has its verdict.
        worker.Clock.Advance(TimeSpan.FromMinutes(30));
        await worker.RunOutboxAsync();
        Assert.That(worker.Dian.Requests, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task A_document_the_initial_validations_reject_is_rejected_at_once_and_the_rest_wait_for_get_status_zip()
    {
        await using var worker = await WorkerAsync();
        using var issuer = await NewIssuerAsync();
        var batch = await CreateBatchAsync(issuer);
        using (await AddAsync(issuer, batch, "invoice", Body("invoice", "single-line-iva19")))
        using (await AddAsync(issuer, batch, "credit-note", Body("credit-note", "without-reference-invoice-period")))
        {
        }

        var nit = issuer.Nit.PadLeft(10, '0');
        worker.Dian.Reply(BatchReplies.SendBillAsync(ZipKey, ($"nc{nit}00026{1:D8}", "Campo CUFE del UBL vacío.")));
        using (await SendAsync(issuer, batch))
        {
        }

        await worker.RunOutboxAsync();

        var documents = await DocumentsAsync(issuer);
        Assert.Multiple(async () =>
        {
            Assert.That(documents.Single(row => row.DocumentTypeId == 4).Status, Is.EqualTo(DocumentStatus.Rejected));
            Assert.That(documents.Single(row => row.DocumentTypeId == 1).Status, Is.EqualTo(DocumentStatus.Sent));
            Assert.That((await BatchRowAsync(batch)).ErrorMessages, Is.EqualTo(new[] { "Campo CUFE del UBL vacío." }));
        });
    }

    [Test]
    public async Task A_zip_refused_without_zip_key_rejects_every_document_with_the_dian_messages()
    {
        await using var worker = await WorkerAsync();
        using var issuer = await NewIssuerAsync();
        var batch = await CreateBatchAsync(issuer);
        using (await AddAsync(issuer, batch, "invoice", Body("invoice", "single-line-iva19")))
        {
        }

        worker.Dian.Reply(BatchReplies.SendBillAsyncWithoutZipKey(("z", "Archivo ZIP corrupto.")));
        using (await SendAsync(issuer, batch))
        {
        }

        await worker.RunOutboxAsync();

        Assert.Multiple(async () =>
        {
            Assert.That((await DocumentsAsync(issuer)).Single().Status, Is.EqualTo(DocumentStatus.Rejected));
            Assert.That((await BatchRowAsync(batch)).Status, Is.EqualTo(DocumentBatchStatus.Completed));
            Assert.That(await MessagesAsync(issuer, OutboxMessageTypes.PollDocumentBatchStatus), Is.Empty);
        });
    }

    [Test]
    public async Task A_send_without_answer_is_never_repeated_and_each_document_is_followed_by_its_own_code()
    {
        var settings = new Dictionary<string, string?>(Api.SharedSettings)
        {
            ["Dian:TimeoutSeconds"] = "1", ["Resilience:Retries"] = "0", ["Resilience:TotalSeconds"] = "2",
        };
        await using var worker = await WorkerAsync(settings);
        using var issuer = await NewIssuerAsync();
        var batch = await CreateBatchAsync(issuer);
        using (await AddAsync(issuer, batch, "invoice", Body("invoice", "single-line-iva19")))
        {
        }

        worker.Dian.Hang = true;
        using (await SendAsync(issuer, batch))
        {
        }

        await worker.RunOutboxAsync();

        var document = (await DocumentsAsync(issuer)).Single();
        Assert.Multiple(async () =>
        {
            Assert.That(document.Status, Is.EqualTo(DocumentStatus.Unconfirmed));
            Assert.That((await BatchRowAsync(batch)).Status, Is.EqualTo(DocumentBatchStatus.Completed));
            Assert.That((await worker.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.PollDianStatus)).Select(message => message.Payload["document_id"]!.GetValue<Guid>()),
                Is.EqualTo(new[] { document.Id }));
            Assert.That(worker.Dian.Requests.Select(row => row.Operation), Is.EqualTo(new[] { "SendBillAsync" }));
        });
    }

    [Test]
    public async Task A_batch_is_sent_only_once_and_takes_no_more_documents_after_its_send()
    {
        using var issuer = await NewIssuerAsync();
        var batch = await CreateBatchAsync(issuer, "lote-una-vez");
        using (await AddAsync(issuer, batch, "invoice", Body("invoice", "single-line-iva19")))
        {
        }

        using var first = await SendAsync(issuer, batch);
        using var second = await SendAsync(issuer, batch);
        using var late = await AddAsync(issuer, batch, "credit-note", Body("credit-note", "without-reference-invoice-period"));

        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK), await first.Content.ReadAsStringAsync());
        using var firstJson = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        Assert.Multiple(async () =>
        {
            Assert.That(firstJson.RootElement.GetProperty("status").GetString(), Is.EqualTo("Queued"));
            Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(await CodeAsync(second), Is.EqualTo("batch.not_open"));
            Assert.That(late.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(await CodeAsync(late), Is.EqualTo("batch.not_open"));
            Assert.That(await DocumentsAsync(issuer), Has.Count.EqualTo(1));
            Assert.That(await MessagesAsync(issuer, OutboxMessageTypes.SendDocumentBatchToDian), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task Sends_at_the_same_time_leave_a_single_message_and_a_single_soap_call()
    {
        await using var worker = await WorkerAsync();
        using var issuer = await NewIssuerAsync();
        var batch = await CreateBatchAsync(issuer);
        using (await AddAsync(issuer, batch, "invoice", Body("invoice", "single-line-iva19")))
        {
        }

        worker.Dian.Route = request => request.Operation == "SendBillAsync" ? FakeDian.Response(BatchReplies.SendBillAsync(ZipKey)) : null;
        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => SendAsync(issuer, batch)));
        await worker.RunOutboxAsync();
        await worker.RunOutboxAsync();

        try
        {
            Assert.Multiple(async () =>
            {
                Assert.That(responses.Count(response => response.StatusCode == HttpStatusCode.OK), Is.EqualTo(1));
                Assert.That(responses.Where(response => response.StatusCode != HttpStatusCode.OK).Select(response => response.StatusCode),
                    Is.All.EqualTo(HttpStatusCode.Conflict));
                Assert.That(await MessagesAsync(issuer, OutboxMessageTypes.SendDocumentBatchToDian), Has.Count.EqualTo(1));
                Assert.That(worker.Dian.Requests.Count(request => request.Operation == "SendBillAsync"), Is.EqualTo(1));
            });
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Test]
    public async Task A_batch_of_another_company_answers_404_and_never_reaches_the_dian()
    {
        await using var worker = await WorkerAsync();
        using var owner = await NewIssuerAsync();
        using var other = await NewIssuerAsync();
        var batch = await CreateBatchAsync(owner, "lote-ajeno");

        using var get = await other.Client.GetAsync($"{BatchesRoute}/{batch}");
        using var add = await AddAsync(other, batch, "invoice", Body("invoice", "single-line-iva19"));
        using var send = await SendAsync(other, batch);
        using var list = await other.Client.GetAsync(BatchesRoute);
        await worker.RunOutboxAsync();

        using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.Multiple(async () =>
        {
            Assert.That(new[] { get.StatusCode, add.StatusCode, send.StatusCode }, Is.All.EqualTo(HttpStatusCode.NotFound));
            Assert.That(await CodeAsync(add), Is.EqualTo("batch.not_found"));
            Assert.That(await DocumentsAsync(other), Is.Empty, "Nothing is issued for the caller either.");
            Assert.That(listJson.RootElement.GetProperty("total_count").GetInt64(), Is.Zero);
            Assert.That((await BatchRowAsync(batch)).Status, Is.EqualTo(DocumentBatchStatus.Open));
            Assert.That(worker.Dian.Requests, Is.Empty);
        });
    }

    [Test]
    public async Task An_empty_batch_is_not_sent_and_a_full_one_takes_no_more_documents()
    {
        using var issuer = await NewIssuerAsync();
        var empty = await CreateBatchAsync(issuer);
        var full = await CreateBatchAsync(issuer);
        await using (var scope = Api.OpenScope(null, null, ActorType.System))
        {
            await scope.Db.Set<DocumentBatch>().IgnoreQueryFilters().Where(row => row.Id == full)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.DocumentCount, DocumentBatch.MaxDocuments));
        }

        using var sent = await SendAsync(issuer, empty);
        using var added = await AddAsync(issuer, full, "invoice", Body("invoice", "single-line-iva19"));

        Assert.Multiple(async () =>
        {
            Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(added.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(await CodeAsync(added), Is.EqualTo("batch.full"));
            Assert.That(await DocumentsAsync(issuer), Is.Empty);
        });
    }

    [Test]
    public async Task A_repeated_name_answers_409_and_a_batch_without_name_gets_one()
    {
        using var issuer = await NewIssuerAsync();
        await CreateBatchAsync(issuer, "lote-repetido");

        using var repeated = await issuer.Client.PostAsJsonAsync(BatchesRoute, new { name = "lote-repetido" });
        using var invalid = await issuer.Client.PostAsJsonAsync(BatchesRoute, new { name = "lote con espacios" });
        using var unnamed = await issuer.Client.PostAsJsonAsync(BatchesRoute, new { });

        using var json = JsonDocument.Parse(await unnamed.Content.ReadAsStringAsync());
        Assert.Multiple(async () =>
        {
            Assert.That(repeated.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(await CodeAsync(repeated), Is.EqualTo("batch.name_in_use"));
            Assert.That(invalid.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(unnamed.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(json.RootElement.GetProperty("name").GetString(), Does.Match("^lote-[0-9a-f]{32}$"));
        });
    }

    [Test]
    public async Task Repeating_an_addition_answers_its_document_but_a_body_issued_alone_is_not_taken_into_a_batch()
    {
        using var issuer = await NewIssuerAsync();
        var batch = await CreateBatchAsync(issuer);
        using (await AddAsync(issuer, batch, "invoice", Body("invoice", "single-line-iva19")))
        {
        }

        using var again = await AddAsync(issuer, batch, "invoice", Body("invoice", "single-line-iva19"));
        using var alone = await PostAsync(issuer.Client, "/api/v1/documents/invoices", Body("invoice", "two-lines-discounts-iva19"));
        using var intoBatch = await AddAsync(issuer, batch, "invoice", Body("invoice", "two-lines-discounts-iva19"));

        using var againJson = JsonDocument.Parse(await again.Content.ReadAsStringAsync());
        using var detail = await GetBatchAsync(issuer, batch);
        Assert.Multiple(async () =>
        {
            Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(againJson.RootElement.GetProperty("document").GetProperty("replayed").GetBoolean(), Is.True);
            Assert.That(alone.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(intoBatch.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(await CodeAsync(intoBatch), Is.EqualTo("document.number_in_use"));
            Assert.That(detail.RootElement.GetProperty("document_count").GetInt32(), Is.EqualTo(1));
        });
    }

    /// <summary>First GetStatusZip of a sent batch (DianPollSchedule.FirstZipPoll, internal to Application).</summary>
    private static readonly TimeSpan DianPollScheduleFirstZipPoll = TimeSpan.FromSeconds(15);

    private static async Task<DianSendHost> WorkerAsync(IReadOnlyDictionary<string, string?>? settings = null)
    {
        var worker = await DianSendHost.StartAsync(settings ?? Api.SharedSettings, DocumentIssueHost.ConnectionString);
        worker.Clock.Advance(Api.Clock.UtcNow - worker.Clock.UtcNow);
        return worker;
    }

    private static async Task<Guid> CreateBatchAsync(Issuer issuer, string? name = null)
    {
        using var response = await issuer.Client.PostAsJsonAsync(BatchesRoute, new { name });
        var text = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), text);
        using var json = JsonDocument.Parse(text);
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> AddAsync(Issuer issuer, Guid batch, string type, byte[] body) =>
        PostAsync(issuer.Client, $"{BatchesRoute}/{batch}/documents/{type}", body);

    private static Task<HttpResponseMessage> SendAsync(Issuer issuer, Guid batch) => issuer.Client.PostAsync($"{BatchesRoute}/{batch}/send", null);

    private static async Task<JsonDocument> GetBatchAsync(Issuer issuer, Guid batch)
    {
        using var response = await issuer.Client.GetAsync($"{BatchesRoute}/{batch}");
        var text = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), text);
        return JsonDocument.Parse(text);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static async Task<List<Document>> DocumentsAsync(Issuer issuer)
    {
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        return await CompanyApiTests.Rows<Document>(scope.Db, issuer.CompanyId).ToListAsync();
    }

    private static async Task<DocumentBatch> BatchRowAsync(Guid batch)
    {
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        return await scope.Db.Set<DocumentBatch>().IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.Id == batch);
    }

    private static async Task<List<OutboxMessage>> MessagesAsync(Issuer issuer, string type)
    {
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        return await scope.Db.Set<OutboxMessage>().AsNoTracking().Where(row => row.CompanyId == issuer.CompanyId && row.Type == type).ToListAsync();
    }

    private static async Task<byte[]> SignedXmlAsync(DianSendHost worker, Document document) =>
        await worker.ReadFileAsync((await worker.FilesAsync(document.Id)).First(file => file.Kind == DocumentFileKind.SignedXml));

    private static Dictionary<string, byte[]> ZipEntries(byte[] zip)
    {
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        return archive.Entries.ToDictionary(entry => entry.FullName, entry =>
        {
            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        });
    }

    private static async Task AssignInvoicePlanAsync(Issuer issuer, long quantity)
    {
        var planId = Interlocked.Increment(ref planSequence);
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        scope.Db.Add(new Plan { Id = planId, Name = $"Plan {planId}", Period = PlanPeriod.Package, IsActive = true });
        scope.Db.Add(new PlanQuota { PlanId = planId, DocumentKind = DocumentKind.Invoice, Quantity = quantity });
        var companyPlan = new CompanyPlan { CompanyId = issuer.CompanyId, PlanId = planId, StartsAt = Api.Clock.UtcNow.AddDays(-1), IsActive = true };
        scope.Db.Add(companyPlan);
        await scope.Db.SaveChangesAsync();
        scope.Db.Add(new PlanUsage { CompanyPlanId = companyPlan.Id, DocumentKind = DocumentKind.Invoice, Used = 0 });
        await scope.Db.SaveChangesAsync();
    }

    private static async Task<long?> UsedAsync(Issuer issuer)
    {
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        var companyPlanIds = await CompanyApiTests.Rows<CompanyPlan>(scope.Db, issuer.CompanyId).Select(row => row.Id).ToListAsync();
        var used = await scope.Db.Set<PlanUsage>().IgnoreQueryFilters().AsNoTracking()
            .Where(row => companyPlanIds.Contains(row.CompanyPlanId) && row.DocumentKind == DocumentKind.Invoice)
            .Select(row => (long?)row.Used).SingleOrDefaultAsync();
        return used is null or 0 ? null : used;
    }
}
