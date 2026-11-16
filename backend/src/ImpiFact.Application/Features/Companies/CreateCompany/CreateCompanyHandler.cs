using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Companies;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Companies;
using ImpiFact.Domain.Masters;

namespace ImpiFact.Application.Features.Companies.CreateCompany;

/// <summary>
/// Crea la empresa o, si su NIT ya existe, la actualiza (idempotente, como el PHP). Al crearla agrega sus ambientes
/// (habilitación salvo que la solicitud diga otra cosa), sus responsabilidades fiscales, el cliente por defecto y su
/// primera API key, que se devuelve en texto claro esta única vez.
/// </summary>
/// <remarks>
/// El PHP devolvía como <c>token</c> un <c>sha256(NIT)</c> que cualquiera podía calcular («php-rules» 5.1.2). Aquí
/// el <c>token</c> es una API key aleatoria de la empresa con los alcances de una integración (emitir y consultar
/// documentos y cambiar su configuración); de ella solo se guarda el resumen.
/// </remarks>
internal sealed class CreateCompanyHandler(
    ICompanyRepository companies, ITenantContext tenant, IApiKeyGenerator apiKeys, IApiKeyStore apiKeyStore)
    : IHandler<CreateCompanyCommand, CompanySavedResponse>
{
    /// <summary>
    /// Cliente por defecto del PHP (<c>ConfigurationController.php</c>, <c>store</c>): «2222222222», «Cliente por
    /// Defecto», con el correo de la empresa. El modelo de datos propone el consumidor final «222222222222»; se sigue al
    /// PHP por paridad. El tipo de documento, que el PHP no fija, es la cédula de ciudadanía (código DIAN 13, como dice el
    /// modelo), que en el catálogo <c>type_document_identifications</c> tiene el id 3.
    /// </summary>
    internal const string DefaultCustomerNumber = "2222222222";

    internal const string DefaultCustomerName = "Cliente por Defecto";

    internal const int DefaultCustomerDocumentType = 3;

    internal const string InitialApiKeyName = "Llave inicial";

    internal static readonly string[] InitialApiKeyScopes = [ApiScopes.DocumentsWrite, ApiScopes.DocumentsRead, ApiScopes.ConfigWrite];

    public async Task<Result<CompanySavedResponse>> HandleAsync(CreateCompanyCommand request, CancellationToken ct)
    {
        if (tenant.Actor is not (ActorType.Platform or ActorType.System))
        {
            return CompanyErrors.PlatformOnly;
        }

        var existing = await companies.FindByNitAsync(request.Nit!, ct);
        if (await companies.IsEmailTakenAsync(request.Email!, existing?.Id, ct))
        {
            return CompanyErrors.EmailTaken;
        }

        var liabilityIds = request.NormalizedLiabilityIds();
        if (existing is not null)
        {
            CompanyWriter.Apply(existing, request, CompanyWriter.TaxIdFor(request, existing.TaxId));
            await CompanyWriter.SyncLiabilitiesAsync(companies, existing.Id, liabilityIds, ct);
            var environments = await companies.GetEnvironmentsAsync(existing.Id, ct);
            return new CompanySavedResponse { Created = false, Company = CompanyWriter.Response(existing, liabilityIds, environments) };
        }

        var company = CompanyWriter.NewCompany(request, CompanyWriter.TaxIdFor(request, currentTaxId: null));
        companies.Add(company);
        await CompanyWriter.SyncLiabilitiesAsync(companies, company.Id, liabilityIds, ct);
        var newEnvironments = InitialEnvironments(company.Id, request);
        foreach (var environment in newEnvironments)
        {
            companies.Add(environment);
        }

        companies.Add(new Customer
        {
            CompanyId = company.Id,
            IdentificationNumber = DefaultCustomerNumber,
            TypeDocumentIdentificationId = DefaultCustomerDocumentType,
            Name = DefaultCustomerName,
            Email = company.Email,
        });

        // La llave la guarda Identity, dueño de la tabla api_keys, a través de su puerto (regla 4: ningún módulo
        // escribe las tablas de otro). Se guarda en la misma transacción que la empresa.
        var key = apiKeys.Generate();
        await apiKeyStore.AddAsync(
            new NewApiKey(Guid.CreateVersion7(), company.Id, InitialApiKeyName, key.DisplayPrefix, key.Hash, InitialApiKeyScopes, ExpiresAt: null, tenant.UserId),
            ct);

        return new CompanySavedResponse
        {
            Created = true,
            Token = key.PlainText,
            Company = CompanyWriter.Response(company, liabilityIds, newEnvironments),
        };
    }

    private static List<CompanyEnvironment> InitialEnvironments(Guid companyId, CreateCompanyCommand request) =>
    [
        Environment(companyId, DocumentKind.Invoice, request.TypeEnvironmentId),
        Environment(companyId, DocumentKind.Payroll, request.PayrollTypeEnvironmentId),
        Environment(companyId, DocumentKind.EquivalentDocument, request.EqdocsTypeEnvironmentId),
        Environment(companyId, DocumentKind.SupportDocument, request.SupportDocumentTypeEnvironmentId),
        Environment(companyId, DocumentKind.Events, request.EventTypeEnvironmentId),
    ];

    private static CompanyEnvironment Environment(Guid companyId, DocumentKind kind, int? typeEnvironmentId) => new()
    {
        CompanyId = companyId,
        DocumentKind = kind,
        TypeEnvironmentId = typeEnvironmentId ?? Environments.Habilitation,
    };
}
