namespace ImpiFact.Application.Abstractions.Identity;

/// <summary>
/// Crea API keys nuevas (llaves de acceso de las integraciones). Lo usan el caso de uso que crea una llave y el
/// comando del Migrator que crea la primera llave de plataforma; ambos muestran el texto claro una sola vez y
/// guardan solo el resumen y el prefijo para mostrar.
/// </summary>
public interface IApiKeyGenerator
{
    GeneratedApiKey Generate();
}

/// <param name="PlainText">
/// Texto completo de la llave, por ejemplo <c>apk_…</c>. Se entrega al usuario una sola vez y nunca se guarda ni
/// se escribe en los registros.
/// </param>
/// <param name="DisplayPrefix">Primeros 12 caracteres, para reconocer la llave en el panel (<c>key_prefix</c>).</param>
/// <param name="Hash">Resumen SHA-256 del texto completo, en hexadecimal en minúsculas (<c>key_hash</c>).</param>
public sealed record GeneratedApiKey(string PlainText, string DisplayPrefix, string Hash)
{
    /// <summary>No incluye el texto claro, para que un registro accidental del objeto no filtre la llave.</summary>
    public override string ToString() => $"GeneratedApiKey {{ DisplayPrefix = {DisplayPrefix} }}";
}
