using System.Text.Json.Nodes;
using ImpiFact.Application.Abstractions.Identity;

namespace ImpiFact.Api.OpenApi;

/// <summary>
/// Cuerpos de ejemplo de configuración, planes e identidad que Scalar muestra precargados. Pasan los validadores
/// actuales y encajan entre sí: la empresa de ejemplo (NIT 900123456, dígito de verificación 8) con la resolución de
/// habilitación SETP 990000000–995000000 de la DIAN (Dirección de Impuestos y Aduanas Nacionales) emite tal cual los
/// ejemplos de factura de <see cref="OpenApiExamples"/>. Los valores que solo tiene quien integra (el software y el
/// PIN que dio la DIAN, el certificado) se marcan en la descripción del ejemplo para reemplazarlos.
/// </summary>
internal static class ExampleBodies
{
    /// <summary>Identificador de ejemplo del software registrado ante la DIAN.</summary>
    public const string SoftwareId = "3f9d8c2e-5b1a-4e7f-9c3d-2a6b8e1f0d47";

    /// <summary>Clave técnica pública de la resolución de habilitación de la DIAN (entra en el CUFE).</summary>
    private const string TestTechnicalKey = "fc8eac422eba16e22ffd8c6f94b3f40a6e38162c";

    private const string UserEmail = "admin@empresa-prueba.com";

    private const string UserPassword = "Cambie-esta-clave-2026";

    private const string OwnValues = "Reemplace `id` y `pin` por los que le dio la DIAN al registrar el software.";

    public static RequestExample[] Company { get; } =
    [
        new("persona-juridica", "Empresa (persona jurídica) en habilitación", Parse("""
            {
              "type_document_identification_id": 6,
              "type_organization_id": 1,
              "type_regime_id": 2,
              "type_liability_id": 14,
              "business_name": "EMPRESA DE PRUEBA SAS",
              "merchant_registration": "0000000-00",
              "municipality_id": 1006,
              "address": "CALLE 100 # 10-20",
              "phone": "6011234567",
              "email": "facturacion@empresa-prueba.com"
            }
            """), "Use el NIT y el dígito de verificación de la ruta. Sin `type_environment_id` la empresa queda en habilitación."),
    ];

    public static RequestExample[] Environment { get; } =
    [
        new("habilitacion", "Habilitación (2): pruebas sin validez fiscal", Parse("""{ "type_environment_id": 2 }""")),
        new("produccion", "Producción (1): documentos con validez fiscal", Parse("""{ "type_environment_id": 1 }""")),
    ];

    public static RequestExample[] Software { get; } =
    [
        new("facturacion", "Software de facturación con su set de pruebas", Parse($$"""
            {
              "id": "{{SoftwareId}}",
              "pin": "12345",
              "test_set_id": "{{OpenApiExamples.TestSetId}}"
            }
            """), OwnValues),
    ];

    /// <summary>
    /// Support-document software (phase 7a, T4): its own identifier and PIN, given by the DIAN when the support-document
    /// software is registered; it is kept apart from the invoice software.
    /// </summary>
    public static RequestExample[] SupportDocumentSoftware { get; } =
    [
        new("documento-soporte", "Software de documento soporte", Parse("""
            {
              "idsupportdocument": "7c1e5a90-2b4d-4f3a-8e6c-1d9b0a2f3c58",
              "pinsupportdocument": "54321"
            }
            """), "Reemplace `idsupportdocument` y `pinsupportdocument` por los que le dio la DIAN al registrar el software de documento soporte."),
    ];

    public static RequestExample[] Certificate { get; } =
    [
        new("pkcs12", "Certificado .p12 en Base64 con su contraseña", Parse("""
            {
              "certificate": "MIIKpAIBAzCCCm4GCSqGSIb3DQEHAaCCCl8EggpbMIIKVzCCBg8GCSqGSIb3DQEHAaCCBgAEggX8",
              "password": "clave-del-certificado"
            }
            """), "Reemplace `certificate` por el contenido completo de su archivo .p12 o .pfx en Base64 y `password` por su contraseña."),
    ];

    public static RequestExample[] Resolution { get; } =
    [
        new("factura-setp", "Factura: resolución de habilitación SETP 990000000–995000000", Parse($$"""
            {
              "type_document_id": 1,
              "prefix": "SETP",
              "resolution": "18760000001",
              "resolution_date": "2019-01-19",
              "technical_key": "{{TestTechnicalKey}}",
              "from": 990000000,
              "to": 995000000,
              "date_from": "2019-01-19",
              "date_to": "2030-01-19"
            }
            """), "Valores públicos de la DIAN para habilitación; los ejemplos de factura usan esta resolución."),
        new("factura-exportacion-setp", "Factura de exportación: resolución de tipo 2 de habilitación SETP", Parse($$"""
            {
              "type_document_id": 2,
              "prefix": "SETP",
              "resolution": "18760000001",
              "resolution_date": "2019-01-19",
              "technical_key": "{{TestTechnicalKey}}",
              "from": 990000000,
              "to": 995000000,
              "date_from": "2019-01-19",
              "date_to": "2030-01-19"
            }
            """), "La usan los ejemplos de factura de exportación, que tienen su propia resolución y su propio consecutivo."),
        new("factura-contingencia-setp", "Factura de contingencia (tipo 03): resolución de tipo 3 de habilitación SETP", Parse("""
            {
              "type_document_id": 3,
              "prefix": "SETP",
              "resolution": "18760000001",
              "resolution_date": "2019-01-19",
              "from": 990000000,
              "to": 995000000,
              "date_from": "2019-01-19",
              "date_to": "2030-01-19"
            }
            """), "La usan los ejemplos de factura de contingencia tipo 03, que transcriben facturas de papel con su propia numeración. " +
            "No lleva clave técnica: la DIAN no le asigna una a la numeración de contingencia (anexo técnico 1.9, sección 11.1.4)."),
        new("nota-credito-nc", "Nota crédito: numeración NC", Parse("""
            { "type_document_id": 4, "prefix": "NC", "from": 1, "to": 99999999 }
            """), "La usan los ejemplos de nota crédito."),
        new("nota-debito-nd", "Nota débito: numeración ND", Parse("""
            { "type_document_id": 5, "prefix": "ND", "from": 1, "to": 99999999 }
            """), "La usa el ejemplo de nota débito."),
        new("documento-soporte-seds", "Documento soporte: resolución de habilitación SEDS 984000000–985000000", Parse("""
            {
              "type_document_id": 11,
              "prefix": "SEDS",
              "resolution": "18760000001",
              "resolution_date": "2022-01-01",
              "from": 984000000,
              "to": 985000000,
              "date_from": "2022-01-01",
              "date_to": "2030-01-19"
            }
            """), "La usa el ejemplo de documento soporte. No lleva clave técnica: el CUDS usa el PIN del software de documento soporte."),
        new("nota-ajuste-nds", "Nota de ajuste al documento soporte: numeración NDS", Parse("""
            { "type_document_id": 13, "prefix": "NDS", "from": 1, "to": 99999999 }
            """), "La usa el ejemplo de nota de ajuste."),
    ];

    /// <summary>
    /// Support document (phase 7a, T4) bought from a resident seller not obliged to invoice, the Postman «Documento
    /// soporte sencillo» request without <c>number</c>, so it takes the next number of the SEDS resolution.
    /// </summary>
    public static RequestExample[] SupportDocuments { get; } =
    [
        new("support-document-resident", "Documento soporte a un proveedor residente no obligado a facturar", Parse("""
            {
              "type_document_id": 11,
              "prefix": "SEDS",
              "notes": "COMPRA A PROVEEDOR NO OBLIGADO",
              "seller": {
                "identification_number": "800000003",
                "dv": "4",
                "name": "PROVEEDOR NO OBLIGADO DE EJEMPLO",
                "phone": "3000000003",
                "address": "CALLE 3 # 3-03",
                "email": "proveedor@example.com",
                "merchant_registration": "0000-00",
                "postal_zone_code": 630001,
                "type_document_identification_id": 6,
                "type_organization_id": 2,
                "municipality_id": 820,
                "type_liability_id": 117,
                "type_regime_id": 1
              },
              "payment_form": { "payment_form_id": 1, "payment_method_id": 10, "duration_measure": "0" },
              "legal_monetary_totals": {
                "line_extension_amount": "200.00",
                "tax_exclusive_amount": "200.00",
                "tax_inclusive_amount": "200.00",
                "allowance_total_amount": "0.00",
                "charge_total_amount": "0.00",
                "payable_amount": "200.00"
              },
              "tax_totals": [{ "tax_id": 1, "tax_amount": "0.00", "percent": "0", "taxable_amount": "200.00" }],
              "invoice_lines": [
                {
                  "unit_measure_id": 70,
                  "invoiced_quantity": "2.00",
                  "line_extension_amount": "200.00",
                  "free_of_charge_indicator": false,
                  "tax_totals": [{ "tax_id": 1, "tax_amount": "0.00", "percent": "0.00", "taxable_amount": "200.00" }],
                  "description": "SERVICIO DE EJEMPLO",
                  "code": "SERV01",
                  "type_item_identification_id": 4,
                  "price_amount": "100.00",
                  "base_quantity": "2.00",
                  "type_generation_transmition_id": 1,
                  "start_date": "2026-11-14"
                }
              ]
            }
            """), "Un proveedor fuera de Colombia va con `type_operation_id` 24, `country_id` de su país, `municipality_name` y `state_name`."),
    ];

    /// <summary>The Postman «Nota de ajuste de documento soporte» request, which references a support document by its CUDS.</summary>
    public static RequestExample[] SupportDocumentAdjustmentNotes { get; } =
    [
        new("adjustment-cancellation", "Nota de ajuste que anula el documento soporte SEDS984000000", Parse("""
            {
              "type_document_id": 13,
              "prefix": "NDS",
              "billing_reference": {
                "number": "SEDS984000000",
                "uuid": "e3b6f1cf90150b66de1e1c55970c9797970c6239719a278bfb58a3a27ec1ba4dc35d586590a3958d0aa36e661a3d4d11",
                "issue_date": "2026-11-14"
              },
              "discrepancyresponsecode": 2,
              "notes": "ANULACION DEL DOCUMENTO SOPORTE",
              "seller": {
                "identification_number": "800000003",
                "dv": "4",
                "name": "PROVEEDOR NO OBLIGADO DE EJEMPLO",
                "phone": "3000000003",
                "address": "CALLE 3 # 3-03",
                "merchant_registration": "0000-00",
                "postal_zone_code": 630001,
                "type_document_identification_id": 6,
                "type_organization_id": 2,
                "municipality_id": 820,
                "type_liability_id": 117,
                "type_regime_id": 1
              },
              "legal_monetary_totals": {
                "line_extension_amount": "200.00",
                "tax_exclusive_amount": "200.00",
                "tax_inclusive_amount": "200.00",
                "payable_amount": "200.00"
              },
              "tax_totals": [{ "tax_id": 1, "tax_amount": "0.00", "percent": "0.00", "taxable_amount": "200.00" }],
              "credit_note_lines": [
                {
                  "unit_measure_id": 70,
                  "invoiced_quantity": "2.00",
                  "line_extension_amount": "200.00",
                  "free_of_charge_indicator": false,
                  "tax_totals": [{ "tax_id": 1, "tax_amount": "0.00", "percent": "0.00", "taxable_amount": "200.00" }],
                  "description": "SERVICIO DE EJEMPLO",
                  "code": "SERV01",
                  "type_item_identification_id": 4,
                  "price_amount": "100.00",
                  "base_quantity": "2.00"
                }
              ]
            }
            """), "Reemplace `billing_reference` por el número, el CUDS y la fecha de un documento soporte de su empresa ya aceptado por la DIAN."),
    ];

    public static RequestExample[] ResolutionNextNumber { get; } =
    [
        new("continuar-numeracion", "Seguir desde el número 990000101 del rango SETP", Parse("""
            { "next_number": 990000101 }
            """), "El número debe estar dentro del rango de la resolución y no puede ser menor que su próximo número actual."),
    ];

    public static RequestExample[] Plan { get; } =
    [
        new("mensual", "Plan mensual de 100 facturas", Parse("""
            {
              "name": "Mensual 100",
              "qty_docs_invoice": 100,
              "qty_docs_payroll": 0,
              "qty_docs_radian": 0,
              "qty_docs_ds": 0,
              "period": 1,
              "observations": "Facturas y notas: 100 por mes."
            }
            """)),
        new("paquete", "Paquete de 1000 facturas sin vencimiento mensual", Parse("""
            {
              "name": "Paquete 1000",
              "qty_docs_invoice": 1000,
              "qty_docs_payroll": 0,
              "qty_docs_radian": 0,
              "qty_docs_ds": 0,
              "period": 3
            }
            """)),
    ];

    public static RequestExample[] AssignPlan { get; } =
    [
        new("plan-1", "Asignar el plan 1", Parse("""{ "plan_id": 1 }""")),
    ];

    /// <summary>RADIAN (phase 7a, T6): the CUFE of an invoice addressed to the company, to load it from the DIAN.</summary>
    public static RequestExample[] ReceivedDocument { get; } =
    [
        new("por-cufe", "Factura recibida por su CUFE", Parse("""
            { "cufe": "83c3ba93981ccf3d79c246498209c7762962faccc62b1328e3be207da45569b7dac74cac108cae80addd7e2e4c4e9f9e" }
            """), "Use el CUFE de una factura que un proveedor le emitió a su empresa."),
    ];

    /// <summary>The buyer's events in the order of Anexo Técnico 1.9: 030, 032 and then 031 or 033.</summary>
    public static RequestExample[] ReceivedDocumentEvents { get; } =
    [
        new("acuse-de-recibo", "030: acuse de recibo, con la persona que recibe", Parse("""
            {
              "event_code": "030",
              "issuer_party": {
                "identification_number": "1000000001",
                "first_name": "PERSONA",
                "last_name": "DE PRUEBA",
                "organization_department": "CONTABILIDAD",
                "job_title": "AUXILIAR CONTABLE"
              }
            }
            """)),
        new("recibo-del-bien", "032: recibo del bien o prestación del servicio", Parse("""{ "event_code": "032" }"""),
            "Requiere el 030."),
        new("aceptacion-expresa", "033: aceptación expresa", Parse("""{ "event_code": "033" }"""), "Requiere el 030 y el 032."),
        new("reclamo", "031: reclamo por mercancía no entregada totalmente", Parse("""{ "event_code": "031", "claim_reason": "02" }"""),
            "Requiere el 030 y el 032. `claim_reason`: 01 documento con inconsistencias, 02 mercancía no entregada totalmente, " +
            "03 mercancía no entregada parcialmente, 04 servicio no prestado."),
    ];

    public static RequestExample[] TacitAcceptance { get; } =
    [
        new("aceptacion-tacita", "034: aceptación tácita de una factura propia", Parse("""{ "event_code": "034" }"""),
            "Pasados 3 días hábiles desde el 032 del adquiriente, sin reclamo ni aceptación expresa."),
    ];

    public static RequestExample[] Login { get; } =
    [
        new("usuario", "Correo y contraseña de un usuario del panel", Parse($$"""
            { "email": "{{UserEmail}}", "password": "{{UserPassword}}" }
            """), "Son los datos del ejemplo de `POST /api/v1/users`."),
    ];

    public static RequestExample[] Refresh { get; } =
    [
        new("renovar", "Renovar con el refresh_token del inicio de sesión", Parse("""
            { "refresh_token": "pegue-aqui-el-refresh_token-de-login" }
            """), "Reemplace el valor por el `refresh_token` que entregó `POST /api/v1/auth/login`."),
    ];

    public static RequestExample[] Logout { get; } =
    [
        new("cerrar", "Cerrar la sesión de una integración con su refresh_token", Parse("""
            { "refresh_token": "pegue-aqui-el-refresh_token-de-login" }
            """), "El panel no manda cuerpo: envía `X-Client: panel` y el navegador agrega la cookie de la sesión."),
    ];

    public static RequestExample[] PasswordResetRequest { get; } =
    [
        new("correo", "Correo de la cuenta", Parse($$"""
            { "email": "{{UserEmail}}" }
            """), "La respuesta es la misma aunque el correo no tenga cuenta."),
    ];

    public static RequestExample[] PasswordResetConfirm { get; } =
    [
        new("nueva-clave", "Token del enlace y contraseña nueva", Parse("""
            { "token": "pegue-aqui-el-token-del-enlace", "new_password": "Otra-clave-segura-2026" }
            """), "Reemplace `token` por el valor de `?token=` del enlace que llegó por correo."),
    ];

    public static RequestExample[] ApiKey { get; } =
    [
        new("integracion", "Llave de empresa para emitir y consultar", Parse($$"""
            { "name": "Integración ERP", "scopes": ["{{ApiScopes.DocumentsWrite}}", "{{ApiScopes.DocumentsRead}}"] }
            """), "Con la credencial de la empresa."),
        new("solo-lectura", "Llave de empresa de solo consulta, con vencimiento", Parse($$"""
            { "name": "Consulta de documentos", "scopes": ["{{ApiScopes.DocumentsRead}}"], "expires_at": "2027-12-31T23:59:59-05:00" }
            """), "Con la credencial de la empresa."),
        new("plataforma", "Llave de plataforma", Parse($$"""
            { "name": "Operación de plataforma", "scopes": ["{{ApiScopes.Platform}}"] }
            """), "Con una credencial de plataforma."),
    ];

    public static RequestExample[] User { get; } =
    [
        new("administrador", "Administrador de plataforma", Parse($$"""
            { "email": "{{UserEmail}}", "password": "{{UserPassword}}", "name": "Administración ImpiFact", "role": "platform_admin" }
            """), "Con una credencial de plataforma. Luego puede iniciar sesión con el ejemplo de `POST /api/v1/auth/login`."),
        new("vendedor", "Vendedor de la empresa", Parse($$"""
            { "email": "ventas@empresa-prueba.com", "password": "{{UserPassword}}", "name": "Ventas", "phone": "3001234567", "role": "seller" }
            """), "Con la sesión del panel del dueño (`owner`) de la empresa."),
    ];

    /// <summary>
    /// Borradores para <c>POST /api/v1/documents/calculate</c>. El primero tiene las líneas del ejemplo de factura «con
    /// descuentos» (golden <c>two-lines-discounts-iva19</c>) y da sus mismos totales: valor bruto 1900.00, IVA 361.00 y
    /// 2161.00 a pagar.
    /// </summary>
    public static RequestExample[] CalculateTotals { get; } =
    [
        new("descuentos-iva19", "Dos líneas con descuento e IVA del 19 % y un descuento global", Parse("""
            {
              "lines": [
                {
                  "quantity": 1,
                  "unit_price": 1000,
                  "allowance_charges": [{ "charge_indicator": false, "amount": 50 }],
                  "taxes": [{ "tax_id": 1, "percent": 19 }]
                },
                {
                  "quantity": 1,
                  "unit_price": 1000,
                  "allowance_charges": [{ "charge_indicator": false, "percent": 5 }],
                  "taxes": [{ "tax_id": 1, "percent": 19 }]
                }
              ],
              "allowance_charges": [{ "charge_indicator": false, "amount": 100 }]
            }
            """)),
        new("iva-mixto-bolsas", "IVA del 19 % y del 5 %, impuesto a las bolsas y un flete", Parse("""
            {
              "lines": [
                { "quantity": 2, "unit_price": 2350000, "taxes": [{ "tax_id": 1, "percent": 19 }] },
                { "quantity": 3.5, "unit_price": 12500.75, "taxes": [{ "tax_id": 1, "percent": 5 }] },
                { "quantity": 3, "unit_price": 0, "taxes": [{ "tax_id": 10, "per_unit_amount": 66, "unit_measure_id": 70 }] },
                { "quantity": 1, "unit_price": 65000, "free_of_charge": true }
              ],
              "allowance_charges": [{ "charge_indicator": true, "amount": 35000 }]
            }
            """), "La última línea es una muestra gratuita: vale 0 y su precio solo es de referencia."),
    ];

    private static JsonNode Parse(string json) => JsonNode.Parse(json)!;
}
