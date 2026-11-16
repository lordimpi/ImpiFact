using System.Text.Json.Serialization;
using ImpiFact.Application.Abstractions.Handlers;

namespace ImpiFact.Application.Features.Companies.UploadCertificate;

/// <summary>
/// Sube el certificado de firma de la empresa (<c>PUT config/certificate</c>): el PKCS#12 (archivo .p12 o .pfx con el
/// certificado y su llave privada) en base64 y su contraseña, como en <c>ConfigurationCertificateRequest.php</c>.
/// Ni el archivo ni la contraseña se devuelven ni se registran.
/// </summary>
public sealed record UploadCertificateCommand : ICommand
{
    [JsonIgnore]
    public string? Nit { get; init; }

    /// <summary>PKCS#12 en base64.</summary>
    [JsonPropertyName("certificate")]
    public string? Certificate { get; init; }

    [JsonPropertyName("password")]
    public string? Password { get; init; }

    /// <summary>No incluye el certificado ni la contraseña, para que un registro accidental del objeto no los filtre.</summary>
    public override string ToString() => "UploadCertificateCommand { certificate and password hidden }";
}

/// <summary>Respuesta con el mensaje del PHP, los metadatos del certificado y los días que le quedan.</summary>
public sealed record CertificateSavedResponse
{
    public const string SavedMessage = "Certificado creado con éxito";

    [JsonPropertyName("success")]
    public bool Success { get; init; } = true;

    [JsonPropertyName("message")]
    public string Message { get; init; } = SavedMessage;

    /// <summary>Días que faltan para el vencimiento (<c>certificate_days_left</c> del PHP).</summary>
    [JsonPropertyName("certificate_days_left")]
    public int DaysLeft { get; init; }

    [JsonPropertyName("certificate")]
    public required CertificateResponse Certificate { get; init; }
}
