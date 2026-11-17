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
        new("nota-credito-nc", "Nota crédito: numeración NC", Parse("""
            { "type_document_id": 4, "prefix": "NC", "from": 1, "to": 99999999 }
            """), "La usan los ejemplos de nota crédito."),
        new("nota-debito-nd", "Nota débito: numeración ND", Parse("""
            { "type_document_id": 5, "prefix": "ND", "from": 1, "to": 99999999 }
            """), "La usa el ejemplo de nota débito."),
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

    private static JsonNode Parse(string json) => JsonNode.Parse(json)!;
}
