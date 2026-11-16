using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ImpiFact.Dian.Tests.Signing;

/// <summary>Throwaway signing certificates generated in memory; nothing is written to disk.</summary>
public static class TestCertificates
{
    private static readonly Lazy<X509Certificate2> SharedSigner = new(() =>
        CreateSelfSigned(new X500DistinguishedName("CN=ImpiFact Test Signer, O=ImpiFact Pruebas, C=CO")));

    /// <summary>One RSA 2048 self-signed certificate reused by the whole test run.</summary>
    public static X509Certificate2 Signer => SharedSigner.Value;

    public static X509Certificate2 CreateSelfSigned(X500DistinguishedName subject)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, true));
        var notBefore = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return request.CreateSelfSigned(notBefore, notBefore.AddYears(10));
    }
}
