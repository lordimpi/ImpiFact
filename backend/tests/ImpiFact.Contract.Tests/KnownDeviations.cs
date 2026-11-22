namespace ImpiFact.Contract.Tests;

/// <summary>
/// Única lista de diferencias conocidas entre ImpiFact y el contrato de la colección Postman. Cada entrada nombra una
/// solicitud (por su nombre en la colección) o una ruta (<c>MÉTODO /api/...</c> con <c>{}</c> en los segmentos
/// variables, ver <see cref="Postman.ExpectedContract.RouteOf"/>), lo que se aparta y por qué. Una solicitud que no
/// cumple el contrato y no está aquí hace fallar la prueba, y una entrada que ya no se usa también: así la lista no se
/// queda vieja cuando se corrige una diferencia.
/// </summary>
public static class KnownDeviations
{
    /// <summary>Lo que falta construir y se decide al cerrar la fase 4.</summary>
    private const string Todo = "TODO(phase-4): ";

    public static readonly IReadOnlyList<KnownDeviation> All =
    [
        // Decisiones de diseño: no se cambian.
        KnownDeviation.Keys("POST /api/ubl2.1/config/{}/{}",
            "El PHP devolvía el hash de la contraseña del usuario; ImpiFact nunca devuelve secretos (php-rules, sección 7, #5). " +
            "El plan se asigna con PUT /api/v1/companies/{nit}/plan, no con type_plan_id (Arquitectura, sección 9).",
            "password"),
        KnownDeviation.Keys("POST /api/ubl2.1/invoice",
            "Respuesta inmediata con el documento en cola (decisión C5): el envío a la DIAN (reqfe, rptafe), el AttachedDocument " +
            "y el correo (send_email_*) ocurren después en el Worker y se consultan con status/document; whatsapp_response es " +
            "Enterprise (RF-18).",
            "send_email_success", "send_email_date_time", "reqfe", "rptafe", "attacheddocument", "whatsapp_response"),
        KnownDeviation.Keys("POST /api/ubl2.1/invoice-aiu",
            "Respuesta inmediata con el documento en cola (decisión C5), como la factura (php-rules 9.2, ítem 3).",
            "send_email_success", "send_email_date_time", "reqfe", "rptafe", "attacheddocument", "whatsapp_response"),
        KnownDeviation.Keys("POST /api/ubl2.1/invoice-mandate",
            "Respuesta inmediata con el documento en cola (decisión C5), como la factura (php-rules 9.2, ítem 3).",
            "send_email_success", "send_email_date_time", "reqfe", "rptafe", "attacheddocument", "whatsapp_response"),
        KnownDeviation.Keys("POST /api/ubl2.1/invoice-transport",
            "Respuesta inmediata con el documento en cola (decisión C5), como la factura (php-rules 9.2, ítem 3).",
            "send_email_success", "send_email_date_time", "reqfe", "rptafe", "attacheddocument", "whatsapp_response"),
        KnownDeviation.Keys("POST /api/ubl2.1/invoice-export",
            "Respuesta inmediata con el documento en cola (decisión C5), como la factura (php-rules 9.2, ítem 3).",
            "send_email_success", "send_email_date_time", "reqfe", "rptafe", "attacheddocument", "whatsapp_response"),
        KnownDeviation.Keys("POST /api/ubl2.1/credit-note",
            "Respuesta inmediata con el documento en cola (decisión C5), como la factura.",
            "send_email_success", "send_email_date_time", "reqfe", "rptafe", "attacheddocument"),
        KnownDeviation.Keys("POST /api/ubl2.1/debit-note",
            "Respuesta inmediata con el documento en cola (decisión C5), como la factura.",
            "send_email_success", "send_email_date_time", "reqfe", "rptafe", "attacheddocument"),
        KnownDeviation.StatusCode("Eventos registrados de un documento por CUFE", 404,
            "Eventos RADIAN (RF-10): fase 7."),
        KnownDeviation.StatusCode("Descargar un documento recibido", 404,
            "Documentos recibidos de proveedores (recepción RADIAN, RF-10): fase 7."),
        KnownDeviation.StatusCode("Factura con anticipo (pago previo)", 422,
            "El anticipo ya se escribe (cac:PrepaidPayment) y cuadra con pre_paid_amount (regla FAU12), pero el ejemplo resta el " +
            "anticipo del valor a pagar (900000.00 = 1000000.00 − 100000.00). La regla FAU14 del anexo técnico 1.9 no lo resta " +
            "(total con impuestos − descuentos + cargos), así que la DIAN la rechazaría: ImpiFact responde 422 en " +
            "legal_monetary_totals.payable_amount (php-rules, sección 8)."),
        KnownDeviation.StatusCode("Documento soporte con impuesto", 422,
            "El vendedor del ejemplo tiene type_document_identification_id 3 (cédula), que SupportDocumentRequest.php no admite " +
            "(Rule::in 4, 5, 6, 7, 8, 9 u 11): el PHP también la rechaza; ImpiFact responde 422 en " +
            "seller.type_document_identification_id (php-rules 13.1, ítem 2)."),
        KnownDeviation.StatusCode("Modificar un plan", 422,
            "La solicitud de la colección no trae qty_docs_radian ni qty_docs_ds, que PlanRequest.php exige: el PHP también la " +
            "rechaza, con HTTP 500 (php-rules 3.1.11); ImpiFact responde 422 con el detalle por campo."),

        // Emisión: rutas del PHP que ImpiFact todavía no tiene.
        KnownDeviation.StatusCode("Vista previa del PDF de la factura (sin enviar a la DIAN)", 404,
            Todo + "POST invoice/preeliminar-view (vista previa del PDF sin emitir, RF-02) no existe."),

        // Carpeta 05: rutas del PHP que ImpiFact todavía no tiene.
        KnownDeviation.StatusCode("Buscar documentos con filtros", 404,
            Todo + "GET /api/documents con filtros y paginación de Laravel no existe; el listado canónico es GET /api/v1/documents."),
        KnownDeviation.StatusCode("Registrar o actualizar un cliente", 404, Todo + "clientes (RF-13): register-update-customer no existe."),
        KnownDeviation.StatusCode("Buscar un cliente por tipo y numero de documento", 404, Todo + "clientes (RF-13): GET /api/customer/{tipo}/{numero} no existe."),
        KnownDeviation.StatusCode("Consultar los parametros de correo de la empresa", 404, Todo + "GET emailconfig (correo por empresa, RF-01) no existe."),
        KnownDeviation.StatusCode("Consultar la razon social por NIT", 404, Todo + "GET name-by-nit/{nit} no existe."),
        KnownDeviation.StatusCode("Consultar los datos de una empresa por NIT", 404, Todo + "GET SearchCompany/{nit} no existe."),
        KnownDeviation.StatusCode("Unir varios PDF en un solo archivo", 404, Todo + "POST join-pdfs (RF-12) no existe."),
        KnownDeviation.StatusCode("Descargar el PDF de un documento", 404,
            Todo + "el alias GET /api/invoice/{nit}/{archivo} no existe; el mismo archivo se descarga con /api/download/{nit}/{archivo}."),
        KnownDeviation.StatusCode("Reenviar un documento al correo del cliente", 404, Todo + "POST /api/send-email-customer/{x} no existe; existe send-email."),
        KnownDeviation.StatusCode("Cargar el logo de la empresa", 405, Todo + "PUT config/logo (RF-01) no existe."),
        KnownDeviation.StatusCode("Ajustar el consecutivo de un tipo de documento", 405,
            Todo + "PUT config/generateddocuments no existe (el consecutivo sale de numbering_sequences)."),
        KnownDeviation.StatusCode("Eliminar una empresa", 404, Todo + "POST delete/company/{nit}/{dv} (RF-01, rol de plataforma) no existe."),

        // Carpeta 10: plantillas de PDF.
        KnownDeviation.StatusCode("Listar las plantillas de PDF disponibles", 404, Todo + "GET /api/templates (RF-12) no existe."),
        KnownDeviation.StatusCode("Cambiar la plantilla de PDF de la empresa", 404, Todo + "PUT /api/templates (RF-12) no existe."),
    ];
}

/// <param name="Request">Nombre de la solicitud en la colección o ruta (<c>POST /api/ubl2.1/invoice</c>).</param>
/// <param name="Status">Código HTTP que responde ImpiFact en vez del 2xx; nulo si responde 2xx.</param>
/// <param name="MissingKeys">Claves del ejemplo del PHP que ImpiFact no devuelve.</param>
/// <param name="Reason">Por qué, con su referencia (decisión, ADR, fase o <c>TODO(phase-4)</c>).</param>
public sealed record KnownDeviation(string Request, int? Status, IReadOnlyList<string> MissingKeys, string Reason)
{
    public static KnownDeviation StatusCode(string request, int status, string reason) => new(request, status, [], reason);

    public static KnownDeviation Keys(string request, string reason, params string[] keys) => new(request, null, keys, reason);
}
