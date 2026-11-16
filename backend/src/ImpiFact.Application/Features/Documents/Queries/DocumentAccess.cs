using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Results;

namespace ImpiFact.Application.Features.Documents.Queries;

/// <summary>Resuelve la empresa sobre la que actúa una consulta con NIT (Número de Identificación Tributaria) en la ruta.</summary>
internal static class DocumentAccess
{
    /// <summary>
    /// Devuelve la empresa de la credencial si el NIT de la ruta es el suyo. Si no lo es, responde
    /// <see cref="DocumentErrors.ForeignNit"/> sin buscar ese NIT: así la respuesta es la misma exista o no.
    /// </summary>
    public static async Task<Result<DocumentCompany>> OwnCompanyAsync(IDocumentReads reads, ITenantContext tenant, string nit, CancellationToken ct)
    {
        if (tenant.CompanyId is not { } companyId || await reads.FindCompanyAsync(companyId, ct) is not { } company)
        {
            return DocumentErrors.ForeignNit;
        }

        return string.Equals(nit, company.IdentificationNumber, StringComparison.Ordinal) ? company : DocumentErrors.ForeignNit;
    }
}
