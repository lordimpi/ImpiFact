namespace ImpiFact.Infrastructure.Protection;

/// <summary>
/// La referencia que va a <c>certificates.secret_ref</c>: <c>{esquema}:{empresa}/{blob}</c>, con los dos identificadores
/// en su forma canónica. El esquema dice qué almacén la guardó (<c>local</c>, <c>keyvault</c>, <c>aws</c>,
/// <c>gcp</c>); el blob es un identificador nuevo por cada certificado guardado.
/// </summary>
internal static class CertificateReference
{
    public static string Format(string scheme, Guid companyId, Guid blobId) => $"{scheme}:{companyId:D}/{blobId:D}";

    /// <summary>
    /// Lee la referencia y devuelve el blob. Ambas partes deben ser identificadores canónicos, así la referencia nunca
    /// puede apuntar a otro lugar (por ejemplo con <c>../</c>), y la empresa debe ser la que pide el certificado.
    /// </summary>
    /// <exception cref="ArgumentException">Si la referencia no es válida o es de otra empresa.</exception>
    public static Guid Parse(string scheme, Guid companyId, string secretRef)
    {
        var prefix = scheme + ":";
        var parts = secretRef is not null && secretRef.StartsWith(prefix, StringComparison.Ordinal)
            ? secretRef[prefix.Length..].Split('/')
            : [];
        if (parts is not [var company, var blob]
            || !Guid.TryParseExact(company, "D", out var refCompanyId) || company != refCompanyId.ToString("D")
            || !Guid.TryParseExact(blob, "D", out var blobId) || blob != blobId.ToString("D"))
        {
            throw new ArgumentException("The certificate reference is not valid.", nameof(secretRef));
        }

        if (refCompanyId != companyId)
        {
            throw new ArgumentException("The certificate reference does not belong to the company.", nameof(secretRef));
        }

        return blobId;
    }

    /// <summary>
    /// Nombre del secreto en un servicio de la nube: <c>impifact-cert-{empresa}-{blob}</c>, con los identificadores sin
    /// guiones (78 caracteres de letras, dígitos y «-», válido en Key Vault y en Secret Manager).
    /// </summary>
    public static string SecretName(Guid companyId, Guid blobId) => $"impifact-cert-{companyId:N}-{blobId:N}";
}
