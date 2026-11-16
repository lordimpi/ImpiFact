namespace ImpiFact.Application.Abstractions.Companies;

/// <summary>Consultas de solo lectura del módulo Company; no rastrean cambios ni abren transacción.</summary>
public interface ICompanyReads
{
    /// <summary>Certificados de la empresa, el activo primero y luego del más reciente al más antiguo.</summary>
    Task<IReadOnlyList<CertificateMetadata>> ListCertificatesAsync(Guid companyId, CancellationToken ct);
}

/// <summary>Datos públicos de un certificado guardado: nunca el archivo, su contraseña ni la referencia al secreto.</summary>
public sealed record CertificateMetadata(
    Guid Id,
    string Name,
    string Thumbprint,
    string Subject,
    string Issuer,
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter,
    bool IsActive,
    DateTimeOffset UploadedAt);
