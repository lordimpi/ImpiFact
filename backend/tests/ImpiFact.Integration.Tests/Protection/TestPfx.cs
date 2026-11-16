using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ImpiFact.Integration.Tests.Protection;

/// <summary>
/// Certificados PKCS#12 (el formato .p12 o .pfx: certificado y llave privada protegidos con contraseña)
/// autofirmados y generados en memoria para cada prueba. Nada se escribe en el repositorio.
/// </summary>
public static class TestPfx
{
    public const string Password = "test-password-1";

    /// <summary>Un PKCS#12 con certificado RSA de 2048 bits y su llave privada, vigente en el rango indicado.</summary>
    public static byte[] Rsa(DateTimeOffset notBefore, DateTimeOffset notAfter, string password = Password)
    {
        using var certificate = CreateRsa(notBefore, notAfter);
        return certificate.Export(X509ContentType.Pkcs12, password);
    }

    /// <summary>Un PKCS#12 vigente diez años a partir de 2026.</summary>
    public static byte[] Rsa() => Rsa(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2036, 1, 1, 0, 0, 0, TimeSpan.Zero));

    /// <summary>Un PKCS#12 que solo trae el certificado, sin la llave privada.</summary>
    public static byte[] WithoutPrivateKey()
    {
        using var certificate = CreateRsa(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2036, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var publicOnly = X509CertificateLoader.LoadCertificate(certificate.RawData);
        return publicOnly.Export(X509ContentType.Pkcs12, Password);
    }

    /// <summary>Un PKCS#12 con llave privada de curva elíptica (ECDSA), que no sirve para la firma RSA de la DIAN.</summary>
    public static byte[] Ecdsa()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=ImpiFact Test ECDSA, C=CO", key, HashAlgorithmName.SHA256);
        using var certificate = request.CreateSelfSigned(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2036, 1, 1, 0, 0, 0, TimeSpan.Zero));
        return certificate.Export(X509ContentType.Pkcs12, Password);
    }

    private static X509Certificate2 CreateRsa(DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=ImpiFact Test Company, O=ImpiFact Pruebas, C=CO", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, true));
        return request.CreateSelfSigned(notBefore, notAfter);
    }
}
