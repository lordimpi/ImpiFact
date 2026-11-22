using ImpiFact.Api.Endpoints;
using ImpiFact.Api.Http;
using ImpiFact.Api.OpenApi;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Companies;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Features.Companies.Activation;
using ImpiFact.Application.Features.Companies.CreateCompany;
using ImpiFact.Application.Features.Companies.ListCertificates;
using ImpiFact.Application.Features.Companies.ListPlatformCompanies;
using ImpiFact.Application.Features.Companies.Logo;
using ImpiFact.Application.Features.Companies.MailSettings;
using ImpiFact.Application.Features.Companies;
using ImpiFact.Application.Features.Companies.NumberingRange;
using ImpiFact.Application.Features.Companies.Reads;
using ImpiFact.Application.Features.Companies.SetEnvironment;
using ImpiFact.Application.Features.Companies.SetResolutionNextNumber;
using ImpiFact.Application.Features.Companies.SetSoftware;
using ImpiFact.Application.Features.Companies.UpdateCompany;
using ImpiFact.Application.Features.Companies.UploadCertificate;
using ImpiFact.Application.Features.Companies.UpsertResolution;
using Microsoft.AspNetCore.Mvc;

namespace ImpiFact.Api.Companies;

/// <summary>
/// Endpoints del módulo Company (Arquitectura detallada, sección 9.3), bajo <c>/api/v1/companies/{nit}/...</c>. El NIT
/// debe ser el de la empresa de la credencial (si no, 403 sin revelar si existe). Las rutas heredadas del PHP
/// (<c>/api/ubl2.1/config/...</c>) se borraron en la fase 7a (T9b2). Cada endpoint solo arma la solicitud, llama al
/// caso de uso y traduce el resultado.
/// </summary>
internal sealed class CompanyEndpoints : IEndpointModule
{
    /// <summary>Recordatorio que llevan las rutas que reciben el NIT de la empresa.</summary>
    private const string OwnNit =
        " El `{nit}` debe ser el de la empresa de la credencial; otro NIT responde 403 (`company.forbidden`).";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var canonical = endpoints.MapGroup("/api/v1/companies").WithTags(ApiTags.Companies);

        // Alta o actualización por NIT: operación de plataforma.
        canonical.MapPost("/{nit}/{dv}", CreateCompany).RequireAuthorization(ApiScopes.Platform)
            .Produces<CompanySavedResponse>()
            .WithRequestExamples(ExampleBodies.Company)
            .WithOperationId("createCompany")
            .WithSummary("Crear o actualizar una empresa")
            .WithDescription(
                "Crea la empresa emisora con su NIT (número de identificación tributaria) y su dígito de verificación " +
                "(`dv`); si el NIT ya existe, actualiza sus datos (repetir la solicitud no crea otra). El cuerpo trae los " +
                "datos de la empresa (`business_name`, `email`, `municipality_id`, `type_document_identification_id`, " +
                "`type_organization_id`, `type_regime_id`, `type_liability_id`, …) y, si quiere, su ambiente " +
                "(`type_environment_id`; si no llega, habilitación). Al crearla, la respuesta trae en `token` la API key de " +
                "la empresa (alcances `documents:write`, `documents:read` y `config:write`): se muestra solo esta vez. En " +
                "una actualización `token` es nulo. Correo ya usado por otra empresa: 409 (`company.email_taken`).");

        canonical.MapPut("/{nit}/{dv}", UpdateCompany).RequireAuthorization(ApiScopes.ConfigWrite)
            .Produces<CompanySavedResponse>()
            .WithRequestExamples(ExampleBodies.Company)
            .WithOperationId("updateCompany")
            .WithSummary("Actualizar los datos de la empresa")
            .WithDescription(
                "La empresa actualiza sus propios datos con el mismo cuerpo del alta. Los ambientes no cambian aquí (use " +
                "`PUT /api/v1/companies/{nit}/environment`). Correo ya usado por otra empresa: 409 (`company.email_taken`)." +
                OwnNit);

        canonical.MapPut("/{nit}/environment", SetEnvironment).RequireAuthorization(ApiScopes.ConfigWrite)
            .Produces<EnvironmentsResponse>()
            .WithRequestExamples(ExampleBodies.Environment)
            .WithOperationId("setCompanyEnvironment")
            .WithSummary("Cambiar el ambiente de la DIAN")
            .WithDescription(
                "Pone cada familia de documentos en producción (1) o habilitación (2): `type_environment_id` (factura y " +
                "notas), `payroll_type_environment_id` (nómina), `eqdocs_type_environment_id` (documento equivalente), " +
                "`support_document_type_environment_id` (documento soporte) y `event_type_environment_id` (eventos). Las " +
                "familias que no llegan quedan como estaban. Exige el software configurado antes: si no, 422 " +
                "(`company.software_missing`)." + OwnNit);

        canonical.MapPut("/{nit}/software", SetSoftware).RequireAuthorization(ApiScopes.ConfigWrite)
            .Produces<SoftwareSavedResponse>()
            .WithRequestExamples([.. ExampleBodies.Software, .. ExampleBodies.SupportDocumentSoftware])
            .WithOperationId("setCompanySoftware")
            .WithSummary("Registrar el software ante la DIAN")
            .WithDescription(
                "Guarda el identificador del software (`id`), su PIN de 5 dígitos (`pin`) y, si quiere, la URL del servicio " +
                "(`url`) y el `test_set_id` del set de pruebas, tal como los dio la DIAN al registrar el software. Otras " +
                "familias usan sus propios campos (`idpayroll`, `ideqdocs`, `idsupportdocument`…). El software queda en el " +
                "ambiente actual de cada familia. El del documento soporte (`idsupportdocument` y `pinsupportdocument`, o " +
                "`identifier_support_document` y `pin_support_document`) es el que firman y envían el documento soporte y su " +
                "nota de ajuste; es la misma operación que `PUT /api/ubl2.1/config/softwaresupportdocument` del PHP. El PIN " +
                "se guarda cifrado y nunca se devuelve." + OwnNit);

        canonical.MapPut("/{nit}/certificate", UploadCertificate).RequireAuthorization(ApiScopes.ConfigWrite)
            .Produces<CertificateSavedResponse>()
            .WithRequestExamples(ExampleBodies.Certificate)
            .WithOperationId("uploadCompanyCertificate")
            .WithSummary("Subir el certificado de firma")
            .WithDescription(
                "Recibe el certificado digital de la empresa como PKCS#12 (archivo .p12 o .pfx con el certificado y su llave " +
                "privada) en Base64 (`certificate`) y su contraseña (`password`). Queda como el certificado activo con el que " +
                "se firman los documentos. Responde sus datos y los días que le quedan (`certificate_days_left`). Errores: " +
                "422 si la contraseña o el archivo no son válidos (`certificate.invalid`), si no trae llave privada RSA " +
                "(`certificate.no_private_key`) o si ya venció (`certificate.expired`). Ni el archivo ni la contraseña se " +
                "devuelven." + OwnNit);

        canonical.MapPut("/{nit}/resolution", UpsertResolution).RequireAuthorization(ApiScopes.ConfigWrite)
            .Produces<ResolutionSavedResponse>()
            .WithRequestExamples(ExampleBodies.Resolution)
            .WithOperationId("saveCompanyResolution")
            .WithSummary("Crear o actualizar una resolución de numeración")
            .WithDescription(
                "Guarda una resolución de numeración autorizada por la DIAN: `type_document_id`, `prefix`, `resolution`, " +
                "`resolution_date`, `technical_key` (clave técnica; entra en el CUFE), el rango `from`-`to` y su vigencia " +
                "`date_from`-`date_to`. Se identifica por tipo de documento, número, prefijo y ambiente: si ya existe, se " +
                "actualiza. Queda en el ambiente actual de la familia de su tipo. Con `delete_all_type_resolutions` las " +
                "demás resoluciones del mismo tipo y ambiente quedan inactivas." + OwnNit);

        canonical.MapGet("/{nit}/certificates", ListCertificates).RequireAuthorization(ApiScopes.ConfigWrite)
            .Produces<CertificatesResponse>()
            .WithOperationId("listCompanyCertificates")
            .WithSummary("Listar los certificados de la empresa")
            .WithDescription("Lista los certificados que la empresa ha subido, solo con sus datos (titular, emisor, vigencia, " +
                "días que le quedan); nunca el archivo ni la contraseña." + OwnNit);

        canonical.MapGet("/{nit}/certificate/end-date", CertificateEndDate).RequireAuthorization(ApiScopes.ConfigWrite)
            .Produces<CertificateEndDateResponse>()
            .WithOperationId("getCertificateEndDate")
            .WithSummary("Consultar el vencimiento del certificado")
            .WithDescription("Responde la fecha de vencimiento del certificado activo (formato `dd/MM/yyyy`, hora de Colombia). " +
                "Sin certificado activo: 404 (`certificate.not_found`)." + OwnNit);

        // Lecturas de la configuración para el panel: lo mismo que guardan las escrituras, sin ningún secreto.
        canonical.MapGet("/{nit}", (string nit, IHandler<GetCompanyQuery, CompanyResponse> handler, CancellationToken ct) =>
            Read(new GetCompanyQuery(nit), handler, ct)).RequireAuthorization(ApiScopes.ConfigWrite)
            .Produces<CompanyResponse>()
            .WithOperationId("getCompany")
            .WithSummary("Ver los datos de la empresa")
            .WithDescription(
                "Devuelve los datos de la empresa con los mismos nombres que recibe `PUT /api/v1/companies/{nit}/{dv}` " +
                "(`business_name`, `email`, `phone`, `address`, `merchant_registration`, `municipality_id`, " +
                "`type_liability_ids`, …), así el panel llena el formulario y puede enviar el mismo cuerpo para actualizar. " +
                "Trae además `identification_number`, `dv`, `environments` (el ambiente de cada familia) e `is_active`, " +
                "que la actualización no cambia." + OwnNit);

        canonical.MapGet("/{nit}/environments", (string nit, IHandler<GetCompanyEnvironmentsQuery, CompanyEnvironmentsResponse> handler,
            CancellationToken ct) => Read(new GetCompanyEnvironmentsQuery(nit), handler, ct)).RequireAuthorization(ApiScopes.ConfigWrite)
            .Produces<CompanyEnvironmentsResponse>()
            .WithOperationId("getCompanyEnvironments")
            .WithSummary("Ver el ambiente de la DIAN de cada familia")
            .WithDescription(
                "Lista en `environments` cada familia de documentos (`document_kind`: 1 factura y notas, 2 nómina, 3 documento " +
                "equivalente, 4 documento soporte, 5 eventos) con su ambiente (`type_environment_id`: 1 producción, 2 " +
                "habilitación). Se cambian con `PUT /api/v1/companies/{nit}/environment`." + OwnNit);

        canonical.MapGet("/{nit}/software", (string nit, IHandler<GetCompanySoftwareQuery, CompanySoftwareResponse> handler,
            CancellationToken ct) => Read(new GetCompanySoftwareQuery(nit), handler, ct)).RequireAuthorization(ApiScopes.ConfigWrite)
            .Produces<CompanySoftwareResponse>()
            .WithOperationId("getCompanySoftware")
            .WithSummary("Ver el software registrado ante la DIAN")
            .WithDescription(
                "Lista en `software` el software de cada familia y ambiente: `identifier` (el id del software en la DIAN), " +
                "`url`, `test_set_id` y `has_pin` (si hay PIN guardado). El PIN nunca se devuelve: para cambiarlo se envía de " +
                "nuevo con `PUT /api/v1/companies/{nit}/software`. Sin software configurado, la lista llega vacía." + OwnNit);

        canonical.MapGet("/{nit}/resolutions", (string nit, IHandler<ListCompanyResolutionsQuery, CompanyResolutionsResponse> handler,
            CancellationToken ct) => Read(new ListCompanyResolutionsQuery(nit), handler, ct)).RequireAuthorization(ApiScopes.ConfigWrite)
            .Produces<CompanyResolutionsResponse>()
            .WithOperationId("listCompanyResolutions")
            .WithSummary("Listar las resoluciones de numeración")
            .WithDescription(
                "Lista en `resolutions` todas las resoluciones de la empresa, activas e inactivas y de los dos ambientes: " +
                "`kind` (`invoice`, `credit_note` o `debit_note`), `type_document_id`, `type_environment_id`, `prefix`, " +
                "`resolution`, `resolution_date`, el rango `from`-`to`, la vigencia `date_from`-`date_to`, `is_active`, " +
                "`next_number` (el número que tomará el próximo documento), `remaining` (los números que quedan) y " +
                "`days_left` (días hasta el fin de la vigencia, en el calendario de Colombia). De la clave técnica solo dice " +
                "si existe (`has_technical_key`); nunca la devuelve." + OwnNit);

        canonical.MapPut("/{nit}/resolutions/{id:guid}/next-number", SetResolutionNextNumber).RequireAuthorization(ApiScopes.ConfigWrite)
            .Produces<ResolutionNextNumberResponse>()
            .WithRequestExamples(ExampleBodies.ResolutionNextNumber)
            .WithOperationId("setResolutionNextNumber")
            .WithSummary("Fijar el próximo número de una resolución")
            .WithDescription(
                "Fija en `next_number` el número que tomará el próximo documento emitido con la resolución `{id}` (el `id` " +
                "que devuelve `GET /api/v1/companies/{nit}/resolutions`). Se usa si la empresa viene de otro sistema de " +
                "facturación y debe seguir desde su último número, o si ya usó números de este rango ante la DIAN (por " +
                "ejemplo, un set de pruebas anterior con la misma resolución de habilitación, que la DIAN rechaza con la " +
                "regla 90). El número debe estar dentro del rango `from`-`to` de la resolución y no puede ser menor que el " +
                "próximo número actual, porque bajarlo podría repetir un número ya enviado a la DIAN: si no cumple, responde " +
                "422 con el motivo en `errors.next_number`. Una emisión al mismo tiempo nunca recibe un número repetido. " +
                "Resolución inexistente o de otra empresa: 404 (`resolution.not_found`). Responde el `next_number` que quedó." +
                OwnNit);

        canonical.MapGet("/{nit}/setup-status", (string nit, IHandler<GetCompanySetupStatusQuery, CompanySetupStatusResponse> handler,
            CancellationToken ct) => Read(new GetCompanySetupStatusQuery(nit), handler, ct)).RequireAuthorization(ApiScopes.ConfigWrite)
            .Produces<CompanySetupStatusResponse>()
            .WithOperationId("getCompanySetupStatus")
            .WithSummary("Ver qué falta para el paso a producción")
            .WithDescription(
                "Responde lo que revisa el asistente de paso a producción, con lo que ya está guardado (no pregunta a la " +
                "DIAN): `company_complete` (datos obligatorios llenos), `certificate_valid` (certificado activo con al menos un " +
                "día de vigencia), `software_configured` y `test_set_id_configured` (software de factura con su set de pruebas " +
                "en el ambiente actual de la factura), `test_set_accepted` (verdadero si la DIAN validó un documento del set, " +
                "falso si respondió sin validar ninguno, nulo si no hay respuestas guardadas), `environments` (el ambiente de " +
                "cada familia) y `resolutions` (para factura, nota crédito y nota débito, `valid` si hay una resolución activa " +
                "en el ambiente actual, vigente hoy y con números por usar; la de factura además con clave técnica) y " +
                "`support_document` (la familia del documento soporte: `type_environment_id`, su ambiente actual; " +
                "`software_configured`, software de documento soporte en ese ambiente; y `resolutions` del documento soporte, con " +
                "número y vigencia, y de su nota de ajuste)." + OwnNit);

        // Logo del PDF: multipart con el campo «logo». Sin protección antifalsificación de formularios: la credencial
        // viaja en el encabezado Authorization, que un formulario de otro sitio no puede poner.
        canonical.MapPut("/{nit}/logo", SaveLogo).RequireAuthorization(ApiScopes.ConfigWrite)
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(LogoRequestLimit))
            .Produces<CompanyLogoResponse>()
            .WithOperationId("saveCompanyLogo")
            .WithSummary("Subir el logo de la empresa")
            .WithDescription(
                "Recibe el logo como `multipart/form-data` en el campo `logo`: una imagen PNG o JPEG de hasta 512 KiB, " +
                "reconocida por su contenido (el tipo que declare el archivo no cuenta; SVG, GIF y otros formatos no se " +
                "aceptan). Reemplaza el logo anterior y responde `content_type` y `size_bytes`. Desde ese momento va en el " +
                "encabezado del PDF de los documentos que se generen y de la vista previa. Archivo que falta, vacío, más " +
                "grande o de otro formato: 422 con `errors.logo`." + OwnNit);
        canonical.MapGet("/{nit}/logo", GetLogo).RequireAuthorization(ApiScopes.DocumentsRead)
            .Produces(StatusCodes.Status200OK, contentType: "image/png", additionalContentTypes: ["image/jpeg"])
            .WithOperationId("getCompanyLogo")
            .WithSummary("Ver el logo de la empresa")
            .WithDescription(
                "Devuelve la imagen del logo (`image/png` o `image/jpeg`) con `Cache-Control: private, no-cache`. Sin logo: " +
                "404 (`company.logo_not_found`). Pide `documents:read`." + OwnNit);
        canonical.MapDelete("/{nit}/logo", DeleteLogo).RequireAuthorization(ApiScopes.ConfigWrite)
            .Produces(StatusCodes.Status204NoContent)
            .WithOperationId("deleteCompanyLogo")
            .WithSummary("Quitar el logo de la empresa")
            .WithDescription(
                "Borra el logo y responde 204 sin cuerpo, también si la empresa no tenía. Los PDF que se generen después " +
                "salen sin logo; los ya generados no cambian." + OwnNit);

        // Servidor de correo propio (RF-01 «correo»; el GET emailconfig del PHP): la contraseña solo se escribe.
        canonical.MapGet("/{nit}/mail-settings", (string nit, IHandler<GetCompanyMailSettingsQuery, CompanyMailSettingsResponse> handler,
            CancellationToken ct) => Read(new GetCompanyMailSettingsQuery(nit), handler, ct)).RequireAuthorization(ApiScopes.ConfigWrite)
            .Produces<CompanyMailSettingsResponse>()
            .WithOperationId("getCompanyMailSettings")
            .WithSummary("Ver el servidor de correo de la empresa")
            .WithDescription(
                "Responde el servidor de correo (SMTP) propio de la empresa: `host`, `port`, `username`, `encryption`, " +
                "`from_address`, `from_name`, `use_platform_default` y `has_password` (si hay contraseña guardada; la " +
                "contraseña nunca se devuelve). `in_use` dice por dónde sale el correo de los documentos: `company` si el " +
                "servidor propio está completo (servidor, puerto, usuario, contraseña y cifrado) y no se eligió el de la " +
                "plataforma, o `platform`. Sin servidor propio, los datos llegan nulos y `in_use` es `platform`. Los datos " +
                "del servidor de la plataforma no se muestran." + OwnNit);
        canonical.MapPut("/{nit}/mail-settings", SaveMailSettings).RequireAuthorization(ApiScopes.ConfigWrite)
            .Produces<CompanyMailSettingsResponse>()
            .WithRequestExamples(ExampleBodies.MailSettings)
            .WithOperationId("saveCompanyMailSettings")
            .WithSummary("Guardar el servidor de correo de la empresa")
            .WithDescription(
                "Crea o reemplaza el servidor de correo propio con que salen los correos de los documentos de la empresa: " +
                "`host` (nombre o dirección IP, sin protocolo), `port` (1 a 65535), `username`, `password`, `encryption` " +
                "(`ssl`, `tls`, `starttls` o `none`), y si quiere `from_address` y `from_name` (sin ellos sale con el " +
                "remitente de la plataforma) y `use_platform_default` (verdadero: lo guarda pero envía por la plataforma). " +
                "La contraseña es obligatoria la primera vez y cada vez que cambian `host`, `port`, `username` o " +
                "`encryption` (sin ella: 422 en `errors.password` y no se guarda nada), porque la guardada solo se usa con " +
                "el servidor para el que se dio; si solo cambian el remitente o `use_platform_default`, sin `password` queda " +
                "la guardada. Se guarda cifrada y " +
                "nunca se devuelve ni se registra. Datos inválidos: 422 con el motivo en `errors.<campo>`. Responde lo mismo " +
                "que `GET .../mail-settings`." + OwnNit);
        canonical.MapDelete("/{nit}/mail-settings", DeleteMailSettings).RequireAuthorization(ApiScopes.ConfigWrite)
            .Produces(StatusCodes.Status204NoContent)
            .WithOperationId("deleteCompanyMailSettings")
            .WithSummary("Quitar el servidor de correo de la empresa")
            .WithDescription(
                "Borra el servidor de correo propio, contraseña incluida, y responde 204 sin cuerpo, también si la empresa no " +
                "tenía. Desde ese momento los correos salen por el servidor de la plataforma." + OwnNit);

        // Todas las empresas, para la pantalla de la plataforma: operación de plataforma, fuera del grupo de la empresa.
        endpoints.MapGet("/api/v1/platform/companies", (
                [FromQuery(Name = "search")] string? search,
                [FromQuery(Name = "cursor")] string? cursor,
                [FromQuery(Name = "limit")] int? limit,
                IHandler<ListPlatformCompaniesQuery, PlatformCompanyListResponse> handler,
                CancellationToken ct) => Read(new ListPlatformCompaniesQuery(search, cursor, limit), handler, ct))
            .WithTags(ApiTags.Companies)
            .RequireAuthorization(ApiScopes.Platform)
            .Produces<PlatformCompanyListResponse>()
            .WithOperationId("listPlatformCompanies")
            .WithSummary("Listar las empresas de la plataforma")
            .WithDescription(
                "Devuelve en `items` las empresas de la plataforma ordenadas por razón social (sin distinguir mayúsculas): " +
                "`id`, `name`, `nit`, `dv`, `email`, `is_active` y el plan vigente (`plan_id` y `plan_name`, nulos si no tiene " +
                "plan). `search` busca el texto dentro del NIT o de la razón social, sin distinguir mayúsculas (hasta 100 " +
                "caracteres). Pagina por cursor: `limit` (20 por defecto, máximo 100) y `cursor` con el `next_cursor` de la " +
                "página anterior (nulo en la última). Pide `platform:*` sin empresa.");

        // Desactivar y reactivar: reemplazan el borrado del PHP (delete/company/{nit}/{dv}); ImpiFact nunca borra una empresa.
        endpoints.MapPost("/api/v1/platform/companies/{nit}/deactivate", (
                string nit, IHandler<DeactivateCompanyCommand, CompanyActivationResponse> handler, CancellationToken ct) =>
                Read(new DeactivateCompanyCommand(nit), handler, ct))
            .WithTags(ApiTags.Companies)
            .RequireAuthorization(ApiScopes.Platform)
            .Produces<CompanyActivationResponse>()
            .WithOperationId("deactivatePlatformCompany")
            .WithSummary("Desactivar una empresa")
            .WithDescription(
                "Deja inactiva la empresa del NIT (`is_active` falso) en una sola transacción: revoca para siempre sus API " +
                "keys y cierra las sesiones del panel de sus usuarios (deben volver a iniciar sesión; la empresa les aparece " +
                "deshabilitada y no pueden elegirla). Desde ese momento no emite nada: facturas, notas, documento soporte, " +
                "eventos RADIAN, lotes y contingencia responden 403 (`company.inactive`) antes de numerar o llamar a la DIAN. " +
                "Los documentos y archivos guardados se conservan y la plataforma sigue viendo la empresa, su plan y sus " +
                "usuarios; el Worker sigue " +
                "consultando en la DIAN el estado de lo ya enviado, pero no envía nada nuevo mientras esté inactiva. Nunca " +
                "borra la empresa. Responde la empresa con `changed`, `revoked_api_keys` y `signed_out_users`; si ya estaba " +
                "inactiva responde 200 con `changed` falso y no hace nada. NIT inexistente: 404 (`company.not_found`). " +
                "Solo el administrador de la plataforma (`platform:*` sin empresa); otra credencial: 403.");
        endpoints.MapPost("/api/v1/platform/companies/{nit}/reactivate", (
                string nit, IHandler<ReactivateCompanyCommand, CompanyActivationResponse> handler, CancellationToken ct) =>
                Read(new ReactivateCompanyCommand(nit), handler, ct))
            .WithTags(ApiTags.Companies)
            .RequireAuthorization(ApiScopes.Platform)
            .Produces<CompanyActivationResponse>()
            .WithOperationId("reactivatePlatformCompany")
            .WithSummary("Reactivar una empresa")
            .WithDescription(
                "Vuelve a dejar activa la empresa del NIT: sus usuarios pueden elegirla de nuevo y vuelve a emitir. No " +
                "restaura nada más: las API keys revocadas al desactivarla siguen revocadas (un dueño crea llaves nuevas) y " +
                "las sesiones cerradas siguen cerradas. Si ya estaba activa responde 200 con `changed` falso y no hace nada. " +
                "NIT inexistente: 404 (`company.not_found`). Solo el administrador de la plataforma (`platform:*` sin " +
                "empresa); otra credencial: 403.");

        // Única consulta en vivo a la DIAN desde la API (rangos de numeración autorizados).
        canonical.MapGet("/{nit}/numbering-ranges", (string nit, [FromQuery(Name = "software_id")] string? softwareId,
            IHandler<GetNumberingRangeQuery, NumberingRangeResponse> handler, CancellationToken ct) =>
            NumberingRange(new GetNumberingRangeQuery(softwareId) { Nit = nit }, handler, ct)).RequireAuthorization(ApiScopes.ConfigWrite)
            .Produces<NumberingRangeResponse>()
            .WithOperationId("getNumberingRanges")
            .WithSummary("Consultar los rangos de numeración en la DIAN")
            .WithDescription(
                "Pregunta en línea a la DIAN (servicio GetNumberingRange) qué rangos de numeración tiene autorizados la " +
                "empresa para el software `software_id`; sirve para copiar los datos de la resolución antes del paso a " +
                "producción. Es la única ruta que llama a la DIAN mientras espera. Exige un certificado activo y vigente " +
                "(404 `certificate.not_found`, 422 `certificate.expired`). Si la DIAN no responde: 503 " +
                "(`dian.unavailable`, reintente después de `Retry-After`); si rechaza la consulta: 422 (`dian.fault`)." +
                OwnNit);
    }

    private static async Task<IResult> CreateCompany(
        string nit, string dv, CreateCompanyCommand body, IHandler<CreateCompanyCommand, CompanySavedResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(body with { Nit = nit, Dv = dv }, ct)).ToHttpResult();

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

    private static async Task<IResult> SetResolutionNextNumber(
        string nit, Guid id, SetResolutionNextNumberCommand body, IHandler<SetResolutionNextNumberCommand, ResolutionNextNumberResponse> handler,
        CancellationToken ct) =>
        (await handler.HandleAsync(body with { Nit = nit, ResolutionId = id }, ct)).ToHttpResult();

    private static async Task<IResult> ListCertificates(
        string? nit, IHandler<ListCertificatesQuery, CertificatesResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new ListCertificatesQuery(nit), ct)).ToHttpResult();

    private static async Task<IResult> Read<TQuery, TResponse>(TQuery query, IHandler<TQuery, TResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(query, ct)).ToHttpResult();

    private static async Task<IResult> NumberingRange(
        GetNumberingRangeQuery query, IHandler<GetNumberingRangeQuery, NumberingRangeResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(query, ct)).ToHttpResult();

    /// <summary>
    /// Tope del cuerpo de la subida del logo: el archivo (512 KiB) más el sobre del formulario. Lo que pase de ahí se corta
    /// antes de leerlo completo.
    /// </summary>
    private const long LogoRequestLimit = 1024 * 1024;

    private static async Task<IResult> SaveLogo(
        string nit, [FromForm(Name = "logo")] IFormFile? logo, IHandler<SaveCompanyLogoCommand, CompanyLogoResponse> handler, CancellationToken ct)
    {
        byte[]? content = null;
        // Un archivo más grande que el tope no se copia entero: basta un byte de más para que la validación lo rechace.
        if (logo is not null)
        {
            await using var stream = logo.OpenReadStream();
            using var buffer = new MemoryStream();
            await CopyAtMostAsync(stream, buffer, CompanyLogoFormat.MaxBytes + 1, ct);
            content = buffer.ToArray();
        }

        return (await handler.HandleAsync(new SaveCompanyLogoCommand(nit, content), ct)).ToHttpResult();
    }

    private static async Task<IResult> GetLogo(string nit, HttpContext context, IHandler<GetCompanyLogoQuery, CompanyLogoImage> handler, CancellationToken ct)
    {
        var result = await handler.HandleAsync(new GetCompanyLogoQuery(nit), ct);
        if (!result.IsSuccess)
        {
            return result.ToHttpResult();
        }

        // El logo puede cambiar en cualquier momento: el navegador lo guarda pero pregunta cada vez.
        context.Response.Headers.CacheControl = "private, no-cache";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        return TypedResults.File(result.Value.Content, result.Value.ContentType);
    }

    private static async Task<IResult> DeleteLogo(string nit, IHandler<DeleteCompanyLogoCommand, bool> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new DeleteCompanyLogoCommand(nit), ct)).ToHttpResult(_ => TypedResults.NoContent());

    private static async Task<IResult> SaveMailSettings(
        string nit, SaveCompanyMailSettingsCommand body, IHandler<SaveCompanyMailSettingsCommand, CompanyMailSettingsResponse> handler,
        CancellationToken ct) =>
        (await handler.HandleAsync(body with { Nit = nit }, ct)).ToHttpResult();

    private static async Task<IResult> DeleteMailSettings(
        string nit, IHandler<DeleteCompanyMailSettingsCommand, bool> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new DeleteCompanyMailSettingsCommand(nit), ct)).ToHttpResult(_ => TypedResults.NoContent());

    private static async Task CopyAtMostAsync(Stream source, Stream destination, int maxBytes, CancellationToken ct)
    {
        var buffer = new byte[81920];
        var left = maxBytes;
        int read;
        while (left > 0 && (read = await source.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, left)), ct)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
            left -= read;
        }
    }

    private static async Task<IResult> CertificateEndDate(
        string? nit, IHandler<GetCertificateEndDateQuery, CertificateEndDateResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetCertificateEndDateQuery(nit), ct)).ToHttpResult();
}
