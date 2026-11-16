using ImpiFact.Api.Endpoints;
using ImpiFact.Api.Http;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Features.Companies.CreateCompany;
using ImpiFact.Application.Features.Companies.ListCertificates;
using ImpiFact.Application.Features.Companies.SetEnvironment;
using ImpiFact.Application.Features.Companies.SetSoftware;
using ImpiFact.Application.Features.Companies.UpdateCompany;
using ImpiFact.Application.Features.Companies.UploadCertificate;
using ImpiFact.Application.Features.Companies.UpsertResolution;
using ImpiFact.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;

namespace ImpiFact.Api.Companies;

/// <summary>
/// Endpoints del módulo Company (Arquitectura detallada, sección 9.3). La ruta canónica es
/// <c>/api/v1/companies/{nit}/...</c>; las del contrato heredado (<c>/api/ubl2.1/...</c>, anexo A.5) son alias del
/// mismo caso de uso. En las canónicas el NIT debe ser el de la empresa de la credencial (si no, 403 sin revelar si
/// existe); las heredadas no traen NIT y actúan sobre la empresa de la credencial. Cada endpoint solo arma la
/// solicitud, llama al caso de uso y traduce el resultado.
/// </summary>
internal sealed class CompanyEndpoints : IEndpointModule
{
    private const string Tag = "Companies";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var canonical = endpoints.MapGroup("/api/v1/companies").WithTags(Tag);
        var legacy = endpoints.MapGroup("/api/ubl2.1").WithTags(Tag);

        // Alta o actualización por NIT: operación de plataforma.
        canonical.MapPost("/{nit}/{dv}", CreateCompany).RequireAuthorization(ApiScopes.Platform);

        // Ruta heredada del alta. La plataforma crea o actualiza cualquier empresa; una empresa con config:write solo
        // actualiza la suya, como cuando el PHP recibía de nuevo el alta de una empresa existente.
        legacy.MapPost("/config/{nit}/{dv?}", CreateOrUpdateCompany).RequireAuthorization(PlatformOrConfigWrite);

        canonical.MapPut("/{nit}/{dv}", UpdateCompany).RequireAuthorization(ApiScopes.ConfigWrite);

        canonical.MapPut("/{nit}/environment", SetEnvironment).RequireAuthorization(ApiScopes.ConfigWrite);
        legacy.MapPut("/config/environment", SetEnvironment).RequireAuthorization(ApiScopes.ConfigWrite);

        canonical.MapPut("/{nit}/software", SetSoftware).RequireAuthorization(ApiScopes.ConfigWrite);
        legacy.MapPut("/config/software", SetSoftware).RequireAuthorization(ApiScopes.ConfigWrite);
        legacy.MapPut("/config/softwaresupportdocument", SetSoftware).RequireAuthorization(ApiScopes.ConfigWrite);

        canonical.MapPut("/{nit}/certificate", UploadCertificate).RequireAuthorization(ApiScopes.ConfigWrite);
        legacy.MapPut("/config/certificate", UploadCertificate).RequireAuthorization(ApiScopes.ConfigWrite);

        canonical.MapPut("/{nit}/resolution", UpsertResolution).RequireAuthorization(ApiScopes.ConfigWrite);
        legacy.MapPut("/config/resolution", UpsertResolution).RequireAuthorization(ApiScopes.ConfigWrite);

        canonical.MapGet("/{nit}/certificates", ListCertificates).RequireAuthorization(ApiScopes.ConfigWrite);
        legacy.MapGet("/certificates-listing/{nit?}", ListCertificates).RequireAuthorization(ApiScopes.ConfigWrite);

        canonical.MapGet("/{nit}/certificate/end-date", CertificateEndDate).RequireAuthorization(ApiScopes.ConfigWrite);
        legacy.MapPut("/certificate-end-date", CertificateEndDate).RequireAuthorization(ApiScopes.ConfigWrite);
    }

    /// <summary>
    /// Credencial de plataforma (alcance <c>platform:*</c> sin empresa, igual que la política de plataforma) o de una
    /// empresa con <c>config:write</c>.
    /// </summary>
    private static void PlatformOrConfigWrite(AuthorizationPolicyBuilder policy) => policy
        .RequireAuthenticatedUser()
        .RequireAssertion(context =>
            context.User.HasClaim(IdentityClaimTypes.Scope, ApiScopes.ConfigWrite)
            || (context.User.HasClaim(IdentityClaimTypes.Scope, ApiScopes.Platform)
                && !context.User.HasClaim(claim => claim.Type == IdentityClaimTypes.CompanyId)));

    private static async Task<IResult> CreateCompany(
        string nit, string dv, CreateCompanyCommand body, IHandler<CreateCompanyCommand, CompanySavedResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(body with { Nit = nit, Dv = dv }, ct)).ToHttpResult();

    private static async Task<IResult> CreateOrUpdateCompany(
        string nit,
        string? dv,
        CreateCompanyCommand body,
        ITenantContext tenant,
        IHandler<CreateCompanyCommand, CompanySavedResponse> create,
        IHandler<UpdateCompanyCommand, CompanySavedResponse> update,
        CancellationToken ct)
    {
        var command = body with { Nit = nit, Dv = dv };
        var result = tenant.Actor == ActorType.Platform
            ? await create.HandleAsync(command, ct)
            : await update.HandleAsync(new UpdateCompanyCommand(command), ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> UpdateCompany(
        string nit, string dv, UpdateCompanyCommand body, IHandler<UpdateCompanyCommand, CompanySavedResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(body with { Nit = nit, Dv = dv }, ct)).ToHttpResult();

    private static async Task<IResult> SetEnvironment(
        string? nit, SetEnvironmentCommand body, IHandler<SetEnvironmentCommand, EnvironmentsResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(body with { Nit = nit }, ct)).ToHttpResult();

    private static async Task<IResult> SetSoftware(
        string? nit, SetSoftwareCommand body, IHandler<SetSoftwareCommand, SoftwareSavedResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(body with { Nit = nit }, ct)).ToHttpResult();

    private static async Task<IResult> UploadCertificate(
        string? nit, UploadCertificateCommand body, IHandler<UploadCertificateCommand, CertificateSavedResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(body with { Nit = nit }, ct)).ToHttpResult();

    private static async Task<IResult> UpsertResolution(
        string? nit, UpsertResolutionCommand body, IHandler<UpsertResolutionCommand, ResolutionSavedResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(body with { Nit = nit }, ct)).ToHttpResult();

    private static async Task<IResult> ListCertificates(
        string? nit, IHandler<ListCertificatesQuery, CertificatesResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new ListCertificatesQuery(nit), ct)).ToHttpResult();

    private static async Task<IResult> CertificateEndDate(
        string? nit, IHandler<GetCertificateEndDateQuery, CertificateEndDateResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetCertificateEndDateQuery(nit), ct)).ToHttpResult();
}
