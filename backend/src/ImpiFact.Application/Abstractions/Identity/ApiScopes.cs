namespace ImpiFact.Application.Abstractions.Identity;

/// <summary>
/// Alcances (scopes) que puede tener una API key (llave de acceso de una integración): cada uno habilita un grupo
/// de operaciones (Arquitectura detallada, sección 9.2). Se guardan separados por coma en <c>api_keys.scopes</c>.
/// </summary>
public static class ApiScopes
{
    /// <summary>Emitir documentos (facturas, notas, eventos).</summary>
    public const string DocumentsWrite = "documents:write";

    /// <summary>Consultar documentos y descargar sus archivos.</summary>
    public const string DocumentsRead = "documents:read";

    /// <summary>Cambiar la configuración de la empresa: datos, ambiente, software, certificado y resoluciones.</summary>
    public const string ConfigWrite = "config:write";

    /// <summary>Administrar la plataforma (empresas, planes y revendedores). Solo lo tienen las llaves sin empresa.</summary>
    public const string Platform = "platform:*";

    public static IReadOnlyList<string> All { get; } = [DocumentsWrite, DocumentsRead, ConfigWrite, Platform];
}
