using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Infrastructure.Protection;

/// <summary>
/// Cifra cada llave de Data Protection (el sistema de ASP.NET Core que cifra los campos <c>*_cipher</c>) antes de
/// guardarla en <c>data_protection_keys</c>, con AES-256-GCM y una llave derivada de la llave maestra local
/// (ADR-25). Sin la llave maestra, las filas de la tabla no sirven para descifrar nada.
/// </summary>
/// <remarks>
/// Es la protección de la instalación mínima. Con un proveedor de nube se cambiará por el de ese proveedor:
/// <c>ProtectKeysWithAzureKeyVault</c> en Azure, una llave de KMS en AWS o de Cloud KMS en Google Cloud
/// (Arquitectura detallada, sección 19.2).
/// </remarks>
internal sealed class LocalAesGcmXmlEncryptor(LocalMasterKey masterKey) : IXmlEncryptor
{
    internal const string KeyPurpose = "impifact/data-protection-keys/v1";

    internal static readonly byte[] AssociatedData = "impifact:data-protection-key:v1"u8.ToArray();

    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        ArgumentNullException.ThrowIfNull(plaintextElement);

        var plaintext = Encoding.UTF8.GetBytes(plaintextElement.ToString(SaveOptions.DisableFormatting));
        var key = masterKey.DeriveKey(KeyPurpose);
        try
        {
            var envelope = AesGcmEnvelope.Seal(key, plaintext, AssociatedData);
            var element = new XElement(
                "encryptedSecret",
                new XComment(" This key is encrypted with AES-256-GCM and the local master key. "),
                new XElement("value", Convert.ToBase64String(envelope)));
            return new EncryptedXmlInfo(element, typeof(LocalAesGcmXmlDecryptor));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}

/// <summary>
/// Descifra las llaves que cifró <see cref="LocalAesGcmXmlEncryptor"/>. Data Protection la crea por su nombre de
/// tipo, guardado junto a cada llave, y le pasa el contenedor de dependencias; por eso es pública.
/// </summary>
public sealed class LocalAesGcmXmlDecryptor : IXmlDecryptor
{
    private readonly LocalMasterKey masterKey;

    public LocalAesGcmXmlDecryptor(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        masterKey = services.GetRequiredService<LocalMasterKey>();
    }

    public XElement Decrypt(XElement encryptedElement)
    {
        ArgumentNullException.ThrowIfNull(encryptedElement);

        var value = (string?)encryptedElement.Element("value")
            ?? throw new CryptographicException("The encrypted key has an unknown format.");
        var key = masterKey.DeriveKey(LocalAesGcmXmlEncryptor.KeyPurpose);
        byte[]? plaintext = null;
        try
        {
            plaintext = AesGcmEnvelope.Open(key, Convert.FromBase64String(value), LocalAesGcmXmlEncryptor.AssociatedData);
            return XElement.Parse(Encoding.UTF8.GetString(plaintext));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
    }
}
