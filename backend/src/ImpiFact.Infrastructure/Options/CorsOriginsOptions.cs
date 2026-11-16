namespace ImpiFact.Infrastructure.Options;

/// <summary>
/// CORS (Cross-Origin Resource Sharing: permiso para que una página de otro dominio, como el panel, llame a la
/// API desde el navegador). Solo se aceptan los orígenes de esta lista explícita; si está vacía, ninguno
/// (Arquitectura detallada, sección 9.1).
/// </summary>
public sealed class CorsOriginsOptions
{
    public const string SectionName = "Cors";

    /// <summary>Orígenes permitidos, por ejemplo <c>https://app.example.com</c>.</summary>
    public string[] Origins { get; set; } = [];
}
