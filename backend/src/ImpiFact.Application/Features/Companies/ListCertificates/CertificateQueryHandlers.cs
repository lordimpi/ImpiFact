using System.Globalization;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Companies;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Results;

namespace ImpiFact.Application.Features.Companies.ListCertificates;

/// <summary>Lista los certificados de la empresa de la credencial con sus días restantes.</summary>
internal sealed class ListCertificatesHandler(ICompanyRepository companies, ICompanyReads reads, ITenantContext tenant, IClock clock)
    : IHandler<ListCertificatesQuery, CertificatesResponse>
{
    public async Task<Result<CertificatesResponse>> HandleAsync(ListCertificatesQuery request, CancellationToken ct)
    {
        var own = await CompanyAccess.OwnCompanyAsync(companies, tenant, request.Nit, ct);
        if (!own.IsSuccess)
        {
            return own.Error;
        }

        var now = clock.UtcNow;
        var certificates = await reads.ListCertificatesAsync(own.Value.Id, ct);
        return new CertificatesResponse
        {
            Certificates = certificates
                .Select(certificate => CompanyMapper.Map<CertificateResponse>(certificate) with
                {
                    DaysLeft = CertificateDays.Left(certificate.NotAfter, now),
                })
                .ToList(),
        };
    }
}

/// <summary>Fecha de vencimiento del certificado activo, como la devolvía el PHP, más los días restantes.</summary>
internal sealed class GetCertificateEndDateHandler(ICompanyRepository companies, ITenantContext tenant, IClock clock)
    : IHandler<GetCertificateEndDateQuery, CertificateEndDateResponse>
{
    public async Task<Result<CertificateEndDateResponse>> HandleAsync(GetCertificateEndDateQuery request, CancellationToken ct)
    {
        var own = await CompanyAccess.OwnCompanyAsync(companies, tenant, request.Nit, ct);
        if (!own.IsSuccess)
        {
            return own.Error;
        }

        if (await companies.GetActiveCertificateAsync(own.Value.Id, ct) is not { } certificate)
        {
            return CompanyErrors.CertificateNotFound;
        }

        return new CertificateEndDateResponse
        {
            ExpirationDate = CertificateDays.ColombiaDate(certificate.NotAfter).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            DaysLeft = CertificateDays.Left(certificate.NotAfter, clock.UtcNow),
        };
    }
}
