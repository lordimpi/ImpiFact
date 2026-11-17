using ImpiFact.Api.Documents;
using ImpiFact.Api.Http;
using ImpiFact.Application.Abstractions.Identity;

namespace ImpiFact.Api.OpenApi;

/// <summary>
/// Introducción del documento OpenAPI (<c>info.description</c>, en Markdown), la primera página que ve en Scalar quien
/// integra ImpiFact: qué es, el flujo de punta a punta, los ambientes de la DIAN (Dirección de Impuestos y Aduanas
/// Nacionales), las credenciales y sus alcances, la forma de los errores y la idempotencia.
/// </summary>
internal static class OpenApiIntroduction
{
    public static string Markdown { get; } = $$"""
        ImpiFact emite documentos electrónicos ante la DIAN (Dirección de Impuestos y Aduanas Nacionales, la autoridad
        tributaria de Colombia): facturas, notas crédito y notas débito. Arma y firma el XML UBL (el formato XML que
        exige la DIAN), lo envía a la DIAN, guarda su respuesta, genera el PDF y lo envía por correo al comprador.

        Aquí solo se documentan las rutas `/api/v1`.

        ## Pruebe en este orden

        Cada ruta trae ejemplos listos para enviar (elíjalos en el selector de ejemplos del cuerpo). La empresa de
        ejemplo es el NIT `{{OpenApiExamples.Nit}}` con dígito de verificación `{{OpenApiExamples.Dv}}`.

        1. **Consiga una credencial.** Desde la carpeta `backend/`, una llave de plataforma:
           `dotnet run --project src/ImpiFact.Migrator -- create-platform-key <nombre>`. O, más rápido, una empresa de
           prueba ya configurada: `dotnet run --project src/ImpiFact.Migrator -- seed-demo`, que muestra la llave de esa
           empresa; con ella siga directo al paso 5.
        2. **Péguela en la sección de autenticación** de esta página (sin la palabra Bearer).
        3. **Cree la empresa** con la llave de plataforma: `POST /api/v1/companies/{nit}/{dv}` con su ejemplo. Copie el
           `token` de la respuesta (la llave de la empresa) y cámbielo en la sección de autenticación: los pasos
           siguientes usan esa llave.
        4. **Configure la empresa** con la llave de la empresa: `PUT /api/v1/companies/{nit}/software` (con el `id` y el
           `pin` que le dio la DIAN), `PUT .../certificate` (su archivo .p12 en Base64) y `PUT .../resolution` (los tres
           ejemplos: la resolución de habilitación SETP para facturas y las numeraciones NC y ND para las notas).
        5. **Emita una factura** con la llave de la empresa: `POST /api/v1/documents/invoices` con el ejemplo «Factura
           simple». Copie el `document.id` de la respuesta.
        6. **Consulte su estado** con la llave de la empresa: `GET /api/v1/documents/{id}`. Pasa de `Queued` a
           `Accepted` cuando el Worker la envía y la DIAN la acepta.

        ## Flujo de integración

        1. **Crear la empresa** con una llave de plataforma: `POST /api/v1/companies/{nit}/{dv}`. La respuesta trae en
           `token` la API key de la empresa. Se muestra una sola vez: guárdela en un lugar seguro.
        2. **Configurar la empresa** con su propia llave: software ante la DIAN (`PUT /api/v1/companies/{nit}/software`),
           certificado de firma (`PUT .../certificate`) y resolución de numeración (`PUT .../resolution`). Si la empresa
           tiene un plan de documentos, la plataforma se lo asigna con `PUT /api/v1/companies/{nit}/plan`.
        3. **Emitir** una factura o una nota: `POST /api/v1/documents/invoices`, `.../credit-notes` o `.../debit-notes`.
           La respuesta es inmediata: el documento queda firmado, con su CUFE o CUDE (código único de la factura o de la
           nota) y en cola (`Queued`), todavía sin respuesta de la DIAN.
        4. **Envío a la DIAN**: el Worker (el proceso de ImpiFact que trabaja en segundo plano) toma el documento de la
           cola, lo envía y guarda la respuesta. Si la DIAN no responde o contesta «en proceso», el documento queda
           `Unconfirmed` y el Worker vuelve a consultar su estado más tarde.
        5. **Consultar el estado** cuando lo necesite: `GET /api/v1/documents/{id}` o
           `GET /api/v1/documents/by-cufe/{cufe}/dian-status`. Estados: `Queued` (en cola), `Sent` (enviado, sin
           respuesta), `Accepted` o `AcceptedWithWarnings` (aceptado), `Rejected` (rechazado por la DIAN),
           `Unconfirmed` (sin confirmar, se vuelve a consultar) y `Failed` (error técnico tras agotar los reintentos).
        6. **Después de la aceptación** el Worker arma el AttachedDocument (el contenedor electrónico que se entrega al
           comprador), genera el PDF y, si la solicitud lo pidió, envía el correo. Los archivos se descargan con
           `GET /api/v1/documents/{id}/files/{kind}`.

        ## Ambientes de la DIAN

        Cada familia de documentos de la empresa trabaja en un ambiente: **habilitación** (código 2, pruebas sin
        validez fiscal; es el ambiente con el que se crea la empresa, salvo que la solicitud diga otro) o
        **producción** (código 1, documentos con validez fiscal). Se cambia con `PUT /api/v1/companies/{nit}/environment`.
        Las rutas `.../test-set/{testSetId}` envían el set de pruebas que la DIAN pide en habilitación para autorizar el
        software; esos documentos no cuentan en el plan.

        ## Autenticación

        Toda ruta, salvo el inicio de sesión y la renovación de la sesión, pide el encabezado
        `Authorization: Bearer <credencial>`. En esta página, escríbala una vez en la sección de autenticación.

        - **Llave de plataforma** (`apk_...`, sin empresa, alcance `{{ApiScopes.Platform}}`): la crea el operador de
          ImpiFact con `ImpiFact.Migrator create-platform-key <nombre>`. Sirve para crear empresas y administrar planes.
        - **Llave de empresa** (`apk_...`): la devuelve la creación de la empresa en `token`, con los alcances
          `{{ApiScopes.DocumentsWrite}}`, `{{ApiScopes.DocumentsRead}}` y `{{ApiScopes.ConfigWrite}}`. Se pueden crear
          otras con `POST /api/v1/api-keys`.
        - **JWT de un usuario del panel** (JSON Web Token, credencial firmada de vida corta): lo entrega
          `POST /api/v1/auth/login` con correo y contraseña, y se renueva con `POST /api/v1/auth/refresh`.

        Alcances (lo que una credencial puede hacer):

        | Alcance | Permite |
        | --- | --- |
        | `{{ApiScopes.DocumentsWrite}}` | Emitir facturas y notas, regenerar el PDF o el AttachedDocument y reenviar el correo. |
        | `{{ApiScopes.DocumentsRead}}` | Consultar documentos y su estado en la DIAN, descargar sus archivos y ver el plan vigente. |
        | `{{ApiScopes.ConfigWrite}}` | Cambiar la configuración de la empresa (datos, ambiente, software, certificado, resoluciones) y administrar sus API keys. |
        | `{{ApiScopes.Platform}}` | Administrar la plataforma: crear empresas y planes y asignarlos. Solo lo tienen las llaves sin empresa. |

        En las rutas que llevan `{nit}`, el NIT (número de identificación tributaria) debe ser el de la empresa de la
        credencial; otro NIT responde 403.

        ## Errores

        Los errores llegan como `application/problem+json` (RFC 9457, el formato estándar de errores en JSON):

        ```json
        {
          "title": "Datos no procesables",
          "status": 422,
          "detail": "La solicitud tiene datos inválidos.",
          "code": "validation.failed",
          "message": "La solicitud tiene datos inválidos.",
          "errors": { "nit": ["El NIT es obligatorio."] },
          "success": false,
          "correlation_id": "0HN8…"
        }
        ```

        - `code`: código estable para tomar decisiones en su programa (por ejemplo `plan.limit_reached`).
        - `message`: texto en español para mostrar a la persona.
        - `errors`: errores por campo; solo en validaciones y rechazos de la DIAN.
        - `{{LegacyProblemFields.CorrelationId}}`: identificador de la solicitud; inclúyalo al pedir soporte. También
          llega en el encabezado `{{CorrelationIdMiddleware.HeaderName}}`.

        Códigos HTTP: 401 falta la credencial o no es válida, 403 sin permiso, 404 no existe, 409 conflicto, 422 datos no
        válidos o rechazo de la DIAN, 429 demasiadas solicitudes y 503 servicio no disponible (reintente después de los
        segundos que indica `Retry-After`).

        ## Idempotencia

        Las rutas de emisión aceptan el encabezado opcional `{{DocumentIssueEndpoints.IdempotencyKeyHeader}}` (hasta 100
        caracteres). Si repite la solicitud con la misma llave y el mismo cuerpo dentro de 24 horas, recibe el documento
        ya emitido y no se emite otro; la misma llave con otro cuerpo responde 409 (`idempotency.key_reused`). Enviar el
        mismo `number` con el mismo cuerpo también responde con el documento existente.
        """;
}
