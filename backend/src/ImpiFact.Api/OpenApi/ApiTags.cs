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

    public const string Plans = "Planes";

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
        (Plans,
            "Planes de documentos y su consumo. La plataforma crea los planes y los asigna a las empresas; cada empresa " +
            "consulta su plan vigente y los documentos que le quedan."),
    ];
}
