using ImpiFact.Api.Endpoints;
using ImpiFact.Api.Http;
using ImpiFact.Api.OpenApi;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Features.Companies.CreateCompany;
using ImpiFact.Application.Features.Companies.ListCertificates;
using ImpiFact.Application.Features.Companies.NumberingRange;
using ImpiFact.Application.Features.Companies.SetEnvironment;
using ImpiFact.Application.Features.Companies.SetSoftware;
using ImpiFact.Application.Features.Companies.UpdateCompany;
using ImpiFact.Application.Features.Companies.UploadCertificate;
using ImpiFact.Application.Features.Companies.UpsertResolution;
using ImpiFact.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
    /// <summary>Recordatorio que llevan las rutas que reciben el NIT de la empresa.</summary>
    private const string OwnNit =
        " El `{nit}` debe ser el de la empresa de la credencial; otro NIT responde 403 (`company.forbidden`).";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var canonical = endpoints.MapGroup("/api/v1/companies").WithTags(ApiTags.Companies);
        // Contrato heredado del PHP: sigue activo para la colección Postman, fuera de la documentación; se borra en la fase 7.
        var legacy = endpoints.MapGroup("/api/ubl2.1").WithTags(ApiTags.Companies).ExcludeFromDescription();

        // Alta o actualización por NIT: operación de plataforma.
        canonical.MapPost("/{nit}/{dv}", CreateCompany).RequireAuthorization(ApiScopes.Platform)
            .Produces<CompanySavedResponse>()
            .WithSummary("Crear o actualizar una empresa")
            .WithDescription(
                "Crea la empresa emisora con su NIT (número de identificación tributaria) y su dígito de verificación " +
                "(`dv`); si el NIT ya existe, actualiza sus datos (repetir la solicitud no crea otra). El cuerpo trae los " +
                "datos de la empresa (`business_name`, `email`, `municipality_id`, `type_document_identification_id`, " +
                "`type_organization_id`, `type_regime_id`, `type_liability_id`, …) y, si quiere, su ambiente " +
                "(`type_environment_id`; si no llega, habilitación). Al crearla, la respuesta trae en `token` la API key de " +
                "la empresa (alcances `documents:write`, `documents:read` y `config:write`): se muestra solo esta vez. En " +
                "una actualización `token` es nulo. Correo ya usado por otra empresa: 409 (`company.email_taken`).");

        // Ruta heredada del alta. La plataforma crea o actualiza cualquier empresa; una empresa con config:write solo
        // actualiza la suya, como cuando el PHP recibía de nuevo el alta de una empresa existente.
        legacy.MapPost("/config/{nit}/{dv?}", CreateOrUpdateCompany).RequireAuthorization(PlatformOrConfigWrite);

        canonical.MapPut("/{nit}/{dv}", UpdateCompany).RequireAuthorization(ApiScopes.ConfigWrite)
            .Produces<CompanySavedResponse>()
            .WithSummary("Actualizar los datos de la empresa")
            .WithDescription(
                "La empresa actualiza sus propios datos con el mismo cuerpo del alta. Los ambientes no cambian aquí (use " +
                "`PUT /api/v1/companies/{nit}/environment`). Correo ya usado por otra empresa: 409 (`company.email_taken`)." +
                OwnNit);

        canonical.MapPut("/{nit}/environment", SetEnvironment).RequireAuthorization(ApiScopes.ConfigWrite)
            .Produces<EnvironmentsResponse>()
            .WithSummary("Cambiar el ambiente de la DIAN")
            .WithDescription(
                "Pone cada familia de documentos en producción (1) o habilitación (2): `type_environment_id` (factura y " +
                "notas), `payroll_type_environment_id` (nómina), `eqdocs_type_environment_id` (documento equivalente), " +
                "`support_document_type_environment_id` (documento soporte) y `event_type_environment_id` (eventos). Las " +
                "familias que no llegan quedan como estaban. Exige el software configurado antes: si no, 422 " +
                "(`company.software_missing`)." + OwnNit);
        legacy.MapPut("/config/environment", SetEnvironment).RequireAuthorization(ApiScopes.ConfigWrite);

        canonical.MapPut("/{nit}/software", SetSoftware).RequireAuthorization(ApiScopes.ConfigWrite)
            .Produces<SoftwareSavedResponse>()
            .WithSummary("Registrar el software ante la DIAN")
            .WithDescription(
                "Guarda el identificador del software (`id`), su PIN de 5 dígitos (`pin`) y, si quiere, la URL del servicio " +
                "(`url`) y el `test_set_id` del set de pruebas, tal como los dio la DIAN al registrar el software. Otras " +
                "familias usan sus propios campos (`idpayroll`, `ideqdocs`, `idsupportdocument`…). El software queda en el " +
                "ambiente actual de cada familia. El PIN se guarda cifrado y nunca se devuelve." + OwnNit);
        legacy.MapPut("/config/software", SetSoftware).RequireAuthorization(ApiScopes.ConfigWrite);
        legacy.MapPut("/config/softwaresupportdocument", SetSoftware).RequireAuthorization(ApiScopes.ConfigWrite);

        canonical.MapPut("/{nit}/certificate", UploadCertificate).RequireAuthorization(ApiScopes.ConfigWrite)
            .Produces<CertificateSavedResponse>()
            .WithSummary("Subir el certificado de firma")
            .WithDescription(
                "Recibe el certificado digital de la empresa como PKCS#12 (archivo .p12 o .pfx con el certificado y su llave " +
                "privada) en Base64 (`certificate`) y su contraseña (`password`). Queda como el certificado activo con el que " +
                "se firman los documentos. Responde sus datos y los días que le quedan (`certificate_days_left`). Errores: " +
                "422 si la contraseña o el archivo no son válidos (`certificate.invalid`), si no trae llave privada RSA " +
                "(`certificate.no_private_key`) o si ya venció (`certificate.expired`). Ni el archivo ni la contraseña se " +
                "devuelven." + OwnNit);
        legacy.MapPut("/config/certificate", UploadCertificate).RequireAuthorization(ApiScopes.ConfigWrite);

        canonical.MapPut("/{nit}/resolution", UpsertResolution).RequireAuthorization(ApiScopes.ConfigWrite)
            .Produces<ResolutionSavedResponse>()
            .WithSummary("Crear o actualizar una resolución de numeración")
            .WithDescription(
                "Guarda una resolución de numeración autorizada por la DIAN: `type_document_id`, `prefix`, `resolution`, " +
                "`resolution_date`, `technical_key` (clave técnica; entra en el CUFE), el rango `from`-`to` y su vigencia " +
                "`date_from`-`date_to`. Se identifica por tipo de documento, número, prefijo y ambiente: si ya existe, se " +
                "actualiza. Queda en el ambiente actual de la familia de su tipo. Con `delete_all_type_resolutions` las " +
                "demás resoluciones del mismo tipo y ambiente quedan inactivas." + OwnNit);
        legacy.MapPut("/config/resolution", UpsertResolution).RequireAuthorization(ApiScopes.ConfigWrite);

        canonical.MapGet("/{nit}/certificates", ListCertificates).RequireAuthorization(ApiScopes.ConfigWrite)
            .Produces<CertificatesResponse>()
            .WithSummary("Listar los certificados de la empresa")
            .WithDescription("Lista los certificados que la empresa ha subido, solo con sus datos (titular, emisor, vigencia, " +
                "días que le quedan); nunca el archivo ni la contraseña." + OwnNit);
        legacy.MapGet("/certificates-listing/{nit?}", ListCertificates).RequireAuthorization(ApiScopes.ConfigWrite);

        canonical.MapGet("/{nit}/certificate/end-date", CertificateEndDate).RequireAuthorization(ApiScopes.ConfigWrite)
            .Produces<CertificateEndDateResponse>()
            .WithSummary("Consultar el vencimiento del certificado")
            .WithDescription("Responde la fecha de vencimiento del certificado activo (formato `dd/MM/yyyy`, hora de Colombia). " +
                "Sin certificado activo: 404 (`certificate.not_found`)." + OwnNit);
        legacy.MapPut("/certificate-end-date", CertificateEndDate).RequireAuthorization(ApiScopes.ConfigWrite);

        // Única consulta en vivo a la DIAN desde la API (rangos de numeración autorizados).
        canonical.MapGet("/{nit}/numbering-ranges", (string nit, [FromQuery(Name = "software_id")] string? softwareId,
            IHandler<GetNumberingRangeQuery, NumberingRangeResponse> handler, CancellationToken ct) =>
            NumberingRange(new GetNumberingRangeQuery(softwareId) { Nit = nit }, handler, ct)).RequireAuthorization(ApiScopes.ConfigWrite)
            .Produces<NumberingRangeResponse>()
            .WithSummary("Consultar los rangos de numeración en la DIAN")
            .WithDescription(
                "Pregunta en línea a la DIAN (servicio GetNumberingRange) qué rangos de numeración tiene autorizados la " +
                "empresa para el software `software_id`; sirve para copiar los datos de la resolución antes del paso a " +
                "producción. Es la única ruta que llama a la DIAN mientras espera. Exige un certificado activo y vigente " +
                "(404 `certificate.not_found`, 422 `certificate.expired`). Si la DIAN no responde: 503 " +
                "(`dian.unavailable`, reintente después de `Retry-After`); si rechaza la consulta: 422 (`dian.fault`)." +
                OwnNit);
        legacy.MapPost("/numbering-range", (GetNumberingRangeQuery body, IHandler<GetNumberingRangeQuery, NumberingRangeResponse> handler,
            CancellationToken ct) => NumberingRange(body with { Nit = null }, handler, ct)).RequireAuthorization(ApiScopes.ConfigWrite);
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

    private static async Task<IResult> NumberingRange(
        GetNumberingRangeQuery query, IHandler<GetNumberingRangeQuery, NumberingRangeResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(query, ct)).ToHttpResult();

    private static async Task<IResult> CertificateEndDate(
        string? nit, IHandler<GetCertificateEndDateQuery, CertificateEndDateResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetCertificateEndDateQuery(nit), ct)).ToHttpResult();
}
