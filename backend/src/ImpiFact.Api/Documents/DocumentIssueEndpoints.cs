using ImpiFact.Api.Endpoints;
using ImpiFact.Api.Http;
using ImpiFact.Api.OpenApi;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Features.Documents.Calculate;
using ImpiFact.Application.Features.Documents.Issue;
using ImpiFact.Application.Features.Documents.Preview;
using Microsoft.OpenApi;

namespace ImpiFact.Api.Documents;

/// <summary>
/// Emisión de factura, nota crédito y nota débito (Arquitectura detallada, sección 7; anexo A.1 a A.3) y el cálculo de
/// sus totales antes de emitir (<c>POST /api/v1/documents/calculate</c>) y la vista previa del PDF de la factura y de las
/// notas (<c>POST /api/v1/documents/invoices/preview</c>, <c>credit-notes/preview</c> y <c>debit-notes/preview</c>). Todas
/// piden el alcance
/// <c>documents:write</c>; la empresa es siempre la de la credencial, porque el cuerpo (con la forma del PHP) no trae el
/// NIT del emisor. Las variantes con <c>test-set/{testSetId}</c> envían al set de pruebas de habilitación. Las rutas
/// heredadas <c>/api/ubl2.1/...</c> se borraron en la fase 7a (T9b2). El encabezado <c>Idempotency-Key</c> es opcional. Cada endpoint solo lee el cuerpo, arma la solicitud, llama al caso de uso y
/// traduce el resultado.
/// </summary>
internal sealed class DocumentIssueEndpoints : IEndpointModule
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    /// <summary>Lo que comparten las tres emisiones: respuesta inmediata, validaciones y errores principales.</summary>
    private const string IssueRules =
        " La respuesta es inmediata: el documento queda firmado, con su CUFE o CUDE, y en cola (`Queued`); el Worker lo " +
        "envía a la DIAN y el resultado se consulta después con `GET /api/v1/documents/{id}`. Antes de emitir se revisa: " +
        "empresa activa (403 `company.inactive`), resolución activa del tipo y el prefijo, certificado vigente (422 " +
        "`certificate.missing` o `certificate.expired`), software del ambiente actual (422 `software.missing`), plan de " +
        "la empresa (403 `plan.inactive` o `plan.limit_reached`) y los totales con las reglas de la DIAN (422 " +
        "`validation.failed`, con `errors` por campo). Sin `number` se asigna el siguiente de la resolución; un número ya " +
        "usado con otra solicitud responde 409 (`document.number_in_use`) y un rango agotado, 422. Acepta el encabezado " +
        "opcional `Idempotency-Key` (ver la introducción). Con `sendmail` el correo al comprador sale después de la " +
        "aceptación.";

    /// <summary>Regla de las notas: corrigen una factura ya aceptada.</summary>
    private const string NoteRules =
        " Si `billing_reference.uuid` es el CUFE de una factura de la empresa en ImpiFact, esa factura debe estar aceptada " +
        "por la DIAN (`Accepted` o `AcceptedWithWarnings`); si no, 422 (`document.reference_not_accepted`).";

    /// <summary>Lo que cambia en las rutas del set de pruebas de habilitación.</summary>
    private const string TestSetRules =
        " Envía el documento dentro del set de pruebas de habilitación `{testSetId}` que dio la DIAN al registrar el " +
        "software: el Worker usa SendTestSetAsync en vez de SendBillSync. Estos documentos no pasan por el plan ni cuentan " +
        "en su consumo.";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var canonical = endpoints.MapGroup("/api/v1/documents").WithTags(ApiTags.Documents).RequireAuthorization(ApiScopes.DocumentsWrite);
        Issue(canonical.MapPost("/invoices", Invoice), OpenApiExamples.Invoices)
            .WithOperationId("issueInvoice")
            .WithSummary("Emitir una factura")
            .WithDescription(InvoiceText + HealthRules + IssueRules);
        Issue(canonical.MapPost("/invoices/test-set/{testSetId}", Invoice), OpenApiExamples.Invoices)
            .WithOperationId("issueTestSetInvoice")
            .WithSummary("Emitir una factura del set de pruebas")
            .WithDescription(InvoiceText + HealthRules + TestSetRules + IssueRules);
        // Variants of the invoice (php-rules, section 10): one route per variant under /invoices, like the test set and the
        // preview, because each has its own required fields and fixed operation.
        Issue(canonical.MapPost("/invoices/aiu", AiuInvoice), OpenApiExamples.AiuInvoices)
            .WithOperationId("issueAiuInvoice")
            .WithSummary("Emitir una factura AIU")
            .WithDescription(AiuInvoiceText + VariantRules + IssueRules);
        Issue(canonical.MapPost("/invoices/aiu/test-set/{testSetId}", AiuInvoice), OpenApiExamples.AiuInvoices)
            .WithOperationId("issueTestSetAiuInvoice")
            .WithSummary("Emitir una factura AIU del set de pruebas")
            .WithDescription(AiuInvoiceText + VariantRules + TestSetRules + IssueRules);
        Issue(canonical.MapPost("/invoices/mandate", MandateInvoice), OpenApiExamples.MandateInvoices)
            .WithOperationId("issueMandateInvoice")
            .WithSummary("Emitir una factura de mandato")
            .WithDescription(MandateInvoiceText + VariantRules + IssueRules);
        Issue(canonical.MapPost("/invoices/mandate/test-set/{testSetId}", MandateInvoice), OpenApiExamples.MandateInvoices)
            .WithOperationId("issueTestSetMandateInvoice")
            .WithSummary("Emitir una factura de mandato del set de pruebas")
            .WithDescription(MandateInvoiceText + VariantRules + TestSetRules + IssueRules);
        Issue(canonical.MapPost("/invoices/transport", TransportInvoice), OpenApiExamples.TransportInvoices)
            .WithOperationId("issueTransportInvoice")
            .WithSummary("Emitir una factura de transporte de carga")
            .WithDescription(TransportInvoiceText + VariantRules + IssueRules);
        Issue(canonical.MapPost("/invoices/transport/test-set/{testSetId}", TransportInvoice), OpenApiExamples.TransportInvoices)
            .WithOperationId("issueTestSetTransportInvoice")
            .WithSummary("Emitir una factura de transporte de carga del set de pruebas")
            .WithDescription(TransportInvoiceText + VariantRules + TestSetRules + IssueRules);
        Issue(canonical.MapPost("/invoices/export", ExportInvoice), OpenApiExamples.ExportInvoices)
            .WithOperationId("issueExportInvoice")
            .WithSummary("Emitir una factura de exportación")
            .WithDescription(ExportInvoiceText + ExportRules + IssueRules);
        Issue(canonical.MapPost("/invoices/export/test-set/{testSetId}", ExportInvoice), OpenApiExamples.ExportInvoices)
            .WithOperationId("issueTestSetExportInvoice")
            .WithSummary("Emitir una factura de exportación del set de pruebas")
            .WithDescription(ExportInvoiceText + ExportRules + TestSetRules + IssueRules);
        // Contingency invoices (php-rules, section 12): type 03, the transcription of a paper invoice, and type 04, issued
        // while the DIAN does not answer; «dian-contingency» names who has the problem, like the annex (section 12).
        Issue(canonical.MapPost("/invoices/contingency", ContingencyInvoice), OpenApiExamples.ContingencyInvoices)
            .WithOperationId("issueContingencyInvoice")
            .WithSummary("Emitir una factura de contingencia (tipo 03)")
            .WithDescription(ContingencyInvoiceText + IssueRules);
        Issue(canonical.MapPost("/invoices/contingency/test-set/{testSetId}", ContingencyInvoice), OpenApiExamples.ContingencyInvoices)
            .WithOperationId("issueTestSetContingencyInvoice")
            .WithSummary("Emitir una factura de contingencia (tipo 03) del set de pruebas")
            .WithDescription(ContingencyInvoiceText + TestSetRules + IssueRules);
        Issue(canonical.MapPost("/invoices/dian-contingency", DianContingencyInvoice), OpenApiExamples.DianContingencyInvoices)
            .WithOperationId("issueDianContingencyInvoice")
            .WithSummary("Emitir una factura tipo 04 (contingencia de la DIAN)")
            .WithDescription(DianContingencyInvoiceText);
        canonical.MapPost("/invoices/dian-contingency/send-pending", SendPendingContingency)
            .Produces<SendPendingContingencyInvoicesResponse>()
            .WithOperationId("sendPendingContingencyInvoices")
            .WithSummary("Enviar las facturas tipo 04 pendientes")
            .WithDescription(SendPendingText);
        Issue(canonical.MapPost("/credit-notes", CreditNote), OpenApiExamples.CreditNotes)
            .WithOperationId("issueCreditNote")
            .WithSummary("Emitir una nota crédito")
            .WithDescription(CreditNoteText + NoteRules + IssueRules);
        Issue(canonical.MapPost("/credit-notes/test-set/{testSetId}", CreditNote), OpenApiExamples.CreditNotes)
            .WithOperationId("issueTestSetCreditNote")
            .WithSummary("Emitir una nota crédito del set de pruebas")
            .WithDescription(CreditNoteText + TestSetRules + IssueRules);
        Issue(canonical.MapPost("/debit-notes", DebitNote), OpenApiExamples.DebitNotes)
            .WithOperationId("issueDebitNote")
            .WithSummary("Emitir una nota débito")
            .WithDescription(DebitNoteText + NoteRules + IssueRules);
        Issue(canonical.MapPost("/debit-notes/test-set/{testSetId}", DebitNote), OpenApiExamples.DebitNotes)
            .WithOperationId("issueTestSetDebitNote")
            .WithSummary("Emitir una nota débito del set de pruebas")
            .WithDescription(DebitNoteText + TestSetRules + IssueRules);

        // Support document family (php-rules 13 and 14): its own collection, because the seller replaces the customer and it
        // uses the support-document software, environment, resolutions and plan quota; the adjustment note lives under it.
        Issue(canonical.MapPost("/support-documents", SupportDocument), ExampleBodies.SupportDocuments)
            .WithOperationId("issueSupportDocument")
            .WithSummary("Emitir un documento soporte")
            .WithDescription(SupportDocumentText + SupportDocumentRules + IssueRules);
        Issue(canonical.MapPost("/support-documents/test-set/{testSetId}", SupportDocument), ExampleBodies.SupportDocuments)
            .WithOperationId("issueTestSetSupportDocument")
            .WithSummary("Emitir un documento soporte del set de pruebas")
            .WithDescription(SupportDocumentText + SupportDocumentRules + TestSetRules + IssueRules);
        Issue(canonical.MapPost("/support-documents/adjustment-notes", SupportDocumentAdjustmentNote), ExampleBodies.SupportDocumentAdjustmentNotes)
            .WithOperationId("issueSupportDocumentAdjustmentNote")
            .WithSummary("Emitir una nota de ajuste al documento soporte")
            .WithDescription(AdjustmentNoteText + SupportDocumentRules + IssueRules);
        Issue(canonical.MapPost("/support-documents/adjustment-notes/test-set/{testSetId}", SupportDocumentAdjustmentNote),
                ExampleBodies.SupportDocumentAdjustmentNotes)
            .WithOperationId("issueTestSetSupportDocumentAdjustmentNote")
            .WithSummary("Emitir una nota de ajuste al documento soporte del set de pruebas")
            .WithDescription(AdjustmentNoteText + SupportDocumentRules + TestSetRules + IssueRules);

        Issue(canonical.MapPost("/invoices/preview", PreviewInvoice), OpenApiExamples.Invoices, producesPdf: true)
            .WithOperationId("previewInvoice")
            .WithSummary("Ver la vista previa del PDF de una factura")
            .WithDescription(PreviewText);
        Issue(canonical.MapPost("/credit-notes/preview", PreviewCreditNote), OpenApiExamples.CreditNotes, producesPdf: true)
            .WithOperationId("previewCreditNote")
            .WithSummary("Ver la vista previa del PDF de una nota crédito")
            .WithDescription(NotePreviewText("nota crédito", "issueCreditNote"));
        Issue(canonical.MapPost("/debit-notes/preview", PreviewDebitNote), OpenApiExamples.DebitNotes, producesPdf: true)
            .WithOperationId("previewDebitNote")
            .WithSummary("Ver la vista previa del PDF de una nota débito")
            .WithDescription(NotePreviewText("nota débito", "issueDebitNote"));

        canonical.MapPost("/calculate", Calculate)
            .Produces<DocumentTotalsCalculation>()
            .WithOperationId("calculateDocumentTotals")
            .WithSummary("Calcular los totales de un documento")
            .WithDescription(CalculateText)
            .WithRequestExamples(ExampleBodies.CalculateTotals);
    }

    /// <summary>
    /// Por qué el cálculo pide <c>documents:write</c> y no <c>documents:read</c>: es el primer paso de una emisión (el
    /// panel lo llama mientras arma el documento que va a emitir) y no consulta nada guardado; una credencial de solo
    /// consulta no emite, así que tampoco lo necesita.
    /// </summary>
    private const string CalculateText =
        "Calcula los valores de una factura, nota crédito o nota débito a partir de sus líneas: el valor de cada línea " +
        "(precio × cantidad − descuentos + cargos), sus impuestos, los impuestos del documento agrupados como en el XML " +
        "de la DIAN (un grupo por `tax_id` con un subtotal por tarifa) y `legal_monetary_totals`. Cada importe sale con " +
        "2 decimales, redondeado como se escribe en el XML, y los totales suman esos valores, así que lo calculado pasa " +
        "las reglas de totales de la emisión: el panel lo muestra y lo envía tal cual al emitir, sin hacer cuentas. En la " +
        "nota débito, `legal_monetary_totals` se envía como `requested_monetary_totals`. Los descuentos y cargos globales " +
        "no cambian los impuestos; los anticipos (`prepaid_payments`) suman `pre_paid_amount`, que no se resta del valor " +
        "a pagar. Es cálculo puro: no toma consecutivo, no guarda nada, no llama a la DIAN ni consume el plan. Pide " +
        "`documents:write`, porque es parte de emitir, y una credencial de empresa. Datos inválidos: 422 " +
        "(`validation.failed`, con `errors` por campo, por ejemplo `lines[0].taxes[0].percent`).";

    /// <summary>
    /// Por qué la vista previa pide <c>documents:write</c>: es parte de emitir (el botón «Vista previa» del formulario de
    /// emisión) y recibe los datos de un documento que todavía no existe.
    /// </summary>
    private const string PreviewText =
        "Genera el PDF que tendría la factura con el mismo cuerpo de `issueInvoice`, sin emitirla (el " +
        "`invoice/preeliminar-view` de ApiDian). Revisa lo mismo que la emisión, salvo lo que solo sirve para firmar y " +
        "enviar: forma del cuerpo, empresa activa (403 `company.inactive`), resolución activa del tipo y el prefijo, " +
        "catálogos y totales con las reglas de la DIAN (422 `validation.failed`, con `errors` por campo). No pide " +
        "certificado ni software, no toma número del consecutivo, no calcula el CUFE, no guarda nada, no consume el plan " +
        "y no llama a la DIAN; ignora `Idempotency-Key`. El PDF lleva la marca «VISTA PREVIA - SIN VALIDEZ», el número " +
        "`{prefijo} - BORRADOR`, «VISTA PREVIA - SIN CUFE» en lugar del CUFE, un QR de muestra y el logo de la empresa " +
        "si tiene. Responde `application/pdf` para abrir en el navegador (`Content-Disposition: inline`, " +
        "`Cache-Control: no-store`). Si el conversor de PDF no responde: 503 (`pdf.unavailable`, reintente después de " +
        "`Retry-After`). Pide `documents:write`.";

    /// <summary>La vista previa de una nota: la misma de la factura, con el cuerpo de la nota y su CUDE.</summary>
    private static string NotePreviewText(string name, string issueOperation) =>
        $"Genera el PDF que tendría la {name} con el mismo cuerpo de `{issueOperation}`, sin emitirla. Revisa lo mismo que " +
        "la emisión, salvo lo que solo sirve para firmar y enviar: forma del cuerpo, empresa activa (403 " +
        "`company.inactive`), resolución activa del tipo y el prefijo, catálogos y totales con las reglas de la DIAN (422 " +
        "`validation.failed`, con `errors` por campo). Tampoco revisa que la factura de `billing_reference` esté aceptada " +
        "(eso se revisa al emitir). No pide certificado ni software, no toma número del consecutivo, no calcula el CUDE, no " +
        "guarda nada, no consume el plan y no llama a la DIAN; ignora `Idempotency-Key`. El PDF lleva la marca «VISTA " +
        "PREVIA - SIN VALIDEZ», el número `{prefijo} - BORRADOR`, «VISTA PREVIA - SIN CUDE» en lugar del CUDE, un QR de " +
        "muestra y el logo de la empresa si tiene. Responde `application/pdf` para abrir en el navegador " +
        "(`Content-Disposition: inline`, `Cache-Control: no-store`). Si el conversor de PDF no responde: 503 " +
        "(`pdf.unavailable`, reintente después de `Retry-After`). Pide `documents:write`.";

    private const string InvoiceText =
        "Emite una factura electrónica de venta. El cuerpo sigue el contrato de ApiDian: `customer` (comprador), " +
        "`invoice_lines`, `legal_monetary_totals`, `tax_totals`, `payment_form` y, si quiere, `number`, `prefix`, " +
        "`resolution_number`, `date` y `time` (hora de Colombia; sin ellas, la fecha y hora actuales). `payment_form` es " +
        "una forma de pago (objeto) o varias (lista); cada una va en el XML como un medio de pago, y sin `payment_form` se " +
        "usa contado en efectivo. Los anticipos ya " +
        "recibidos van en `prepaid_payment` (uno) o `prepaid_payments` (varios), cada uno con `idpayment`, `paidamount` y " +
        "`receiveddate` (`yyyy-MM-dd`) obligatorios y `paiddate` e `instructionid` opcionales; su suma va en " +
        "`legal_monetary_totals.pre_paid_amount` (obligatorio con `prepaid_payment`) y no se resta de `payable_amount`, " +
        "como lo pide la regla FAU14 de la DIAN.";

    /// <summary>The optional health-sector fields of the national invoice (phase 7a, T7; php-rules 17).</summary>
    private const string HealthRules =
        " **Sector salud (sin RIPS)**: con `health_fields` la factura es del sector salud, como la define el Documento Técnico 2 " +
        "v001 del Ministerio de Salud (Resolución 948 de 2026). Lleva `invoice_period_start_date` e `invoice_period_end_date` " +
        "(`yyyy-MM-dd`, el fin igual o posterior al inicio) y `health_type_operation_id` (1 SS-CUFE, 2 SS-CUDE, 3 SS-POS, " +
        "4 SS-SNum, 5 SS-Recaudo, 6 SS-Reporte, 7 SS-SinAporte), que va en `CustomizationID` en lugar de `type_operation_id`. " +
        "`users_info` trae los usuarios: al menos uno salvo en 5 (que no admite usuarios ni lleva la extensión de salud) y en 7. " +
        "Con usuarios, la factura lleva la extensión de salud con una sola colección, así que todos deben tener el mismo " +
        "`provider_code` (código del prestador), `health_contracting_payment_method_id` (tabla modalidadPago de SISPRO, 1 a 4), " +
        "`health_coverage_id` (tabla coberturaPlan, 1 a 17), `contract_number` o `policy_number` (no los dos) y, sin contrato, " +
        "`health_invoice_without_contract_id` (tabla facturaSinContrato, 1 a 7). Los datos del paciente (documento, nombres, " +
        "autorizaciones, MIPRES) son del RIPS y no van en el XML. El copago, la cuota moderadora, los pagos compartidos y los " +
        "anticipos van como anticipos (`prepaid_payment` o `prepaid_payments`), cada uno con `prepaid_payment_type_id` " +
        "(1 copago, 2 cuota moderadora, 3 pagos compartidos, 4 anticipo, 5 no aplica) y una sola vez por concepto. Sin " +
        "`health_fields`, la factura es la de siempre.";

    /// <summary>What the three invoice variants share: they are type 1 invoices with a fixed operation.</summary>
    private const string VariantRules =
        " Es una factura de venta (`type_document_id` 1): usa la misma resolución, el mismo consecutivo y el mismo CUFE que " +
        "`issueInvoice`, y cuenta en el plan como una factura. La operación es fija: puede omitir `type_operation_id` o " +
        "enviar la de la variante; otra responde 422 (`validation.failed`).";

    private const string AiuInvoiceText =
        "Emite una factura AIU (administración, imprevistos y utilidad; operación 9, código `09`), como un contrato de obra. " +
        "El cuerpo es el de `issueInvoice` más `noteAIU` (obligatorio), el objeto del contrato: la línea cuya `description` " +
        "es exactamente «Administración» lleva la nota «Contrato de servicios AIU por concepto de: {noteAIU}». La " +
        "administración, los imprevistos y la utilidad son líneas normales que usted arma.";

    private const string MandateInvoiceText =
        "Emite una factura de mandato (venta por cuenta de terceros; operación 11, código `11`). El cuerpo es el de " +
        "`issueInvoice`; cada línea vendida por cuenta de un tercero lleva su NIT en `agentparty` (de 1 a 15 dígitos) y " +
        "su dígito de verificación en `agentparty_dv` (obligatorio con `agentparty` y revisado contra el NIT).";

    private const string TransportInvoiceText =
        "Emite una factura de transporte de carga (operación 21, código `12`). El cuerpo es el de `issueInvoice`; cada " +
        "línea lleva `is_RNDC` (obligatorio). Con `is_RNDC` en `true` la línea es una remesa del RNDC (Registro Nacional " +
        "de Despachos de Carga) y son obligatorios `RNDC_consignment_number` (solo dígitos), " +
        "`internal_consignment_number`, `value_consignment` (entero), `unit_measure_consignment_id` (unidad del catálogo) " +
        "y `quantity_consignment` (numérico); van al XML tal como llegan.";

    private const string ExportInvoiceText =
        "Emite una factura de exportación (tipo de documento 2, código `02`, operación 10). El cuerpo es el de " +
        "`issueInvoice` con `type_document_id` 2 más `deliveryterms` (obligatorio): `special_terms` (cómo se pagan los " +
        "costos del transporte), `loss_risk_responsibility_code` (el Incoterm: CFR, CIF, CIP, CPT, DAP, DAT, DDP, EXW, " +
        "FAS, FCA o FOB; DPU todavía no se acepta) y `loss_risk` (texto libre). Cada línea puede llevar `brandname` y " +
        "`modelname`. Un comprador fuera de Colombia (`country_id` distinto de 46) lleva la ciudad en " +
        "`customer.municipality_name` y el estado o provincia en `customer.state_name`. `k_supplement` (el suplemento K " +
        "del anexo técnico 1.9, opcional) lleva los datos de la exportación y los totales en pesos para la representación " +
        "gráfica; si lo envía, deben llegar sus 30 campos (de `responsible_incharge` a `observations` y de `FctConvCop` a " +
        "`TotAnticiposCop`), o la respuesta es 422 con el campo que falta.";

    private const string ExportRules =
        " Usa su propia resolución de tipo 2 (con su clave técnica para el CUFE) y su propio consecutivo, y cuenta en el " +
        "plan como una factura. La operación es fija: puede omitir `type_operation_id` o enviar 10; otra responde 422. " +
        "Moneda: sin `idcurrency`, o con la moneda de la empresa, la factura va en pesos. Con otra moneda (por ejemplo " +
        "`idcurrency` 149, USD) son obligatorios `calculationrate` (la tasa de cambio a pesos, mayor que 0: la TRM o la " +
        "pactada con el comprador) y `calculationratedate` (su fecha); ImpiFact nunca la consulta en línea ni la supone. " +
        "Todos los valores del cuerpo van en esa moneda, como pide el anexo técnico (regla FAD15b), y el XML lleva " +
        "`cac:PaymentExchangeRate` con la tasa y su fecha.";

    private const string ContingencyInvoiceText =
        "Emite una factura de contingencia tipo 03 (tipo de documento 3, código `03`, operación 10): la transcripción de una " +
        "factura de talonario o de papel que usted expidió mientras no pudo facturar electrónicamente (anexo técnico 1.9, " +
        "sección 12.1). El cuerpo es el de `issueInvoice` con `type_document_id` 3 más, obligatorios, `number` (el de la " +
        "factura de papel, del rango de su resolución de contingencia), `time`, `AdditionalDocumentReferenceID` (el número de " +
        "la factura de papel), `AdditionalDocumentReferenceDate` (su fecha, `yyyy-MM-dd`) y " +
        "`AdditionalDocumentReferenceTypeDocument` (su tipo de documento DIAN, dos dígitos, por ejemplo `01`). Usa su propia " +
        "resolución de tipo 3 (con número y vigencia; sin clave técnica) y cuenta en el plan como una factura. El código es " +
        "el CUDE con el PIN del software, como pide el anexo (sección 11.1.4): la respuesta lo trae en `cude`. La operación " +
        "es fija: puede omitir `type_operation_id` o enviar 10; otra responde 422.";

    private const string DianContingencyInvoiceText =
        "Emite una factura tipo 04 (tipo de documento 12, código `04`, operación 10) cuando la DIAN no responde (anexo técnico " +
        "1.9, sección 12.2). El cuerpo es el de `issueInvoice` con `type_document_id` 12. Usa la resolución, el consecutivo y " +
        "el CUFE (con la clave técnica) de la factura de venta, como pide el anexo: un número que ya usa una factura de la " +
        "empresa responde 409 (`document.number_in_use`). La respuesta es inmediata: el documento queda firmado y pendiente " +
        "(`ContingencyPending`), sin llamar a la DIAN. El Worker lo envía solo con SendBillSync 30 minutos después y, mientras " +
        "la DIAN no lo reciba, lo sigue intentando hasta 48 horas; `sendPendingContingencyInvoices` lo envía de inmediato. Cuando " +
        "la DIAN lo acepta se generan su AttachedDocument, su PDF y el correo (con `sendmail`); si lo rechaza queda `Rejected` " +
        "con su aviso. Antes de emitir se revisa: empresa activa (403 `company.inactive`), resolución de factura activa con el " +
        "prefijo, certificado vigente (422 `certificate.missing` o `certificate.expired`), software (422 `software.missing`), " +
        "plan (403 `plan.inactive` o `plan.limit_reached`) y los totales (422 `validation.failed`). Acepta `Idempotency-Key`.";

    private const string SendPendingText =
        "Envía ya a la DIAN las facturas tipo 04 pendientes (`ContingencyPending`) de la empresa de la credencial: todas, las " +
        "del prefijo `prefix` o la de `prefix` y `number` (`number` sin `prefix` responde 422). Revisa la empresa activa (403 " +
        "`company.inactive`) y su certificado (422 `certificate.missing` o `certificate.expired`), pero no el plan, que se " +
        "revisó al emitir. La respuesta es inmediata y lista los documentos que quedaron en cola, todavía `ContingencyPending`: " +
        "el Worker los envía con SendBillSync y cada uno queda `Accepted` (con su PDF) o `Rejected` (con su aviso); consulte " +
        "el resultado con `GET /api/v1/documents/{id}`. Sin pendientes, `documents` va vacío. Pide `documents:write`.";

    private const string SupportDocumentText =
        "Emite un documento soporte en adquisiciones a no obligados a facturar (tipo de documento 11, código `05`): la " +
        "empresa compra a un vendedor que no factura. El cuerpo es el de `issueInvoice` con `type_document_id` 11 y " +
        "`seller` (el vendedor) en lugar de `customer`: `identification_number`, `dv` (opcional; si llega se revisa), " +
        "`type_document_identification_id` (4, 5, 6, 7, 8, 9 u 11), `name`, `phone`, `address`, `merchant_registration` " +
        "y `postal_zone_code` (numérico) son obligatorios. Cada línea lleva `type_generation_transmition_id` (1 por " +
        "operación, 2 acumulado semanal) y `start_date` (`yyyy-MM-dd`), obligatorios. La operación es 23 (residente, por " +
        "defecto, código `10`) o 24 (no residente, código `11`); otra responde 422. Un vendedor fuera de Colombia lleva " +
        "`country_id` de su país, `municipality_name` y `state_name`. Sin `idcurrency`, el XML lleva la tasa de cambio del " +
        "peso con la fecha del día, como ApiDian.";

    private const string AdjustmentNoteText =
        "Emite una nota de ajuste al documento soporte (tipo de documento 13, `CreditNoteTypeCode` 95). El cuerpo es el de " +
        "`issueCreditNote` con `type_document_id` 13, `seller` en lugar de `customer` y `billing_reference` obligatorio: " +
        "el número, el CUDS y la fecha de un documento soporte de la empresa, que debe estar aceptado por la DIAN (en el " +
        "set de pruebas basta con que exista); si no es un documento soporte de la empresa responde 422 " +
        "(`document.reference_not_found`), si el número no es el de ese CUDS, 422 (`document.reference_mismatch`), y si no " +
        "está aceptado, 422 (`document.reference_not_accepted`). `discrepancyresponsecode` es opcional, de 1 a 5 (el " +
        "catálogo de motivos de la nota de ajuste). `type_operation_id` puede ser 23 o 24; sin él se usa la operación 10 " +
        "(código `10`), como ApiDian. Cada línea puede llevar `brandname` y `modelname`.";

    /// <summary>What the support-document family does differently from the invoice: its own software, environment and plan.</summary>
    private const string SupportDocumentRules =
        " Usa el software de documento soporte (`idsupportdocument` y `pinsupportdocument` de " +
        "`PUT /api/v1/companies/{nit}/software`) y el ambiente de documento soporte de la empresa, no los de la factura: " +
        "sin ese software responde 422 (`software.missing`). Usa su propia resolución (tipo 11 para el documento soporte, " +
        "con número y vigencia; tipo 13 para la nota de ajuste) y su propio consecutivo, y cuenta en el plan como documento " +
        "soporte (`qty_docs_ds`). El código del documento es el CUDS, con el PIN del software de documento soporte, y la " +
        "respuesta lo trae en `cuds`; una clave técnica en la resolución no se usa. Cuando la DIAN lo acepta se genera su PDF " +
        "(`DSS-…pdf` o `NDSNS-…pdf`, con el vendedor como «Proveedor» y el CUDS) y, si llega `sendmail`, el correo va al " +
        "correo del vendedor (`seller.email`), como ApiDian.";

    private const string CreditNoteText =
        "Emite una nota crédito, que corrige o anula total o parcialmente una factura. El cuerpo sigue el contrato de " +
        "ApiDian: `billing_reference` (número, CUFE y fecha de la factura), `discrepancyresponsecode`, `customer`, " +
        "`credit_note_lines`, `legal_monetary_totals`, `tax_totals` y, si quiere, `payment_form` (una forma de pago o una " +
        "lista). Las notas no llevan anticipos.";

    private const string DebitNoteText =
        "Emite una nota débito, que agrega cargos a una factura. El cuerpo sigue el contrato de ApiDian: " +
        "`billing_reference` (número, CUFE y fecha de la factura), `discrepancyresponsecode`, `customer`, " +
        "`debit_note_lines`, `requested_monetary_totals`, `tax_totals` y, si quiere, `payment_form` (una forma de pago o " +
        "una lista). Las notas no llevan anticipos.";

    /// <summary>
    /// El cuerpo se lee a mano (su resumen identifica la solicitud repetida), así que su forma, sus ejemplos (los casos
    /// golden que Scalar muestra precargados) y la respuesta se declaran aquí para que aparezcan en el documento
    /// OpenAPI. El cuerpo se agrega solo al documento: con <c>Accepts</c> la ruta exigiría el tipo de contenido JSON y
    /// respondería 415 antes de revisar la credencial.
    /// </summary>
    /// <param name="producesPdf">Verdadero en la vista previa, que responde el PDF y no la respuesta de la emisión.</param>
    internal static RouteHandlerBuilder Issue(RouteHandlerBuilder route, IReadOnlyList<RequestExample> examples, bool producesPdf = false) =>
        (producesPdf ? route.Produces(StatusCodes.Status200OK, contentType: "application/pdf") : route.Produces<IssueDocumentResponse>())
        .AddOpenApiOperationTransformer(async (operation, context, ct) => operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new()
                {
                    Schema = await context.GetOrCreateSchemaAsync(typeof(IssueDocumentBody), null, ct),
                    Examples = OpenApiExamples.ToOpenApi(examples),
                },
            },
        });

    private static async Task<IResult> Calculate(
        CalculateDocumentTotalsRequest body, IHandler<CalculateDocumentTotalsRequest, DocumentTotalsCalculation> handler, CancellationToken ct) =>
        (await handler.HandleAsync(body, ct)).ToHttpResult();

    private static Task<IResult> PreviewInvoice(
        HttpRequest request, HttpResponse response, IHandler<PreviewInvoiceQuery, byte[]> handler, CancellationToken ct) =>
        PreviewAsync(request, response, body => handler.HandleAsync(new PreviewInvoiceQuery(body), ct), ct);

    private static Task<IResult> PreviewCreditNote(
        HttpRequest request, HttpResponse response, IHandler<PreviewCreditNoteQuery, byte[]> handler, CancellationToken ct) =>
        PreviewAsync(request, response, body => handler.HandleAsync(new PreviewCreditNoteQuery(body), ct), ct);

    private static Task<IResult> PreviewDebitNote(
        HttpRequest request, HttpResponse response, IHandler<PreviewDebitNoteQuery, byte[]> handler, CancellationToken ct) =>
        PreviewAsync(request, response, body => handler.HandleAsync(new PreviewDebitNoteQuery(body), ct), ct);

    /// <summary>La vista previa de los tres tipos: lee el cuerpo como la emisión y responde el PDF del caso de uso.</summary>
    private static async Task<IResult> PreviewAsync(
        HttpRequest request, HttpResponse response, Func<IssueDocumentBody, Task<Application.Abstractions.Results.Result<byte[]>>> preview,
        CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, ct);
        if (!IssueDocumentJson.TryParse(buffer.ToArray(), out var body))
        {
            return IssueErrors.InvalidJson.ToHttpResult();
        }

        var result = await preview(body!);
        if (!result.IsSuccess)
        {
            return result.ToHttpResult();
        }

        // Se abre en el navegador, no se descarga, y no se guarda en ninguna caché: es un borrador.
        response.Headers.CacheControl = "no-store";
        response.Headers.ContentDisposition = "inline; filename=\"vista-previa.pdf\"";
        return TypedResults.File(result.Value, "application/pdf");
    }

    private static Task<IResult> Invoice(
        HttpRequest request, string? testSetId, IHandler<IssueInvoiceCommand, IssueDocumentResponse> handler, CancellationToken ct) =>
        IssueAsync(request, (body, raw, key) => handler.HandleAsync(new IssueInvoiceCommand(body, raw, key, testSetId), ct), ct);

    private static Task<IResult> AiuInvoice(
        HttpRequest request, string? testSetId, IHandler<IssueAiuInvoiceCommand, IssueDocumentResponse> handler, CancellationToken ct) =>
        IssueAsync(request, (body, raw, key) => handler.HandleAsync(new IssueAiuInvoiceCommand(body, raw, key, testSetId), ct), ct);

    private static Task<IResult> MandateInvoice(
        HttpRequest request, string? testSetId, IHandler<IssueMandateInvoiceCommand, IssueDocumentResponse> handler, CancellationToken ct) =>
        IssueAsync(request, (body, raw, key) => handler.HandleAsync(new IssueMandateInvoiceCommand(body, raw, key, testSetId), ct), ct);

    private static Task<IResult> TransportInvoice(
        HttpRequest request, string? testSetId, IHandler<IssueTransportInvoiceCommand, IssueDocumentResponse> handler, CancellationToken ct) =>
        IssueAsync(request, (body, raw, key) => handler.HandleAsync(new IssueTransportInvoiceCommand(body, raw, key, testSetId), ct), ct);

    private static Task<IResult> ExportInvoice(
        HttpRequest request, string? testSetId, IHandler<IssueExportInvoiceCommand, IssueDocumentResponse> handler, CancellationToken ct) =>
        IssueAsync(request, (body, raw, key) => handler.HandleAsync(new IssueExportInvoiceCommand(body, raw, key, testSetId), ct), ct);

    private static Task<IResult> ContingencyInvoice(
        HttpRequest request, string? testSetId, IHandler<IssueContingencyInvoiceCommand, IssueDocumentResponse> handler, CancellationToken ct) =>
        IssueAsync(request, (body, raw, key) => handler.HandleAsync(new IssueContingencyInvoiceCommand(body, raw, key, testSetId), ct), ct);

    private static Task<IResult> DianContingencyInvoice(
        HttpRequest request, IHandler<IssueDianContingencyInvoiceCommand, IssueDocumentResponse> handler, CancellationToken ct) =>
        IssueAsync(request, (body, raw, key) => handler.HandleAsync(new IssueDianContingencyInvoiceCommand(body, raw, key), ct), ct);

    /// <summary><c>prefix</c> and <c>number</c> come from the query; the request has no body.</summary>
    private static async Task<IResult> SendPendingContingency(
        string? prefix, string? number, IHandler<SendPendingContingencyInvoicesCommand, SendPendingContingencyInvoicesResponse> handler,
        CancellationToken ct) =>
        (await handler.HandleAsync(new SendPendingContingencyInvoicesCommand(prefix, number), ct)).ToHttpResult();

    private static Task<IResult> CreditNote(
        HttpRequest request, string? testSetId, IHandler<IssueCreditNoteCommand, IssueDocumentResponse> handler, CancellationToken ct) =>
        IssueAsync(request, (body, raw, key) => handler.HandleAsync(new IssueCreditNoteCommand(body, raw, key, testSetId), ct), ct);

    private static Task<IResult> DebitNote(
        HttpRequest request, string? testSetId, IHandler<IssueDebitNoteCommand, IssueDocumentResponse> handler, CancellationToken ct) =>
        IssueAsync(request, (body, raw, key) => handler.HandleAsync(new IssueDebitNoteCommand(body, raw, key, testSetId), ct), ct);

    private static Task<IResult> SupportDocument(
        HttpRequest request, string? testSetId, IHandler<IssueSupportDocumentCommand, IssueDocumentResponse> handler, CancellationToken ct) =>
        IssueAsync(request, (body, raw, key) => handler.HandleAsync(new IssueSupportDocumentCommand(body, raw, key, testSetId), ct), ct);

    private static Task<IResult> SupportDocumentAdjustmentNote(
        HttpRequest request, string? testSetId, IHandler<IssueSupportDocumentAdjustmentNoteCommand, IssueDocumentResponse> handler,
        CancellationToken ct) =>
        IssueAsync(request, (body, raw, key) => handler.HandleAsync(new IssueSupportDocumentAdjustmentNoteCommand(body, raw, key, testSetId), ct), ct);

    /// <summary>
    /// Lee el cuerpo completo tal como llegó (su resumen identifica la solicitud repetida) y lo interpreta con el
    /// contrato heredado; un cuerpo que no es JSON o no tiene su forma responde 422.
    /// </summary>
    internal static async Task<IResult> IssueAsync(
        HttpRequest request,
        Func<IssueDocumentBody, byte[], string?, Task<Application.Abstractions.Results.Result<IssueDocumentResponse>>> issue,
        CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, ct);
        var raw = buffer.ToArray();
        if (!IssueDocumentJson.TryParse(raw, out var body))
        {
            return IssueErrors.InvalidJson.ToHttpResult();
        }

        var key = request.Headers.TryGetValue(IdempotencyKeyHeader, out var values) ? values.ToString() : null;
        return (await issue(body!, raw, key)).ToHttpResult();
    }
}
