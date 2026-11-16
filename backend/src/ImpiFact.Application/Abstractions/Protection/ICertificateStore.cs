using System.Security.Cryptography.X509Certificates;

namespace ImpiFact.Application.Abstractions.Protection;

/// <summary>
/// Guarda el certificado de firma de cada empresa (Arquitectura detallada, sección 5). El certificado llega como
/// PKCS#12 (también llamado PFX o .p12: un archivo que reúne el certificado y su llave privada, protegido con una
/// contraseña). La base de datos solo guarda la referencia que devuelve <see cref="SaveAsync"/>
/// (<c>certificates.secret_ref</c>), nunca el certificado ni su contraseña.
/// </summary>
/// <remarks>
/// Cada operación recibe la empresa dueña, que el llamador toma de una fuente confiable (la solicitud y la fila
/// de <c>certificates</c>). La implementación comprueba que la referencia y el contenido guardado pertenezcan a
/// esa empresa, así una referencia de otra empresa nunca abre su certificado.
/// </remarks>
public interface ICertificateStore
{
    /// <summary>Guarda el PKCS#12 y su contraseña, y devuelve la referencia para <c>certificates.secret_ref</c>.</summary>
    Task<string> SaveAsync(Guid companyId, byte[] pfx, string password, CancellationToken ct);

    /// <summary>
    /// Abre el certificado en memoria, con su llave privada; nunca lo escribe a disco. Quien lo pide lo libera
    /// (<c>Dispose</c>) al terminar de firmar.
    /// </summary>
    /// <exception cref="ArgumentException">Si la referencia no tiene el formato esperado o es de otra empresa.</exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// Si el contenido guardado fue alterado o no pertenece a la empresa.
    /// </exception>
    /// <exception cref="FileNotFoundException">Si la referencia no existe.</exception>
    Task<X509Certificate2> LoadAsync(Guid companyId, string secretRef, CancellationToken ct);

    /// <summary>Borra el certificado guardado. No falla si ya no existía.</summary>
    /// <exception cref="ArgumentException">Si la referencia no tiene el formato esperado o es de otra empresa.</exception>
    Task DeleteAsync(Guid companyId, string secretRef, CancellationToken ct);
}
