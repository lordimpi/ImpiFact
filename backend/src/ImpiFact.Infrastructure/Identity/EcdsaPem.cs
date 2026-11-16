using System.Security.Cryptography;

namespace ImpiFact.Infrastructure.Identity;

/// <summary>
/// Lee llaves ECDSA (firma digital con curvas elípticas) de la curva P-256 escritas en PEM (el formato de texto
/// <c>-----BEGIN …-----</c>). Solo se acepta P-256 porque los tokens se firman con ES256 (ECDSA P-256 con SHA-256).
/// Los errores nunca incluyen el contenido de la llave.
/// </summary>
internal static class EcdsaPem
{
    private const string P256Oid = "1.2.840.10045.3.1.7";

    /// <summary>Lee una llave privada; devuelve <see langword="null"/> si el texto no es una llave privada P-256.</summary>
    public static ECDsa? TryImportPrivate(string pem)
    {
        var key = TryImport(pem);
        if (key is null)
        {
            return null;
        }

        try
        {
            // Una llave pública también se importa sin error; exportar la parte privada confirma que la hay.
            key.ExportParameters(includePrivateParameters: true);
            return key;
        }
        catch (CryptographicException)
        {
            key.Dispose();
            return null;
        }
    }

    /// <summary>Lee una llave pública (o la parte pública de una privada); <see langword="null"/> si no es P-256.</summary>
    public static ECDsa? TryImportPublic(string pem) => TryImport(pem);

    /// <summary>
    /// Indica si el texto trae una llave privada: un bloque PEM cuya etiqueta termina en «PRIVATE KEY» (también las
    /// cifradas) o un texto que se importa con parte privada.
    /// </summary>
    public static bool ContainsPrivateKey(string pem)
    {
        if (pem.Contains("PRIVATE KEY-----", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        using var key = TryImportPrivate(pem);
        return key is not null;
    }

    private static ECDsa? TryImport(string pem)
    {
        var key = ECDsa.Create();
        try
        {
            key.ImportFromPem(pem);
            if (key.ExportParameters(includePrivateParameters: false).Curve.Oid.Value == P256Oid)
            {
                return key;
            }
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException)
        {
            // El texto no es una llave válida; quien llama informa el error sin mostrar el contenido.
        }

        key.Dispose();
        return null;
    }
}
