using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Infrastructure.Protection;

/// <summary>
/// La llave de un servicio de la nube con que se envuelven las llaves de Data Protection (ADR-25): una llave RSA de Azure
/// Key Vault, una llave de AWS KMS o una de Google Cloud KMS. La llave nunca sale del servicio: aquí solo se le pide que
/// cifre o descifre 32 bytes.
/// </summary>
internal interface IKeyEncryptionKey
{
    /// <summary>El proveedor (<c>KeyVault</c>, <c>AwsSecrets</c>, <c>GcpSecrets</c>); queda anotado en cada llave.</summary>
    string Provider { get; }

    /// <summary>Envuelve una llave con la llave del servicio; devuelve el resultado y el identificador exacto que lo hizo.</summary>
    (byte[] Wrapped, string KeyId) Wrap(byte[] key);

    /// <summary>Desenvuelve con la llave anotada; falla si esa llave no es la configurada.</summary>
    byte[] Unwrap(string keyId, byte[] wrapped);

    /// <summary>Comprueba que la llave existe y que la identidad puede usarla, para <c>/health/ready</c>.</summary>
    Task ProbeAsync(CancellationToken ct);
}

/// <summary>
/// Cifra cada llave de Data Protection antes de guardarla en <c>data_protection_keys</c> con cifrado de sobre: una llave
/// nueva de 32 bytes al azar cifra el XML con AES-256-GCM (<see cref="AesGcmEnvelope"/>) y la llave del servicio de la
/// nube (<see cref="IKeyEncryptionKey"/>) envuelve esa llave. Es lo que hace <c>ProtectKeysWithAzureKeyVault</c>, con la
/// misma forma para las tres nubes. Sin acceso a la llave del servicio, las filas de la tabla no sirven para nada.
/// </summary>
internal sealed class CloudKeyXmlEncryptor(IKeyEncryptionKey keyEncryptionKey) : IXmlEncryptor
{
    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        ArgumentNullException.ThrowIfNull(plaintextElement);

        var plaintext = Encoding.UTF8.GetBytes(plaintextElement.ToString(SaveOptions.DisableFormatting));
        var key = RandomNumberGenerator.GetBytes(32);
        try
        {
            var (wrapped, keyId) = keyEncryptionKey.Wrap(key);
            var envelope = AesGcmEnvelope.Seal(key, plaintext, AssociatedData(keyEncryptionKey.Provider));
            var element = new XElement(
                "encryptedSecret",
                new XAttribute("provider", keyEncryptionKey.Provider),
                new XAttribute("keyId", keyId),
                new XComment(" This key is encrypted with AES-256-GCM; its key is wrapped by the cloud key named in keyId. "),
                new XElement("wrappedKey", Convert.ToBase64String(wrapped)),
                new XElement("value", Convert.ToBase64String(envelope)));
            return new EncryptedXmlInfo(element, typeof(CloudKeyXmlDecryptor));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>Datos asociados del sobre: atan el XML cifrado al proveedor que envolvió su llave.</summary>
    internal static byte[] AssociatedData(string provider) => Encoding.UTF8.GetBytes($"impifact:data-protection-key:{provider}:v1");
}

/// <summary>
/// Descifra las llaves que cifró <see cref="CloudKeyXmlEncryptor"/> con la llave del servicio configurado. Data Protection
/// la crea por su nombre de tipo, guardado junto a cada llave, y le pasa el contenedor de dependencias; por eso es pública.
/// </summary>
public sealed class CloudKeyXmlDecryptor : IXmlDecryptor
{
    private readonly IKeyEncryptionKey keyEncryptionKey;

    public CloudKeyXmlDecryptor(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        keyEncryptionKey = services.GetRequiredService<IKeyEncryptionKey>();
    }

    public XElement Decrypt(XElement encryptedElement)
    {
        ArgumentNullException.ThrowIfNull(encryptedElement);

        var provider = (string?)encryptedElement.Attribute("provider");
        var keyId = (string?)encryptedElement.Attribute("keyId");
        var wrapped = (string?)encryptedElement.Element("wrappedKey");
        var value = (string?)encryptedElement.Element("value");
        if (provider is null || keyId is null || wrapped is null || value is null)
        {
            throw new CryptographicException("The encrypted key has an unknown format.");
        }

        if (!string.Equals(provider, keyEncryptionKey.Provider, StringComparison.Ordinal))
        {
            throw new CryptographicException(
                $"The key was encrypted by the {provider} secrets provider, but the configured provider is {keyEncryptionKey.Provider}.");
        }

        var key = keyEncryptionKey.Unwrap(keyId, Convert.FromBase64String(wrapped));
        byte[]? plaintext = null;
        try
        {
            plaintext = AesGcmEnvelope.Open(key, Convert.FromBase64String(value), CloudKeyXmlEncryptor.AssociatedData(provider));
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
