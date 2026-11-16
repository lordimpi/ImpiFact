namespace ImpiFact.Application.Abstractions.Identity;

/// <summary>
/// La búsqueda de API keys se guarda en caché 60 segundos (Arquitectura detallada, sección 19.1). El caso de uso
/// que revoca una llave llama a este puerto después de guardar la revocación, para que la instancia que la
/// ejecuta deje de aceptarla de inmediato; las demás instancias la rechazan como máximo 60 segundos después.
/// </summary>
public interface IApiKeyCacheInvalidator
{
    /// <param name="keyHash">Resumen guardado en <c>api_keys.key_hash</c>.</param>
    Task InvalidateAsync(string keyHash, CancellationToken ct);
}
