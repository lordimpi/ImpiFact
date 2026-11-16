using System.Text.Json.Serialization;

namespace ImpiFact.Application.Features.Companies.ListCertificates;

/// <summary>
/// Certificados de la empresa de la credencial (<c>GET certificates-listing/{nit}</c>): solo metadatos. En el PHP
/// esta consulta era de administradores y listaba certificados de cualquier empresa; aquí cada empresa ve los suyos.
/// </summary>
public sealed record ListCertificatesQuery(string? Nit);

public sealed record CertificatesResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; init; } = true;

    [JsonPropertyName("certificates")]
    public IReadOnlyList<CertificateResponse> Certificates { get; init; } = [];
}

/// <summary>Vencimiento del certificado activo (<c>PUT certificate-end-date</c> del PHP).</summary>
public sealed record GetCertificateEndDateQuery(string? Nit);

public sealed record CertificateEndDateResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; init; } = true;

    /// <summary>Fecha de vencimiento en la hora de Colombia, con el formato del PHP (<c>dd/MM/yyyy</c>).</summary>
    [JsonPropertyName("expiration_date")]
    public string ExpirationDate { get; init; } = "";

    [JsonPropertyName("certificate_days_left")]
    public int DaysLeft { get; init; }
}
