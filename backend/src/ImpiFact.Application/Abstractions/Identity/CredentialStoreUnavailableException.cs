namespace ImpiFact.Application.Abstractions.Identity;

/// <summary>
/// El almacén de credenciales (la base de datos con las API keys, usuarios y tokens) no respondió. Es lo único que
/// los adaptadores de Identity lanzan ante una falla de conexión: la API lo traduce a un 503 (servicio no
/// disponible) con <c>Retry-After</c> y sin detalles internos. El mensaje nunca incluye una credencial.
/// </summary>
public sealed class CredentialStoreUnavailableException(Exception innerException)
    : Exception("The credential store is unavailable.", innerException);
