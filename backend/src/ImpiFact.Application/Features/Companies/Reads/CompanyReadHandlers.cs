using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Companies;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Maintenance;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Companies;

namespace ImpiFact.Application.Features.Companies.Reads;

/// <summary>Datos de la empresa de la credencial, con sus responsabilidades y ambientes.</summary>
internal sealed class GetCompanyHandler(ICompanyRepository companies, ITenantContext tenant) : IHandler<GetCompanyQuery, CompanyResponse>
{
    public async Task<Result<CompanyResponse>> HandleAsync(GetCompanyQuery request, CancellationToken ct)
    {
        var own = await CompanyAccess.OwnCompanyAsync(companies, tenant, request.Nit, ct);
        if (!own.IsSuccess)
        {
            return own.Error;
        }

        var company = own.Value;
        var liabilities = await companies.GetLiabilitiesAsync(company.Id, ct);
        var environments = await companies.GetEnvironmentsAsync(company.Id, ct);
        return CompanyWriter.Response(company, liabilities.Select(liability => liability.TypeLiabilityId).Order().ToList(), environments);
    }
}

/// <summary>Ambiente de cada familia de documentos de la empresa de la credencial.</summary>
internal sealed class GetCompanyEnvironmentsHandler(ICompanyRepository companies, ITenantContext tenant)
    : IHandler<GetCompanyEnvironmentsQuery, CompanyEnvironmentsResponse>
{
    public async Task<Result<CompanyEnvironmentsResponse>> HandleAsync(GetCompanyEnvironmentsQuery request, CancellationToken ct)
    {
        var own = await CompanyAccess.OwnCompanyAsync(companies, tenant, request.Nit, ct);
        if (!own.IsSuccess)
        {
            return own.Error;
        }

        var environments = await companies.GetEnvironmentsAsync(own.Value.Id, ct);
        return new CompanyEnvironmentsResponse { Environments = CompanyReads.Environments(environments) };
    }
}

/// <summary>Software de la empresa de la credencial: el identificador, la URL y el set de pruebas; del PIN solo si existe.</summary>
internal sealed class GetCompanySoftwareHandler(ICompanyRepository companies, ITenantContext tenant)
    : IHandler<GetCompanySoftwareQuery, CompanySoftwareResponse>
{
    public async Task<Result<CompanySoftwareResponse>> HandleAsync(GetCompanySoftwareQuery request, CancellationToken ct)
    {
        var own = await CompanyAccess.OwnCompanyAsync(companies, tenant, request.Nit, ct);
        if (!own.IsSuccess)
        {
            return own.Error;
        }

        var software = await companies.GetSoftwareAsync(own.Value.Id, ct);
        return new CompanySoftwareResponse
        {
            Software = software
                .OrderBy(row => row.DocumentKind)
                .ThenBy(row => row.TypeEnvironmentId)
                .Select(row => new ConfiguredSoftwareResponse
                {
                    DocumentKind = (int)row.DocumentKind,
                    TypeEnvironmentId = row.TypeEnvironmentId,
                    Identifier = row.SoftwareIdentifier,
                    Url = row.DianUrl,
                    TestSetId = row.TestSetId,
                    HasPin = !string.IsNullOrEmpty(row.PinCipher),
                })
                .ToList(),
        };
    }
}

/// <summary>Resoluciones de la empresa de la credencial con el próximo número, lo que queda y los días de vigencia.</summary>
internal sealed class ListCompanyResolutionsHandler(
    ICompanyRepository companies, ICompanyConfigurationReads reads, ITenantContext tenant, IClock clock)
    : IHandler<ListCompanyResolutionsQuery, CompanyResolutionsResponse>
{
    public async Task<Result<CompanyResolutionsResponse>> HandleAsync(ListCompanyResolutionsQuery request, CancellationToken ct)
    {
        var own = await CompanyAccess.OwnCompanyAsync(companies, tenant, request.Nit, ct);
        if (!own.IsSuccess)
        {
            return own.Error;
        }

        var today = CertificateDays.ColombiaDate(clock.UtcNow);
        var resolutions = await reads.ListResolutionsAsync(own.Value.Id, ct);
        return new CompanyResolutionsResponse
        {
            Resolutions = resolutions.Select(resolution =>
            {
                var alert = CompanyReads.Evaluate(resolution, today);
                return new ResolutionStatusResponse
                {
                    Id = resolution.Id,
                    Kind = CompanyReads.KindOf(resolution.DocumentTypeId),
                    TypeDocumentId = resolution.DocumentTypeId,
                    TypeEnvironmentId = resolution.TypeEnvironmentId,
                    Prefix = resolution.Prefix,
                    Resolution = resolution.ResolutionNumber,
                    ResolutionDate = resolution.ResolutionDate,
                    From = resolution.RangeFrom,
                    To = resolution.RangeTo,
                    DateFrom = resolution.ValidFrom,
                    DateTo = resolution.ValidTo,
                    IsActive = resolution.IsActive,
                    HasTechnicalKey = resolution.HasTechnicalKey,
                    NextNumber = CompanyReads.NextNumber(resolution),
                    Remaining = alert.Remaining,
                    DaysLeft = alert.DaysLeft,
                };
            }).ToList(),
        };
    }
}

/// <summary>
/// Estado del paso a producción de la empresa de la credencial: lo calcula con lo guardado, sin preguntar a la DIAN.
/// </summary>
internal sealed class GetCompanySetupStatusHandler(
    ICompanyRepository companies, ICompanyConfigurationReads reads, ITenantContext tenant, IClock clock)
    : IHandler<GetCompanySetupStatusQuery, CompanySetupStatusResponse>
{
    public async Task<Result<CompanySetupStatusResponse>> HandleAsync(GetCompanySetupStatusQuery request, CancellationToken ct)
    {
        var own = await CompanyAccess.OwnCompanyAsync(companies, tenant, request.Nit, ct);
        if (!own.IsSuccess)
        {
            return own.Error;
        }

        var company = own.Value;
        var now = clock.UtcNow;
        var today = CertificateDays.ColombiaDate(now);
        var liabilities = await companies.GetLiabilitiesAsync(company.Id, ct);
        var environments = await companies.GetEnvironmentsAsync(company.Id, ct);
        var software = await companies.GetSoftwareAsync(company.Id, ct);
        var certificate = await companies.GetActiveCertificateAsync(company.Id, ct);
        var resolutions = await reads.ListResolutionsAsync(company.Id, ct);
        var testSet = await reads.FindTestSetResultAsync(company.Id, ct);

        // La factura y sus notas comparten familia: el software y las resoluciones que cuentan son los de su ambiente actual.
        var invoiceEnvironment = Environments.CurrentFor(environments, DocumentKind.Invoice);
        var invoiceSoftware = software.FirstOrDefault(row => row.DocumentKind == DocumentKind.Invoice && row.TypeEnvironmentId == invoiceEnvironment);
        var supportEnvironment = Environments.CurrentFor(environments, DocumentKind.SupportDocument);
        return new CompanySetupStatusResponse
        {
            CompanyComplete = IsComplete(company) && liabilities.Count > 0,
            CertificateValid = certificate is not null && CertificateDays.Left(certificate.NotAfter, now) > 0,
            SoftwareConfigured = invoiceSoftware is not null,
            TestSetIdConfigured = !string.IsNullOrWhiteSpace(invoiceSoftware?.TestSetId),
            TestSetAccepted = testSet,
            Environments = CompanyReads.Environments(environments),
            Resolutions = CompanyReads.InvoiceFamily
                .Select(entry => new ResolutionReadiness
                {
                    Kind = entry.Kind,
                    TypeDocumentId = entry.DocumentTypeId,
                    Valid = resolutions.Any(resolution => resolution.DocumentTypeId == entry.DocumentTypeId
                        && resolution.TypeEnvironmentId == invoiceEnvironment
                        && CompanyReads.IsUsable(resolution, today)),
                })
                .ToList(),
            SupportDocument = new SupportDocumentReadiness
            {
                TypeEnvironmentId = supportEnvironment,
                SoftwareConfigured = software.Any(row => row.DocumentKind == DocumentKind.SupportDocument && row.TypeEnvironmentId == supportEnvironment),
                Resolutions = CompanyReads.SupportDocumentFamily
                    .Select(entry => new ResolutionReadiness
                    {
                        Kind = entry.Kind,
                        TypeDocumentId = entry.DocumentTypeId,
                        Valid = resolutions.Any(resolution => resolution.DocumentTypeId == entry.DocumentTypeId
                            && resolution.TypeEnvironmentId == supportEnvironment
                            && CompanyReads.IsUsable(resolution, today)),
                    })
                    .ToList(),
            },
        };
    }

    /// <summary>Los mismos datos obligatorios que exige el alta (<c>CompanyDataValidator</c>).</summary>
    private static bool IsComplete(Company company) =>
        !string.IsNullOrWhiteSpace(company.Name)
        && !string.IsNullOrWhiteSpace(company.Email)
        && !string.IsNullOrWhiteSpace(company.Phone)
        && !string.IsNullOrWhiteSpace(company.Address)
        && !string.IsNullOrWhiteSpace(company.MerchantRegistration);
}

/// <summary>Reglas compartidas por las lecturas de la configuración.</summary>
internal static class CompanyReads
{
    /// <summary>
    /// Tipos de documento de la familia de la factura con el nombre que usa el resto de la API (los mismos de
    /// <c>DocumentKinds</c> del módulo Documents, que este módulo no puede usar).
    /// </summary>
    public static IReadOnlyList<(string Kind, int DocumentTypeId)> InvoiceFamily { get; } =
        [("invoice", 1), ("credit_note", 4), ("debit_note", 5)];

    /// <summary>Types of the support-document family, with the names of the document queries.</summary>
    public static IReadOnlyList<(string Kind, int DocumentTypeId)> SupportDocumentFamily { get; } =
        [("support_document", 11), ("support_document_adjustment_note", 13)];

    public static string? KindOf(int documentTypeId) =>
        InvoiceFamily.Where(entry => entry.DocumentTypeId == documentTypeId).Select(entry => entry.Kind).FirstOrDefault();

    public static IReadOnlyList<EnvironmentResponse> Environments(IEnumerable<CompanyEnvironment> environments) =>
        environments.OrderBy(environment => environment.DocumentKind).Select(CompanyMapper.Map<EnvironmentResponse>).ToList();

    /// <summary>El número del próximo documento: el del consecutivo, nunca por debajo del inicio del rango.</summary>
    public static long NextNumber(ResolutionRecord resolution) => Math.Max(resolution.NextNumber ?? resolution.RangeFrom, resolution.RangeFrom);

    /// <summary>Números que quedan y días de vigencia, con la misma regla de los avisos del tablero.</summary>
    public static ResolutionAlert Evaluate(ResolutionRecord resolution, DateOnly today) =>
        ResolutionAlertRule.Evaluate(resolution.RangeFrom, resolution.RangeTo, resolution.NextNumber, resolution.ValidTo, today);

    /// <summary>
    /// Se puede emitir hoy con ella: activa, con números por usar y dentro de su vigencia. La de factura además necesita
    /// clave técnica y las dos fechas de vigencia, como pide la emisión.
    /// </summary>
    public static bool IsUsable(ResolutionRecord resolution, DateOnly today) =>
        resolution.IsActive
        && Evaluate(resolution, today).Remaining > 0
        && (resolution.ValidFrom is not { } from || from <= today)
        && (resolution.ValidTo is not { } to || to >= today)
        && (resolution.DocumentTypeId != 1 || resolution is { HasTechnicalKey: true, ValidFrom: not null, ValidTo: not null })
        && (resolution.DocumentTypeId != 11 || resolution is { ValidFrom: not null, ValidTo: not null, ResolutionNumber: not (null or "") });
}
