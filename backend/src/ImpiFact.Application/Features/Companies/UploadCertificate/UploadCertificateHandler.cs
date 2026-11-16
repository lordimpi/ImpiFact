using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Companies;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Protection;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Domain.Companies;

namespace ImpiFact.Application.Features.Companies.UploadCertificate;

/// <summary>
/// Revisa el certificado en memoria (contraseña, llave privada RSA y vigencia), lo guarda cifrado en el almacén de
/// certificados y deja en <c>certificates</c> solo la referencia (<c>secret_ref</c>), la huella y la vigencia. El
/// certificado activo anterior queda inactivo: hay uno solo activo por empresa. El PHP mandaba los certificados
/// antiguos a un servicio externo para convertirlos; aquí nunca salen de la plataforma.
/// </summary>
/// <remarks>
/// El almacén guarda el archivo antes de que la transacción confirme. Si la transacción falla después, queda un
/// archivo cifrado sin fila que lo referencie; no se borra aquí porque borrarlo antes de confirmar dejaría sin
/// certificado a la empresa si fallara lo contrario.
/// </remarks>
internal sealed class UploadCertificateHandler(
    ICompanyRepository companies, ITenantContext tenant, ICertificateInspector inspector, ICertificateStore store)
    : IHandler<UploadCertificateCommand, CertificateSavedResponse>
{
    public async Task<Result<CertificateSavedResponse>> HandleAsync(UploadCertificateCommand request, CancellationToken ct)
    {
        var own = await CompanyAccess.OwnCompanyAsync(companies, tenant, request.Nit, ct);
        if (!own.IsSuccess)
        {
            return own.Error;
        }

        var company = own.Value;
        var pfx = UploadCertificateValidator.TryDecode(request.Certificate)!;
        var inspection = inspector.Inspect(pfx, request.Password!);
        if (!inspection.IsSuccess)
        {
            return inspection.Error;
        }

        // Brecha del modelo: certificates.uploaded_by es obligatorio y apunta a users (ver CompanyErrors).
        if (tenant.UserId is not { } uploadedBy)
        {
            return CompanyErrors.CertificateUploaderRequired;
        }

        if (await companies.GetActiveCertificateAsync(company.Id, ct) is { } previous)
        {
            previous.IsActive = false;
        }

        var details = inspection.Value;
        var secretRef = await store.SaveAsync(company.Id, pfx, request.Password!, ct);
        var certificate = new Certificate
        {
            CompanyId = company.Id,
            Name = $"{company.IdentificationNumber}{company.Dv}.p12",
            SecretRef = secretRef,
            Thumbprint = details.Thumbprint,
            Subject = details.Subject,
            Issuer = details.Issuer,
            NotBefore = details.NotBefore,
            NotAfter = details.NotAfter,
            IsActive = true,
            UploadedBy = uploadedBy,
        };
        companies.Add(certificate);

        return new CertificateSavedResponse
        {
            DaysLeft = details.DaysLeft,
            Certificate = CompanyMapper.Map<CertificateResponse>(certificate) with { DaysLeft = details.DaysLeft },
        };
    }
}
