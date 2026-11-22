namespace ImpiFact.Api.OpenApi;

/// <summary>
/// Secciones (tags) en que el documento OpenAPI (la descripción estándar de una API HTTP) y Scalar agrupan las rutas.
/// Son solo etiquetas de la documentación: no cambian rutas, permisos ni respuestas. El orden de
/// <see cref="Descriptions"/> es el del flujo de integración.
/// </summary>
public static class ApiTags
{
    public const string Identity = "Identidad";

    public const string Companies = "Empresas";

    public const string Documents = "Documentos";

    public const string Batches = "Lotes";

    public const string Radian = "RADIAN";

    public const string Plans = "Planes";

    public const string Notifications = "Notificaciones";

    public const string Masters = "Clientes y productos";
    public const string Dashboard = "Tablero";

    public const string Catalogs = "Catálogos";

    /// <summary>Descripción de cada sección, en el orden en que se muestran.</summary>
    public static IReadOnlyList<(string Name, string Description)> Descriptions { get; } =
    [
        (Identity,
            "Inicio de sesión de los usuarios del panel (JWT, JSON Web Token: credencial firmada de vida corta), API keys " +
            "y usuarios. Las integraciones normalmente no necesitan esta sección: usan la API key que devuelve la creación " +
            "de la empresa."),
        (Companies,
            "Alta y configuración de la empresa emisora: datos, ambiente de la DIAN, software, certificado de firma, " +
            "resoluciones de numeración y consulta de los rangos autorizados. Es el paso previo a emitir."),
        (Documents,
            "Emisión de facturas, notas crédito y notas débito; consulta de documentos y de su estado en la DIAN; " +
            "descarga de archivos (XML, PDF, AttachedDocument, ZIP) y tareas de mantenimiento (regenerar el PDF o el " +
            "AttachedDocument, reenviar el correo)."),
        (Batches,
            "Envío por lotes (SendBillAsync): hasta 50 facturas y notas de la empresa en un solo ZIP. Cada documento se agrega con " +
            "el cuerpo de su ruta de emisión y queda firmado en el lote; al enviar el lote, el Worker consulta GetStatusZip y " +
            "deja a cada documento su propio veredicto de la DIAN."),
        (Radian,
            "Eventos RADIAN (registro de la factura electrónica como título valor): facturas recibidas de proveedores, que se " +
            "cargan desde la DIAN por su CUFE, y sus eventos de acuse de recibo (030), reclamo (031), recibo del bien (032) y " +
            "aceptación expresa (033); la aceptación tácita (034) de las facturas propias y la consulta de los eventos que la " +
            "DIAN tiene registrados. ImpiFact revisa el orden de los eventos del anexo técnico antes de enviarlos."),
        (Plans,
            "Planes de documentos y su consumo. La plataforma crea los planes y los asigna a las empresas; cada empresa " +
            "consulta su plan vigente y los documentos que le quedan."),
        (Masters,
            "Clientes y productos de la empresa para llenar la emisión: alta, edición, desactivación y búsqueda paginada en " +
            "el servidor, y la consulta en vivo a la DIAN del nombre y el correo registrados de un adquiriente (GetAcquirer)."),
        (Notifications,
            "Avisos de la campana del panel: certificado por vencer, rangos de numeración, documentos rechazados o sin " +
            "confirmar. Se listan por empresa, del más reciente al más antiguo, y se marcan como leídos."),
        (Dashboard,
            "Resumen del tablero del panel: documentos por estado y por mes, consumo del plan, certificado de firma y " +
            "resoluciones por agotarse de la empresa activa."),
        (Catalogs,
            "Catálogos de la DIAN para las listas de los formularios: tipos de identificación y de organización, regímenes, " +
            "responsabilidades fiscales, municipios, departamentos, países, unidades de medida, impuestos, formas y medios de " +
            "pago, tipos de documento y monedas. Son los mismos para todas las empresas y se pueden guardar en caché."),
    ];
}
