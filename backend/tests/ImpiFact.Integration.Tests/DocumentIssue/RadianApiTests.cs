using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ImpiFact.Application.Abstractions;
using ImpiFact.Domain.Billing;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Documents;
using ImpiFact.Integration.Tests.Companies;
using ImpiFact.Integration.Tests.DianSend;
using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Integration.Tests.DocumentIssue;

/// <summary>
/// RADIAN through the real API (phase 7a, T6): received invoices loaded from the fake DIAN by their CUFE, the events of the
/// buyer (030 to 033) with the order rules of Anexo Técnico 1.9, the tacit acceptance (034) of the seller, the quota
/// <c>qty_docs_radian</c>, the PHP routes and the isolation between companies.
/// </summary>
[Category("Integration")]
public sealed class RadianApiTests
{
    private const string HabilitationUrl = "https://vpfe-hab.dian.gov.co/WcfDianCustomerServices.svc";

    private static int planSequence = 2100;

    private static int cufeSequence;

    private static CompanyApiFactory Api => DocumentIssueHost.Api;

    private static FakeDian Dian => Api.Dian;

    /// <summary>NIT of the buyer of the invoices the fake DIAN returns; each test points it to its company.</summary>
    private string buyerNit = "";

    private Func<string>? sendEventReply;

    private Func<string>? statusEventsReply;

    [SetUp]
    public void RouteTheFakeDian()
    {
        sendEventReply = () => RadianReplies.SendEvent(true);
        statusEventsReply = () => RadianReplies.StatusEvents();
        Dian.Route = request => request.Operation switch
        {
            "GetXmlByDocumentKey" => FakeDian.Response(RadianReplies.XmlByDocumentKey(RadianReplies.Invoice(buyerNit, request.Parameter("trackId")!))),
            "SendEventUpdateStatus" => FakeDian.Response(sendEventReply!()),
            "GetStatusEvent" => FakeDian.Response(statusEventsReply!()),
            _ => null,
        };
    }

    [TearDown]
    public void ResetTheFakeDian() => Dian.Route = null;

    [Test]
    public async Task A_received_invoice_is_loaded_from_the_dian_by_its_cufe_with_its_xml()
    {
        using var issuer = await NewIssuerAsync();
        var cufe = NewCufe();
        var before = Dian.Requests.Count;

        var (status, body) = await PostAsync(issuer.Client, "/api/v1/received-documents", new { cufe });

        var request = Dian.Requests.Skip(before).Single();
        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(201), body.ToString());
            Assert.That(request.Operation, Is.EqualTo("GetXmlByDocumentKey"));
            Assert.That(request.Parameter("trackId"), Is.EqualTo(cufe));
            Assert.That(request.Url.ToString(), Is.EqualTo(HabilitationUrl), "The events environment defaults to habilitación.");
            Assert.That(body.GetProperty("supplier_identification_number").GetString(), Is.EqualTo(RadianReplies.SellerNit));
            Assert.That(body.GetProperty("supplier_dv").GetString(), Is.EqualTo("7"));
            Assert.That(body.GetProperty("supplier_name").GetString(), Is.EqualTo(RadianReplies.SellerName));
            Assert.That(body.GetProperty("prefix").GetString(), Is.EqualTo("FPRU"));
            Assert.That(body.GetProperty("number").GetString(), Is.EqualTo("FPRU1001"));
            Assert.That(body.GetProperty("cufe").GetString(), Is.EqualTo(cufe));
            Assert.That(body.GetProperty("type_document_id").GetInt32(), Is.EqualTo(1));
            Assert.That(body.GetProperty("payable_amount").GetDecimal(), Is.EqualTo(119000m));
            Assert.That(body.GetProperty("tax_amount").GetDecimal(), Is.EqualTo(19000m));
            Assert.That(body.GetProperty("events").GetArrayLength(), Is.Zero);
        });

        var id = body.GetProperty("id").GetGuid();
        using var xml = await issuer.Client.GetAsync($"/api/v1/received-documents/{id}/xml");
        Assert.That(xml.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await xml.Content.ReadAsStringAsync(), Is.EqualTo(RadianReplies.Invoice(issuer.Nit, cufe)));

        var countBefore = Dian.Requests.Count;
        var (again, againBody) = await PostAsync(issuer.Client, "/api/v1/received-documents", new { cufe });
        Assert.Multiple(() =>
        {
            Assert.That(again, Is.EqualTo(200), againBody.ToString());
            Assert.That(againBody.GetProperty("id").GetGuid(), Is.EqualTo(id));
            Assert.That(Dian.Requests, Has.Count.EqualTo(countBefore), "A known document is not asked again.");
        });

        var (listed, list) = await GetAsync(issuer.Client, $"/api/v1/received-documents?supplier_nit={RadianReplies.SellerNit}");
        Assert.That(listed, Is.EqualTo(200));
        Assert.That(list.GetProperty("total_count").GetInt64(), Is.EqualTo(1));
        Assert.That(list.GetProperty("data")[0].GetProperty("id").GetGuid(), Is.EqualTo(id));
    }

    [Test]
    public async Task An_invoice_for_another_buyer_is_refused_and_not_stored()
    {
        using var issuer = await NewIssuerAsync();
        buyerNit = "811111111";

        var (status, body) = await PostAsync(issuer.Client, "/api/v1/received-documents", new { cufe = NewCufe() });

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(422), body.ToString());
            Assert.That(body.GetProperty("code").GetString(), Is.EqualTo("radian.wrong_buyer"));
            Assert.That(body.GetProperty("message").GetString(), Is.EqualTo($"El adquiriente del documento no corresponde a la empresa con nit: {issuer.Nit}"));
        });
        Assert.That(await ReceivedCountAsync(issuer), Is.Zero);
    }

    [Test]
    public async Task A_cufe_the_dian_does_not_have_answers_its_message()
    {
        using var issuer = await NewIssuerAsync();
        Dian.Reply(RadianReplies.XmlByDocumentKey(null, "205", "XML no encontrado"));

        var (status, body) = await PostAsync(issuer.Client, "/api/v1/received-documents", new { cufe = NewCufe() });

        Assert.That(status, Is.EqualTo(422), body.ToString());
        Assert.That(body.GetProperty("message").GetString(), Is.EqualTo("La DIAN no entregó el documento: XML no encontrado"));
    }

    [Test]
    public async Task The_buyer_events_are_signed_sent_and_recorded_in_order()
    {
        using var issuer = await NewIssuerAsync();
        var id = await LoadAsync(issuer);
        var before = Dian.Requests.Count;

        var (ack, ackBody) = await PostAsync(issuer.Client, $"/api/v1/received-documents/{id}/events", new
        {
            event_code = "030",
            issuer_party = new
            {
                identification_number = "1000000001", first_name = "PERSONA", last_name = "DE PRUEBA",
                organization_department = "CONTABILIDAD", job_title = "AUXILIAR CONTABLE",
            },
        });
        var (receipt, _) = await PostAsync(issuer.Client, $"/api/v1/received-documents/{id}/events", new { event_code = "032" });
        var (acceptance, acceptanceBody) = await PostAsync(issuer.Client, $"/api/v1/received-documents/{id}/events", new { event_code = "033" });

        var sent = Dian.Requests.Skip(before).ToList();
        var xml = EventXml(sent[0]);
        Assert.Multiple(() =>
        {
            Assert.That((ack, receipt, acceptance), Is.EqualTo((200, 200, 200)), ackBody.ToString());
            Assert.That(sent.Select(request => request.Operation), Is.EqualTo(new[] { "SendEventUpdateStatus", "SendEventUpdateStatus", "SendEventUpdateStatus" }));
            Assert.That(ackBody.GetProperty("success").GetBoolean(), Is.True);
            Assert.That(ackBody.GetProperty("message").GetString(), Is.EqualTo($"ApplicationResponse #{RadianReplies.SellerNit}FPRU1001030 generada con éxito"));
            Assert.That(ackBody.GetProperty("event").GetProperty("status").GetString(), Is.EqualTo("accepted"));
            Assert.That(ackBody.GetProperty("invoice_cufe").GetString(), Is.Not.Empty);
            Assert.That(xml, Does.Contain("<cbc:ResponseCode>030</cbc:ResponseCode>"));
            Assert.That(xml, Does.Contain($"<cbc:UUID schemeID=\"2\" schemeName=\"CUDE-SHA384\">{ackBody.GetProperty("cude").GetString()}</cbc:UUID>"));
            Assert.That(xml, Does.Contain("<cbc:FirstName>PERSONA</cbc:FirstName>"));
            Assert.That(xml, Does.Contain("<ds:SignatureValue ").And.Contain("<xades:SigningTime>"), "The event is signed.");
            Assert.That(ZipEntry(sent[0]), Does.StartWith("ar030").And.EndWith("FPRU1001.xml"));
            Assert.That(EventXml(sent[2]), Does.Contain("<cbc:ResponseCode>033</cbc:ResponseCode>"));
            Assert.That(acceptanceBody.GetProperty("event").GetProperty("event_name").GetString(), Is.EqualTo("Aceptación expresa"));
        });

        var (_, detail) = await GetAsync(issuer.Client, $"/api/v1/received-documents/{id}");
        Assert.That(detail.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("event_code").GetString()), Is.EqualTo(new[] { "030", "032", "033" }));
    }

    [Test]
    public async Task Events_out_of_order_answer_422_without_calling_the_dian()
    {
        using var issuer = await NewIssuerAsync();
        var id = await LoadAsync(issuer);
        var before = Dian.Requests.Count;

        var (early, earlyBody) = await PostAsync(issuer.Client, $"/api/v1/received-documents/{id}/events", new { event_code = "032" });
        var (tacit, tacitBody) = await PostAsync(issuer.Client, $"/api/v1/received-documents/{id}/events", new { event_code = "034" });

        Assert.Multiple(() =>
        {
            Assert.That(early, Is.EqualTo(422), earlyBody.ToString());
            Assert.That(earlyBody.GetProperty("code").GetString(), Is.EqualTo("radian.event_out_of_order"));
            Assert.That(earlyBody.GetProperty("message").GetString(), Is.EqualTo(
                "El evento 032 (Recibo del bien y/o prestación del servicio) requiere que antes se haya registrado el evento 030 " +
                "(Acuse de recibo de Factura Electrónica de Venta)."));
            Assert.That(tacit, Is.EqualTo(422), tacitBody.ToString());
            Assert.That(tacitBody.GetProperty("code").GetString(), Is.EqualTo("radian.event_not_allowed"));
            Assert.That(Dian.Requests, Has.Count.EqualTo(before));
        });

        await PostAsync(issuer.Client, $"/api/v1/received-documents/{id}/events", new { event_code = "030" });
        await PostAsync(issuer.Client, $"/api/v1/received-documents/{id}/events", new { event_code = "032" });
        await PostAsync(issuer.Client, $"/api/v1/received-documents/{id}/events", new { event_code = "031", claim_reason = "02" });
        var (accept, acceptBody) = await PostAsync(issuer.Client, $"/api/v1/received-documents/{id}/events", new { event_code = "033" });
        var (repeat, repeatBody) = await PostAsync(issuer.Client, $"/api/v1/received-documents/{id}/events", new { event_code = "030" });

        Assert.Multiple(() =>
        {
            Assert.That(accept, Is.EqualTo(422), acceptBody.ToString());
            Assert.That(acceptBody.GetProperty("message").GetString(), Does.Contain("ya tiene el evento 031"));
            Assert.That(repeat, Is.EqualTo(422));
            Assert.That(repeatBody.GetProperty("message").GetString(), Is.EqualTo("Ya se registro este evento para este documento."));
        });
    }

    [Test]
    public async Task A_claim_needs_its_reason()
    {
        using var issuer = await NewIssuerAsync();
        var id = await LoadAsync(issuer);

        var (status, body) = await PostAsync(issuer.Client, $"/api/v1/received-documents/{id}/events", new { event_code = "031" });

        Assert.That(status, Is.EqualTo(422), body.ToString());
        Assert.That(body.GetProperty("errors").GetProperty("claim_reason")[0].GetString(),
            Is.EqualTo("El reclamo (031) requiere el motivo: 01, 02, 03 o 04."));
    }

    [Test]
    public async Task A_rejected_event_is_recorded_with_the_rules_and_can_be_sent_again()
    {
        using var issuer = await NewIssuerAsync();
        var id = await LoadAsync(issuer);
        sendEventReply = () => RadianReplies.SendEvent(false, "Regla: AAH59, Rechazo: El CUFE no existe");

        var (rejected, rejectedBody) = await PostAsync(issuer.Client, $"/api/v1/received-documents/{id}/events", new { event_code = "030" });
        sendEventReply = () => RadianReplies.SendEvent(true);
        var (accepted, acceptedBody) = await PostAsync(issuer.Client, $"/api/v1/received-documents/{id}/events", new { event_code = "030" });

        Assert.Multiple(() =>
        {
            Assert.That(rejected, Is.EqualTo(200), rejectedBody.ToString());
            Assert.That(rejectedBody.GetProperty("success").GetBoolean(), Is.False);
            Assert.That(rejectedBody.GetProperty("event").GetProperty("status").GetString(), Is.EqualTo("rejected"));
            Assert.That(rejectedBody.GetProperty("event").GetProperty("error_messages")[0].GetString(), Is.EqualTo("Regla: AAH59, Rechazo: El CUFE no existe"));
            Assert.That(accepted, Is.EqualTo(200), acceptedBody.ToString());
            Assert.That(acceptedBody.GetProperty("event").GetProperty("status").GetString(), Is.EqualTo("accepted"));
            Assert.That(acceptedBody.GetProperty("event").GetProperty("id").GetGuid(),
                Is.EqualTo(rejectedBody.GetProperty("event").GetProperty("id").GetGuid()), "One row per event type.");
        });

        await using var scope = Api.OpenScope(null, null, ActorType.System);
        var rows = await CompanyApiTests.Rows<DocumentEvent>(scope.Db, issuer.CompanyId).ToListAsync();
        Assert.That(rows.Select(row => (row.Status, row.XmlFileKey is not null, row.ResponseFileKey is not null)),
            Is.EqualTo(new[] { (DocumentEventStatus.Accepted, true, true) }));
    }

    [Test]
    public async Task Another_company_cannot_see_nor_act_on_a_received_document()
    {
        using var owner = await NewIssuerAsync();
        var id = await LoadAsync(owner);
        using var other = await NewIssuerAsync();
        var before = Dian.Requests.Count;

        var (get, getBody) = await GetAsync(other.Client, $"/api/v1/received-documents/{id}");
        var (missing, missingBody) = await GetAsync(other.Client, $"/api/v1/received-documents/{Guid.CreateVersion7()}");
        using var xml = await other.Client.GetAsync($"/api/v1/received-documents/{id}/xml");
        var (send, sendBody) = await PostAsync(other.Client, $"/api/v1/received-documents/{id}/events", new { event_code = "030" });
        var (dianEvents, _) = await GetAsync(other.Client, $"/api/v1/received-documents/{id}/dian-events");
        var (list, listBody) = await GetAsync(other.Client, "/api/v1/received-documents");

        Assert.Multiple(() =>
        {
            Assert.That((get, send, (int)xml.StatusCode, dianEvents), Is.EqualTo((404, 404, 404, 404)));
            Assert.That(getBody.GetProperty("code").GetString(), Is.EqualTo(missingBody.GetProperty("code").GetString()));
            Assert.That(getBody.GetProperty("message").GetString(), Is.EqualTo(missingBody.GetProperty("message").GetString()));
            Assert.That(missing, Is.EqualTo(404));
            Assert.That(sendBody.GetProperty("code").GetString(), Is.EqualTo("received_document.not_found"));
            Assert.That(list, Is.EqualTo(200));
            Assert.That(listBody.GetProperty("total_count").GetInt64(), Is.Zero);
            Assert.That(Dian.Requests, Has.Count.EqualTo(before), "Nothing reaches the DIAN for another company's document.");
        });
    }

    [Test]
    public async Task The_events_count_in_the_radian_quota_and_an_exhausted_quota_refuses_them()
    {
        using var issuer = await NewIssuerAsync();
        var id = await LoadAsync(issuer);
        var companyPlanId = await AssignEventsPlanAsync(issuer, quantity: 2, used: 1);

        var (first, firstBody) = await PostAsync(issuer.Client, $"/api/v1/received-documents/{id}/events", new { event_code = "030" });
        var before = Dian.Requests.Count;
        var (second, secondBody) = await PostAsync(issuer.Client, $"/api/v1/received-documents/{id}/events", new { event_code = "032" });

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(200), firstBody.ToString());
            Assert.That(second, Is.EqualTo(403), secondBody.ToString());
            Assert.That(secondBody.GetProperty("code").GetString(), Is.EqualTo("plan.limit_reached"));
            Assert.That(Dian.Requests, Has.Count.EqualTo(before));
        });
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        var usage = await scope.Db.Set<PlanUsage>().IgnoreQueryFilters()
            .Where(row => row.CompanyPlanId == companyPlanId && row.DocumentKind == DocumentKind.Events).SingleAsync();
        Assert.That(usage.Used, Is.EqualTo(2));
    }

    [Test]
    public async Task The_php_routes_load_and_send_by_cufe_and_from_the_attached_document()
    {
        using var issuer = await NewIssuerAsync();
        var cufe = NewCufe();

        var (loaded, loadedBody) = await PostAsync(issuer.Client, "/api/load-document-by-cufe", new { cufe, saved = true });
        var (again, againBody) = await PostAsync(issuer.Client, "/api/load-document-by-cufe", new { cufe });
        var (sent, sentBody) = await PostAsync(issuer.Client, "/api/ubl2.1/send-event-data", new
        {
            event_id = "1", document_reference = new { cufe },
        });

        var attachedCufe = NewCufe();
        var attached = Convert.ToBase64String(Encoding.UTF8.GetBytes(RadianReplies.AttachedDocument(RadianReplies.Invoice(issuer.Nit, attachedCufe, "FPRU2002"))));
        var (fromFile, fromFileBody) = await PostAsync(issuer.Client, "/api/ubl2.1/send-event", new
        {
            event_id = 1, base64_attacheddocument = attached, base64_attacheddocument_name = "../../evil.xml",
        });
        var (redacted, redactedBody) = await PostAsync(issuer.Client, "/api/ubl2.1/send-event", new
        {
            event_id = 1, base64_attacheddocument = "REDACTED_BASE64", base64_attacheddocument_name = "ad.xml",
        });

        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.EqualTo(200), loadedBody.ToString());
            Assert.That(loadedBody.GetProperty("message").GetString(), Is.EqualTo("El documento fue cargado y guardado satisfactoriamente."));
            Assert.That(loadedBody.GetProperty("saved").GetBoolean(), Is.True);
            Assert.That(againBody.GetProperty("message").GetString(), Is.EqualTo("El documento ya existe en la base de datos."));
            Assert.That(againBody.GetProperty("already_exists").GetBoolean(), Is.True);
            Assert.That(again, Is.EqualTo(200));
            Assert.That(sent, Is.EqualTo(200), sentBody.ToString());
            Assert.That(sentBody.GetProperty("success").GetBoolean(), Is.True);
            Assert.That(sentBody.GetProperty("transmitter_id").GetString(), Is.EqualTo(RadianReplies.SellerNit));
            Assert.That(sentBody.GetProperty("invoice_number").GetString(), Is.EqualTo("FPRU1001"));
            Assert.That(fromFile, Is.EqualTo(200), fromFileBody.ToString());
            Assert.That(fromFileBody.GetProperty("invoice_cufe").GetString(), Is.EqualTo(attachedCufe));
            Assert.That(redacted, Is.EqualTo(422), redactedBody.ToString());
            Assert.That(redactedBody.GetProperty("message").GetString(), Is.EqualTo("La solicitud tiene datos inválidos."));
        });
        Assert.That(await ReceivedCountAsync(issuer), Is.EqualTo(2));

        using var file = await issuer.Client.GetAsync($"/api/receivedfile/{issuer.Nit}/{attachedCufe}.xml");
        using var foreign = await issuer.Client.GetAsync($"/api/receivedfile/900999999/{attachedCufe}.xml");
        Assert.That(file.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(foreign.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task The_events_of_another_company_cufe_are_not_asked_to_the_dian()
    {
        using var issuer = await NewIssuerAsync();
        var id = await LoadAsync(issuer);
        var (_, document) = await GetAsync(issuer.Client, $"/api/v1/received-documents/{id}");
        statusEventsReply = () => RadianReplies.StatusEvents(("030", new DateOnly(2026, 1, 12)));
        var before = Dian.Requests.Count;

        var (own, ownBody) = await PostAsync(issuer.Client, $"/api/ubl2.1/status/events-document/{document.GetProperty("cufe").GetString()}", new { });
        var afterOwn = Dian.Requests.Count;
        var (foreign, _) = await PostAsync(issuer.Client, $"/api/ubl2.1/status/events-document/{NewCufe()}", new { });

        Assert.Multiple(() =>
        {
            Assert.That(own, Is.EqualTo(200), ownBody.ToString());
            Assert.That(ownBody.GetProperty("events")[0].GetProperty("dian_code").GetString(), Is.EqualTo("030"));
            Assert.That(afterOwn - before, Is.EqualTo(1));
            Assert.That(foreign, Is.EqualTo(404));
            Assert.That(Dian.Requests, Has.Count.EqualTo(afterOwn));
        });
    }

    // Clock of the host: Thursday 2026-01-15. A 032 of Friday 2026-01-09 has three business days (12, 13 and 14) behind it.
    [Test]
    public async Task The_seller_registers_the_tacit_acceptance_of_its_accepted_invoice()
    {
        using var issuer = await NewIssuerAsync();
        var documentId = await AcceptedInvoiceAsync(issuer);
        statusEventsReply = () => RadianReplies.StatusEvents(("030", new DateOnly(2026, 1, 9)), ("032", new DateOnly(2026, 1, 9)));
        var before = Dian.Requests.Count;

        var (status, body) = await PostAsync(issuer.Client, $"/api/v1/documents/{documentId}/events", new { event_code = "034" });

        var sent = Dian.Requests.Skip(before).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(200), body.ToString());
            Assert.That(sent.Select(request => request.Operation), Is.EqualTo(new[] { "GetStatusEvent", "SendEventUpdateStatus" }));
            Assert.That(body.GetProperty("success").GetBoolean(), Is.True);
            Assert.That(EventXml(sent[1]), Does.Contain("<cbc:Note>Manifiesto bajo la gravedad de juramento"));
            Assert.That(EventXml(sent[1]), Does.Contain(">800197268</cbc:CompanyID>"));
        });
        var (_, events) = await GetAsync(issuer.Client, $"/api/v1/documents/{documentId}/events");
        Assert.That(events.GetProperty("data")[0].GetProperty("event_code").GetString(), Is.EqualTo("034"));
    }

    [Test]
    public async Task The_tacit_acceptance_waits_three_business_days_after_the_receipt_of_the_goods()
    {
        using var issuer = await NewIssuerAsync();
        var documentId = await AcceptedInvoiceAsync(issuer);
        statusEventsReply = () => RadianReplies.StatusEvents(("030", new DateOnly(2026, 1, 13)), ("032", new DateOnly(2026, 1, 13)));

        var (status, body) = await PostAsync(issuer.Client, $"/api/v1/documents/{documentId}/events", new { event_code = "034" });

        Assert.That(status, Is.EqualTo(422), body.ToString());
        Assert.That(body.GetProperty("code").GetString(), Is.EqualTo("radian.tacit_acceptance_too_early"));
        Assert.That(body.GetProperty("message").GetString(), Does.Contain("no antes del 2026-01-17"));
    }

    /// <summary>A new company; the invoices the fake DIAN returns are addressed to it.</summary>
    private async Task<Issuer> NewIssuerAsync()
    {
        var (nit, dv) = CompaniesHost.NewNit();
        var issuer = await IssuerSetup.CreateAsync(nit, dv);
        buyerNit = issuer.Nit;
        return issuer;
    }

    private async Task<Guid> LoadAsync(Issuer issuer)
    {
        buyerNit = issuer.Nit;
        var (status, body) = await PostAsync(issuer.Client, "/api/v1/received-documents", new { cufe = NewCufe() });
        Assert.That(status, Is.EqualTo(201), body.ToString());
        return body.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> AcceptedInvoiceAsync(Issuer issuer)
    {
        var body = File.ReadAllBytes(Path.Combine(GoldenIssueTests.GoldenRoot(), "invoice", "single-line-iva19", "input.json"));
        using var content = new ByteArrayContent(body);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        using var response = await issuer.Client.PostAsync("/api/ubl2.1/invoice", content);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        Guid id;
        using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            id = json.RootElement.GetProperty("document").GetProperty("id").GetGuid();
        }

        await using var scope = Api.OpenScope(null, null, ActorType.System);
        await scope.Db.Set<Document>().IgnoreQueryFilters().Where(row => row.Id == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Status, DocumentStatus.Accepted));
        return id;
    }

    private static async Task<Guid> AssignEventsPlanAsync(Issuer issuer, long quantity, long used)
    {
        var planId = Interlocked.Increment(ref planSequence);
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        scope.Db.Add(new Plan { Id = planId, Name = $"Plan RADIAN {planId}", Period = PlanPeriod.Package, IsActive = true });
        scope.Db.Add(new PlanQuota { PlanId = planId, DocumentKind = DocumentKind.Events, Quantity = quantity });
        var companyPlan = new CompanyPlan { CompanyId = issuer.CompanyId, PlanId = planId, StartsAt = Api.Clock.UtcNow.AddDays(-1), IsActive = true };
        scope.Db.Add(companyPlan);
        await scope.Db.SaveChangesAsync();
        scope.Db.Add(new PlanUsage { CompanyPlanId = companyPlan.Id, DocumentKind = DocumentKind.Events, Used = used });
        await scope.Db.SaveChangesAsync();
        return companyPlan.Id;
    }

    private static async Task<int> ReceivedCountAsync(Issuer issuer)
    {
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        return await CompanyApiTests.Rows<ReceivedDocument>(scope.Db, issuer.CompanyId).CountAsync();
    }

    private static string NewCufe() =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA384.HashData(BitConverter.GetBytes(Interlocked.Increment(ref cufeSequence) + Environment.TickCount64)));

    private static string EventXml(FakeDianRequest request)
    {
        using var zip = new ZipArchive(new MemoryStream(Convert.FromBase64String(request.Parameter("contentFile")!)));
        using var reader = new StreamReader(zip.Entries.Single().Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string ZipEntry(FakeDianRequest request)
    {
        using var zip = new ZipArchive(new MemoryStream(Convert.FromBase64String(request.Parameter("contentFile")!)));
        return zip.Entries.Single().FullName;
    }

    private static async Task<(int Status, JsonElement Body)> PostAsync(HttpClient client, string path, object body)
    {
        using var response = await client.PostAsJsonAsync(path, body);
        return (await ReadAsync(response));
    }

    private static async Task<(int Status, JsonElement Body)> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        return await ReadAsync(response);
    }

    private static async Task<(int Status, JsonElement Body)> ReadAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(string.IsNullOrEmpty(text) ? "{}" : text);
        return ((int)response.StatusCode, json.RootElement.Clone());
    }
}
