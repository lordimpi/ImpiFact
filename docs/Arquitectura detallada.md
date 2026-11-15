# Arquitectura detallada

Especificación de implementación. Complementa la sección 6 del PRD (visión y diagramas) y el modelo de datos. Cuando algo dependa de la DIAN y no se pueda confirmar sin credenciales, se marca **\[verificar\]** y se valida en el prototipo de la fase 1.

## 1. Reglas de dependencia

```
ImpiFact.Domain          ← no depende de nada (ni de EF, ni de JSON, ni de DI)
ImpiFact.Application     ← Domain, Dian
ImpiFact.Dian            ← Domain (puro: UBL, CUFE, firma, SOAP; sin acceso a BD)
ImpiFact.Infrastructure  ← Application, Dian, Domain  (EF, Dapper, blobs, correo, PDF, secretos)
ImpiFact.Infrastructure.<Proveedor> ← Infrastructure (migraciones y SQL no portable)
ImpiFact.Api             ← Application, Infrastructure (composición)
ImpiFact.Worker          ← Application, Infrastructure (composición)
```

Reglas verificadas por pruebas de arquitectura (NetArchTest):

1. `Domain` no referencia ningún paquete NuGet salvo la BCL.
2. `Application` no referencia EF Core, Dapper, ASP.NET ni SDKs de nube; solo puertos (interfaces).
3. `Dian` no referencia `Application` ni `Infrastructure`: recibe datos del dominio y devuelve XML, bytes y resultados. Los puertos IDianClient, IUblBuilder, ICufeCalculator e IXmlSigner y sus tipos (DianContext, DianResponse, SigningCertificate) se definen en Dian, que también los implementa; Application los consume. Infrastructure solo aporta HttpClient con resiliencia y el almacén de certificados.
4. Ningún módulo lee tablas de otro: se comunican por casos de uso públicos o por eventos (sección 4.5).
5. Los controladores/endpoints no contienen lógica: mapean HTTP ↔ caso de uso.

## 2. Solución y paquetes

```
impifact/
├─ src/
│  ├─ ImpiFact.Domain/
│  │  ├─ Common/            Entity, AggregateRoot, ValueObject, DomainException, IDomainEvent
│  │  ├─ Catalog/           enums y constantes de códigos DIAN usados por reglas
│  │  ├─ Companies/         Company, Resolution, Certificate (metadatos), CompanyEnvironment
│  │  ├─ Documents/         Document (agregado), DocumentLine, DocumentTax, estado y transiciones
│  │  └─ Billing/           Plan, CompanyPlan, cuotas
│  ├─ ImpiFact.Application/
│  │  ├─ Abstractions/      IHandler, Result, Error, puertos de infraestructura (sección 5)
│  │  ├─ Behaviors/         decoradores: logging, validación, tenant, transacción
│  │  └─ Features/<Módulo>/<CasoDeUso>/   Command|Query, Handler, Validator, Mapping, Dto
│  ├─ ImpiFact.Dian/
│  │  ├─ Ubl/               generadores de XML por tipo de documento
│  │  ├─ Cufe/              CUFE, CUDE, CUNE, QR
│  │  ├─ Signing/           XAdES-EPES
│  │  ├─ Soap/              cliente SOAP con WS-Security
│  │  ├─ Zip/               empaquetado y AttachedDocument
│  │  └─ Rounding/          DianRounding (redondeo del anexo técnico)
│  ├─ ImpiFact.Infrastructure/
│  │  ├─ Persistence/       DbContext, configuraciones, UnitOfWork, interceptores, convenciones
│  │  ├─ Reads/             consultas Dapper por módulo
│  │  ├─ Storage/           IBlobStorage: Local, AzureBlob, S3, Gcs
│  │  ├─ Secrets/           ICertificateStore, ISecretProtector: KeyVault, AwsSecrets, GcpSecrets, LocalAesGcm
│  │  ├─ Email/             IEmailSender (MailKit, SMTP por empresa)
│  │  ├─ Pdf/               IPdfRenderer + plantillas
│  │  ├─ Outbox/            publicador y procesador
│  │  └─ Options/           GenericOptionsService<T> y clases de opciones
│  ├─ ImpiFact.Infrastructure.Postgres/ | .SqlServer/ | .MySql/
│  ├─ ImpiFact.Api/          Program.cs, endpoints, middleware, auth, OpenAPI
│  ├─ ImpiFact.Worker/       BackgroundServices
│  └─ ImpiFact.Migrator/     aplica migraciones y semillas
├─ tests/
│  ├─ ImpiFact.Domain.Tests/  ImpiFact.Application.Tests/  ImpiFact.Dian.Tests/
│  ├─ ImpiFact.Architecture.Tests/  ImpiFact.Integration.Tests/  ImpiFact.Golden.Tests/  ImpiFact.Contract.Tests/
├─ web/impifact-web/          Angular
├─ docs/                     PRD, arquitectura, modelo de datos, ADRs y reglas del PHP
└─ deploy/                  Dockerfiles, compose, IaC por nube
```

| Proyecto | Paquetes principales |
| --- | --- |
| Domain | ninguno |
| Application | `FluentValidation`, `Mapster`, `Scrutor`, `Microsoft.Extensions.*.Abstractions` |
| Dian | `System.Security.Cryptography.Xml`, `System.IO.Compression` (BCL) |
| Infrastructure | `Microsoft.EntityFrameworkCore`, `Dapper`, `Serilog`, `MailKit`, `QuestPDF` (candidato; ADR-04 abierto), `Azure.Storage.Blobs`, `AWSSDK.S3`, `Google.Cloud.Storage.V1`, `Azure.Security.KeyVault.*`, `AWSSDK.SecretsManager`, `Google.Cloud.SecretManager.V1`, `Microsoft.Extensions.Http.Resilience`, `Microsoft.AspNetCore.DataProtection` |
| Infrastructure.Postgres | `Npgsql.EntityFrameworkCore.PostgreSQL` |
| Infrastructure.SqlServer | `Microsoft.EntityFrameworkCore.SqlServer` |
| Infrastructure.MySql | proveedor definido en ADR-08 (Pomelo u Oracle) |
| Api | `Scalar.AspNetCore`, `Microsoft.AspNetCore.OpenApi`, `Asp.Versioning.Http`, `Microsoft.AspNetCore.Authentication.JwtBearer`, `Serilog.AspNetCore`, `OpenTelemetry.*` |
| Tests | `NUnit`, `Moq`, `FluentAssertions`, `Testcontainers.*`, `NetArchTest.Rules`, `Verify.NUnit` (golden) |

Versiones de paquetes centralizadas en `Directory.Packages.props`; `Directory.Build.props` fija `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true` y `LangVersion=latest`.

## 3. Módulos

Cada módulo es una carpeta en `Domain`, `Application/Features` y `Infrastructure`, sin proyecto propio (monolito modular simple). Su frontera se hace cumplir con la regla 4.

| Módulo | Responsabilidad | Casos de uso principales |
| --- | --- | --- |
| `Catalog` | Catálogos DIAN de solo lectura | `GetCatalog(tabla)`, `GetMunicipalitiesByFacturador` |
| `Identity` | Usuarios, roles, API keys, JWT | `Login`, `RefreshToken`, `CreateApiKey`, `RevokeApiKey`, `CreateUser` |
| `Company` | Empresa, ambientes, software, certificado, resoluciones, correo, logo | `CreateCompany`, `UpdateCompany`, `SetEnvironment`, `SetSoftware`, `UploadCertificate`, `UpsertResolution`, `SetMailSettings`, `DeleteCompany` |
| `Masters` | Clientes y productos | `UpsertCustomer`, `SearchCustomers`, `UpsertItem`, `SearchItems` |
| `Documents` | Emisión y consulta de documentos | `IssueInvoice`, `IssueCreditNote`, `IssueDebitNote`, `IssueSupportDocument`, `PreviewInvoice`, `GetDocument`, `ListDocuments`, `RegenerateAttachedDocument`, `RegeneratePdf`, `SendEmail`, `SendEvent`, `ProcessContingency` |
| `Billing` | Planes y consumo | `AssignPlan`, `GetPlanUsage`, `ConsumeQuota` |
| `Platform` | Outbox, auditoría, notificaciones, idempotencia | `AppendAudit`, `ListNotifications` |

Enterprise (nómina, POS, RIPS, extractor, app) entra después como módulos nuevos que reutilizan `Dian`, `Company` y `Platform` sin tocar los existentes.

## 4. Patrones

### 4.1 Casos de uso (sin MediatR)

```csharp
public interface IHandler<in TIn, TOut>
{
    Task<Result<TOut>> HandleAsync(TIn request, CancellationToken ct);
}

public readonly record struct Error(string Code, string Message, ErrorType Type);
public enum ErrorType { Validation, NotFound, Conflict, Unauthorized, Forbidden, DianRejected, Unavailable, Unexpected }

public sealed class Result<T>
{
    public bool IsSuccess { get; }
    public T Value { get; }                    // válido solo si IsSuccess
    public Error Error { get; }                // válido solo si !IsSuccess
    public static Result<T> Ok(T value);
    public static Result<T> Fail(Error error);
}
```

- Un handler por caso de uso, inyectado en el endpoint como `IHandler<TIn, TOut>`.
- Los decoradores (Scrutor `Decorate`) se aplican en este orden, de afuera hacia adentro: **Logging → Validation → Tenant → Transaction (solo comandos) → Handler**.
- `Validation` ejecuta todos los `IValidator<TIn>` y devuelve `ErrorType.Validation` con la lista de errores por campo.
- `Tenant` falla con `Unauthorized` si el caso de uso exige empresa y `ITenantContext.CompanyId` es nulo.
- `Transaction` abre una transacción del `UnitOfWork`, confirma si el resultado es éxito y revierte si no.
- Excepciones solo para lo inesperado; los flujos esperados (rechazo de la DIAN, resolución vencida, consecutivo fuera de rango) son `Result.Fail`.
- Las consultas son handlers de solo lectura que usan Dapper (`IReadConnectionFactory`); los comandos usan EF.

### 4.2 Mapeo y validación

- Mapster con `TypeAdapterConfig` por módulo (`IRegister`), mapeo explícito; nunca se exponen entidades.
- FluentValidation, un validador por entrada. Mensajes en español. El contrato JSON legado se valida con los mismos nombres de campo del Postman (anexo A).

### 4.3 Configuración (patrón Options)

Se reutiliza el enfoque de `GenericOptionsService<T>` de tu proyecto de referencia:

```csharp
public interface IOptionsService<T> where T : class { T Value { get; } }
builder.Services.AddValidatedOptions<DianOptions>(config, "Dian");   // ValidateDataAnnotations + ValidateOnStart
```

Clases de opciones: `DatabaseOptions`, `DianOptions`, `StorageOptions`, `SecretsOptions`, `JwtOptions`, `ApiKeyOptions`, `EmailOptions`, `PdfOptions`, `OutboxOptions`, `ResilienceOptions`, `CorsOptions`, `RateLimitOptions`. La configuración **por empresa** (software, correo, ambiente) vive en la BD, no en Options.

### 4.4 Persistencia

- `IUnitOfWork` (envoltorio del `DbContext`): `SaveChangesAsync`, `BeginTransactionAsync`.
- Repositorios solo donde aportan (`IDocumentRepository`, `ICompanyRepository`); el resto usa el `DbContext` desde `Infrastructure` detrás de interfaces de `Application`.
- Interceptores de `SaveChanges`: auditoría (`created_at`, `updated_at`), `row_version`, y publicación de eventos de dominio.
- Filtro global de tenant y de `deleted_at` en el `OnModelCreating`.
- `ITenantContext { Guid? CompanyId; Guid? UserId; ActorType Actor; }` se llena en el middleware desde la API key o el JWT.

### 4.5 Eventos

- **De dominio, en proceso:** `DocumentAccepted`, `DocumentRejected`, `CertificateExpiring`. Se despachan después de confirmar la transacción; los handlers suscritos (consumo de cuota, notificaciones, correo) no pueden hacer fallar la emisión.
- **Entre procesos, por outbox:** `BuildDocumentXml`, `SendDocumentToDian`, `PollDianStatus`, `SendEmail`, `RenderPdf`. Se escriben en `outbox_messages` en la misma transacción que el cambio de estado y los procesa el Worker.

## 5. Puertos

Los puertos de infraestructura viven en `Application/Abstractions`. Los de la DIAN (`IDianClient`, `IUblBuilder`, `ICufeCalculator`, `IXmlSigner`) se definen e implementan en `ImpiFact.Dian` (ADR-23). Aquí se listan juntos para tener el inventario completo.

```csharp
public interface IClock { DateTimeOffset UtcNow { get; } }

public interface IBlobStorage {
    Task<string> PutAsync(string key, Stream content, string contentType, CancellationToken ct);
    Task<Stream> OpenReadAsync(string key, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
    Uri CreateSignedUrl(string key, TimeSpan lifetime);
}

public interface ICertificateStore {
    Task<string> SaveAsync(Guid companyId, byte[] pfx, string password, CancellationToken ct);   // devuelve secret_ref
    Task<SigningCertificate> LoadAsync(string secretRef, CancellationToken ct);                // X509Certificate2 en memoria
    Task DeleteAsync(string secretRef, CancellationToken ct);
}

public interface ISecretProtector { string Protect(string plain); string Unprotect(string cipher); }  // PIN, SMTP, tokens

// --- Definidos e implementados en ImpiFact.Dian (ADR-23) ---
public interface IDianClient {
    Task<DianResponse> SendBillSyncAsync(DianContext ctx, string fileName, byte[] zip, CancellationToken ct);
    Task<DianResponse> SendTestSetAsync(DianContext ctx, string fileName, byte[] zip, string testSetId, CancellationToken ct);
    Task<DianResponse> GetStatusAsync(DianContext ctx, string trackId, CancellationToken ct);
    Task<DianResponse> GetStatusZipAsync(DianContext ctx, string trackId, CancellationToken ct);
    Task<DianResponse> SendEventAsync(DianContext ctx, string fileName, byte[] zip, CancellationToken ct);
    Task<DianResponse> GetNumberingRangeAsync(DianContext ctx, string nit, string softwareId, CancellationToken ct);
    Task<DianResponse> GetXmlByDocumentKeyAsync(DianContext ctx, string trackId, CancellationToken ct);
}

public interface IUblBuilder<TDoc> { string Build(TDoc document); }          // uno por tipo de documento
public interface ICufeCalculator { string Compute(CufeInput input); }
public interface IXmlSigner { string Sign(string xml, SigningCertificate cert, SigningProfile profile); }

public interface IPdfRenderer { Task<byte[]> RenderAsync(DocumentPdfModel model, int templateId, CancellationToken ct); }
public interface IEmailSender { Task SendAsync(MailSettings settings, EmailMessage message, CancellationToken ct); }
public interface INumberingSql { Task<long> TakeNextAsync(DbConnection c, DbTransaction t, Guid companyId, string scopeKey, CancellationToken ct); }
public interface ITenantContext { Guid? CompanyId { get; } Guid? UserId { get; } ActorType Actor { get; } }
public interface IOutbox { Task EnqueueAsync<T>(T message, DateTimeOffset? notBefore, CancellationToken ct); }
```

`DianContext` agrupa ambiente (URL), identificador y PIN del software, `testSetId` y el certificado ya cargado. El handler lo arma; `Dian` nunca consulta la BD.

## 6. Núcleo `ImpiFact.Dian`

### 6.1 UBL

- Un generador por tipo: `InvoiceUblBuilder` (venta, exportación, contingencia, AIU, mandato, transporte como variantes por `type_document_id`/`type_operation_id`), `CreditNoteUblBuilder`, `DebitNoteUblBuilder`, `SupportDocumentUblBuilder`, `SupportCreditNoteUblBuilder`, `ApplicationResponseBuilder` (eventos RADIAN), `AttachedDocumentBuilder`.
- Se construye con `XmlWriter`/`XDocument` y espacios de nombres fijos; las plantillas **no** son cadenas con interpolación.
- Entrada: un modelo de dominio ya validado y con importes `decimal` redondeados; salida: `string` XML.
- Hora de Colombia solo aquí (`TimeZoneInfo` `America/Bogota`, UTC-5 sin horario de verano).
- Cada builder tiene casos golden: JSON de entrada → XML esperado capturado del PHP.

### 6.2 CUFE, CUDE y CUNE

- Invoice: SHA-384 de la concatenación `NumFac + FecFac + HorFac + ValFac + CodImp1 + ValImp1 + CodImp2 + ValImp2 + CodImp3 + ValImp3 + ValTot + NitOFE + NumAdq + ClTec + TipoAmbiente`, con los importes a dos decimales y sin separadores **\[verificar contra el anexo técnico vigente y los golden tests\]**.
- Notas y soporte usan CUDE (clave de software en vez de clave técnica); nómina CUNE.
- El QR se arma con la URL de consulta de la DIAN (ambiente según el documento) y el CUFE.
- Salida en hex minúsculas, 96 caracteres.

Confirmado el 14-nov en el código de `torresoftware`: los valores entran como texto tal como quedaron en el XML, `IssueTime` incluye `-05:00`, un impuesto ausente se escribe `0.00` y las retenciones no entran. El detalle está en «Reglas del PHP», sección 2.1.

### 6.3 Firma

- XAdES-EPES sobre el XML, con `SignedXml` y extensiones propias (`SignedProperties`, `SigningCertificate`, `SignaturePolicyIdentifier` con la política de firma de la DIAN y su hash), digest SHA-256, RSA-SHA256, canonicalización C14N **\[verificar los parámetros exactos en la fase 1 con una factura real en habilitación\]**.
- `ICertificateStore.LoadAsync` devuelve el certificado en memoria; nunca se escribe a disco.
- Certificados con cifrado heredado (el PHP los «moderniza»): al cargar un `.pfx`, si .NET no puede abrirlo, se intenta convertir con una ruta controlada y se informa al usuario; ver riesgo en el PRD.

### 6.4 SOAP con WS-Security

- `Opción A (por defecto hasta el spike): cliente propio sobre HttpClient; opción B: cliente WCF con CustomBinding y un MessageInspector para la seguridad. E`l sobre SOAP 1.2 se construye a mano con WS-Addressing y un `wsse:Security` que incluye `Timestamp`, `BinarySecurityToken` (el certificado) y una firma `ds:Signature` sobre el `wsa:To` **\[verificar con la DIAN en la fase 1\]**.
- URLs por ambiente en `DianOptions`: habilitación `https://vpfe-hab.dian.gov.co/WcfDianCustomerServices.svc`, producción `https://vpfe.dian.gov.co/WcfDianCustomerServices.svc`.
- Operaciones: `SendBillSync`, `SendTestSetAsync`, `SendBillAsync`, `GetStatus`, `GetStatusZip`, `SendEventUpdateStatus`, `GetNumberingRange`, `GetXmlByDocumentKey`.
- Los archivos se envían como ZIP en base64 con el nombre de archivo que exige la DIAN por tipo de documento.
- Resiliencia (`Microsoft.Extensions.Http.Resilience`): tiempo de espera 60 s por intento, 3 reintentos con retroceso exponencial y jitter solo ante errores transitorios (timeout, 5xx, red); rechazo de negocio no se reintenta; *circuit breaker* tras 5 fallos en 30 s. Valores en `ResilienceOptions`.
- Cada llamada guarda una fila en `dian_submissions` y la solicitud/respuesta en `document_files`.

### 6.5 Redondeo

`DianRounding` centraliza el redondeo (`MidpointRounding.AwayFromZero`, 2 decimales en importes y precios, 6 en cantidades y 3 en retenciones, igual que el PHP («php-rules», sección 1)). Ningún builder redondea por su cuenta. Los casos límite viven en `ImpiFact.Golden.Tests`.

## 7. Flujo de emisión (`IssueInvoice`)

Handler síncrono corto + trabajo asíncrono en el Worker (ADR-02).

| Paso | Quién | Detalle |
| --- | --- | --- |
| 1 | Api | Autentica (API key/JWT), resuelve tenant, aplica `Idempotency-Key` si viene |
| 2 | Validación | FluentValidation del JSON; empresa activa; certificado vigente; resolución activa y en rango; cuota del plan disponible; totales validados con las reglas y tolerancias de la DIAN (`DianTotalsValidator`): se rechaza con 422 solo lo que la DIAN rechazaría |
| 3 | Handler (transacción 1, corta) | Toma consecutivo (`INumberingSql`), crea `Document` en `Received` con cliente + snapshot, líneas, impuestos y totales (los del cliente, ya validados en el paso 2), calcula el CUFE (sin E/S) y encola `BuildDocumentXml` en el outbox; confirma. El bloqueo del consecutivo dura solo esta transacción |
| 4 | Handler o Worker (transacción 2) | Tras confirmar (en la misma petición con `?wait=true`, o en el Worker al procesar `BuildDocumentXml`): genera UBL, firma, guarda el XML en `IBlobStorage` y `document_files` y pasa a `Queued`. Es idempotente: si el proceso cae entre 3 y 4, el mensaje `BuildDocumentXml` sigue en el outbox y el Worker reconstruye el XML desde el documento guardado, así una caída no deja huecos de numeración |
| 5 | Handler o Worker (transacción 2) | `IOutbox.EnqueueAsync(SendDocumentToDian)` en la misma transacción del paso 4 |
| 6 | Api | Responde de inmediato con `cufe`, número y estado `Received` (o `Queued` si el XML ya se generó en la petición); con `?wait=true` espera hasta N segundos el estado final (compatibilidad con integradores que esperan respuesta síncrona) |
| 7 | Worker | Toma el mensaje (`SKIP LOCKED`), arma `DianContext`, envía `SendBillSync` (producción) o `SendTestSetAsync` (habilitación), guarda `dian_submissions` |
| 8 | Worker | Según respuesta: `Accepted`/`AcceptedWithWarnings`/`Rejected`; si «En proceso» (98) o sin respuesta → `Unconfirmed` y programa `PollDianStatus` |
| 9 | Worker | Si aceptó: genera `AttachedDocument`, PDF, guarda archivos, encola `SendEmail`, consume cuota, publica `DocumentAccepted` |
| 10 | Worker | Regla 90 (documento ya existe): consulta el estado en la DIAN y reconcilia en vez de fallar |

Reglas:

- El consecutivo se consume en el paso 3; si el documento termina `Failed` queda como hueco registrado.
- Los pasos 7 a 10 son idempotentes: reintentar un mensaje no duplica archivos (`UQ document_id, kind, revision`) ni intentos (`UQ document_id, attempt`).
- El reconsultado de `Unconfirmed` se hace cada 1 minuto durante \~10 minutos y luego cada 15 minutos hasta 24 h; pasado ese plazo pasa a `Failed` con alerta.
- El JSON de respuesta mantiene los campos legados (`success`, `message`, `ResponseDian`, `cufe`, `urlinvoicexml`, `urlinvoicepdf`, …); el conjunto exacto se fija con los contract tests de la colección Postman.
- Notas crédito/débito: mismo flujo con `reference_document_id` obligatorio y validación de que el documento original esté `Accepted*` y pertenezca a la empresa.
- Vista previa (`preeliminar-view`): valida (paso 2), arma el documento en memoria en estado `Draft` y genera UBL y PDF sin tomar consecutivo ni persistir nada; devuelve el PDF.
- Contingencia: `invoice-contingency` emite sin enviar (`ContingencyPending`); `send_pendings` los encola.

## 8. Worker

Proceso aparte (`ImpiFact.Worker`) con la misma imagen base que el Api y otro punto de entrada. Se puede escalar horizontalmente: el *locking* de la cola evita que dos instancias tomen el mismo mensaje.

| Servicio | Intervalo | Qué hace |
| --- | --- | --- |
| `OutboxProcessor` | continuo (sondeo 1–5 s con espera adaptativa) | Toma mensajes `status = 0` con `next_attempt_at <= now` usando `SKIP LOCKED`/`READPAST`, los marca en proceso (`locked_until = now + 5 min`) y despacha al handler por `type`. Reintentos con retroceso: 10 s, 1 min, 5 min, 30 min, 2 h; al quinto fallo queda `Failed` y genera notificación. |
| `DianStatusPoller` | 1 min | Reconsulta documentos `Unconfirmed` (sección 7). |
| `ContingencySender` | 5 min | Encola `send_pendings` de documentos `ContingencyPending` cuando la DIAN vuelve a responder. |
| `CertificateExpiryWatcher` | diario | Crea notificaciones y correo a 30, 15 y 5 días del vencimiento. |
| `ResolutionRangeWatcher` | diario | Avisa cuando queda menos del 10 % del rango o faltan 30 días para `valid_to`. |
| `Housekeeping` | diario | Purga `outbox_messages` hechos con más de 30 días, `idempotency_keys` vencidas y `refresh_tokens` expirados; mueve `request`/`response` antiguos a almacenamiento frío si se configura. |

Cada mensaje del outbox lleva `company_id`; el Worker lo establece en `ITenantContext` antes de ejecutar el handler. Un fallo de un tenant nunca detiene el procesamiento de los demás.

## 9. Capa API

### 9.1 Pipeline (orden de middleware)

1. `ForwardedHeaders` (detrás de proxy/ingress).
2. `CorrelationId` (acepta `X-Correlation-Id` o genera uno; entra al contexto de Serilog).
3. `ExceptionHandler` → `ProblemDetails` (RFC 9457) más los campos legados `success` y `message`.
4. `HttpsRedirection`, `HSTS`, CORS con lista explícita.
5. `RateLimiter` (por API key, por IP y más estricto en login).
6. `Authentication` → `TenantResolution` → `Authorization`.
7. Endpoints.

### 9.2 Autenticación

- **Esquema de política** que decide por el encabezado `Authorization: Bearer <token>`: si el token empieza por `apk_` es API key; si no, JWT. Así el Postman legado (que usa Bearer) funciona sin cambios.
- **API key**: se busca por `key_hash` (SHA-256), se verifica `revoked_at`/`expires_at`, se actualiza `last_used_at` (sin bloquear la petición) y se crean los claims `company_id` y `scopes`.
- **JWT** del panel: vida 15 min, refresh de 14 días rotativo (`refresh_tokens`), claims `sub`, `role` y `company_id` activa; cambiar de empresa emite un token nuevo.
- **Plataforma**: llave con `company_id` nulo y alcance `platform:*` reemplaza `X-Admin-Token` para crear/borrar empresas, planes y revendedores.
- **Propiedad por NIT**: toda ruta con `{nit}` o con NIT en el cuerpo compara contra la empresa del token; si no coincide responde 403 sin revelar si el NIT existe.
- Políticas de autorización por alcance (`documents:write`, `documents:read`, `config:write`, `platform:*`) y por rol en el panel.

### 9.3 Endpoints

- Estilo *Minimal API* agrupado por módulo (`MapGroup`), un archivo por caso de uso o por grupo pequeño; cada endpoint solo valida la forma HTTP, llama al handler y traduce el `Result`.
- Rutas: `/api/v1/...` es la ruta canónica (la usa el panel). Las del contrato legado (`/api/ubl2.1/...`, `/api/information/...`, `/api/table/...`, etc., ver anexo A) se publican como alias del mismo handler para los contract tests.
- Los DTO de entrada replican los nombres JSON del contrato (`[JsonPropertyName]`) incluyendo los typos heredados.
- Documentación con `Microsoft.AspNetCore.OpenApi` y Scalar en `/scalar` (solo en desarrollo y staging, protegido en producción).

### 9.4 Mapeo de errores

| `ErrorType` | HTTP | Notas |
| --- | --- | --- |
| `Validation` | 422 | `errors` por campo; `success: false` |
| `NotFound` | 404 |  |
| `Conflict` | 409 | consecutivo en uso, documento duplicado |
| `Unauthorized` | 401 |  |
| `Forbidden` | 403 | NIT ajeno, alcance insuficiente |
| `DianRejected` | 422 | incluye el detalle de reglas rechazadas por la DIAN |
| `Unavailable` | 503 | DIAN o almacenamiento no disponibles; `Retry-After` |
| `Unexpected` | 500 | sin detalles internos; el `correlation_id` va en la respuesta |

El PHP respondía muchos errores con 200 y `success: false`. Los errores de validación, en cambio, salen como HTTP 500 con `success: false` y sin detalle por campo («php-rules», sección 3.1); ImpiFact responde 422. El contract test fija cuáles casos deben conservar el comportamiento del PHP por compatibilidad, **\[verificar\]** contra la colección.

### 9.5 Archivos y descargas

- Las rutas de descarga legadas (`download/{nit}/{archivo}`, `invoice/{nit}/{pdf}`, `receivedfile/...`) exigen autenticación y propiedad del NIT, o aceptan un token de URL firmada con vencimiento. Ya no existen rutas públicas adivinables.
- `config/certificate` (GET) **no se implementa**; se reemplaza por consulta de metadatos (`certificates-listing`).

## 10. Panel Angular (`web/impifact-web`)

- Angular (última versión estable), componentes *standalone*, `signals`, control flow nativo (`@if`, `@for`), `provideRouter` con rutas perezosas por `feature`.
- Cliente HTTP **generado** desde el OpenAPI del Api (`ng-openapi-gen` u OpenAPI Generator `typescript-angular`, con servicios sobre `HttpClient`); no se escriben interfaces de API a mano.
- Estructura: workspace con librerías `core`, `ui`, `api` y una librería por *feature* (`feature-auth`, `feature-documents`, …). El detalle está en «Arquitectura frontend» sección 2, que manda sobre esta sección.
- Interceptores: token JWT, renovación con refresh, manejo global de `ProblemDetails`, `X-Correlation-Id`.
- Guards por rol (`owner`, `seller`, `customer`, `reseller`, `platform_admin`) y por empresa activa.
- UI: componentes accesibles (WCAG AA), tablas con paginación por servidor, formularios reactivos tipados, `i18n` con español como idioma base.

Módulos del panel de la fase 5:

| Feature | Pantallas |
| --- | --- |
| `auth` | login, recuperar contraseña, selector de empresa |
| `companies` | alta de empresa, datos, ambientes, software, certificado, resoluciones, correo, logo, plantilla PDF |
| `production-wizard` | asistente por pasos (empresa → certificado → resolución → set de pruebas → producción por producto) |
| `documents` | listado con filtros, detalle, descarga XML/PDF, reenvío de correo, regenerar PDF, vista previa; eventos RADIAN al llegar la fase 7 |
| `masters` (clientes y productos) | CRUD con búsqueda |
| `api-keys` | crear (se muestra una vez), revocar, rotar |
| `plans` | plan activo, consumo, vencimiento |
| `platform` (reseller/admin) | empresas, planes, revendedores |
| `notifications` | campana, certificado por vencer, rangos, rechazos |

## 11. Configuración

`appsettings.json` solo trae valores no secretos. Los secretos entran por variables de entorno o por el proveedor de secretos de la nube.

```jsonc
{
  "Database":   { "Provider": "Postgres", "ConnectionString": "" },        // Postgres | SqlServer | MySql
  "Dian":       { "HabilitacionUrl": "https://vpfe-hab.dian.gov.co/WcfDianCustomerServices.svc",
                  "ProduccionUrl":   "https://vpfe.dian.gov.co/WcfDianCustomerServices.svc",
                  "TimeoutSeconds": 60 },
  "Storage":    { "Provider": "Local", "Local": { "Root": "/data" },    // Local | AzureBlob | S3 | Gcs
                  "AzureBlob": { "Container": "impifact" }, "S3": { "Bucket": "", "Region": "" }, "Gcs": { "Bucket": "" } },
  "Secrets":    { "Provider": "LocalAesGcm", "KeyVaultUri": "", "AwsRegion": "", "GcpProject": "" },
  "Jwt":        { "Issuer": "impifact", "Audience": "impifact-web", "AccessMinutes": 15, "RefreshDays": 14 },
  "ApiKey":     { "Prefix": "apk_" },
  "Outbox":     { "PollSeconds": 2, "LockMinutes": 5, "MaxAttempts": 5 },
  "Resilience": { "Retries": 3, "BreakerFailures": 5, "BreakerSeconds": 30 },
  "Cors":       { "Origins": [ "https://app.example.com" ] },
  "RateLimit":  { "PermitPerMinutePerKey": 120, "PermitPerMinutePerIp": 300, "LoginPerMinute": 10 },
  "Pdf":        { "DefaultTemplate": 2 },
  "Serilog":    { /* niveles y sinks */ }
}
```

- Secretos (cadena de conexión, llave de JWT, llave maestra local, credenciales SMTP por defecto) **nunca** van en `appsettings`; el arranque falla (`ValidateOnStart`) si falta alguno.
- Llave de JWT: asimétrica (RS256/ES256) con rotación; la pública se expone en `/.well-known/jwks.json` si se necesita.
- Archivos de ejemplo `appsettings.Development.json` y `.env.example` se versionan sin secretos.

## 12. Observabilidad

- **Logs**: Serilog con salida JSON a consola; enriquecedores `CorrelationId`, `CompanyId`, `UserId`, `RequestPath`. Se filtran encabezados `Authorization` y cualquier campo `*password*`, `*token*`, `*pin*`.
- **Trazas y métricas**: OpenTelemetry (ASP.NET, HttpClient, EF Core) exportando por OTLP; funciona con Azure Monitor, CloudWatch/X-Ray o Cloud Trace mediante el *collector*.
- **Métricas de negocio**: documentos emitidos por tipo y estado, latencia de la DIAN, tamaño de cola del outbox, reintentos, documentos `Unconfirmed`, días al vencimiento de certificados.
- **Health checks**: `/health/live` (proceso) y `/health/ready` (BD, almacenamiento, proveedor de secretos); la DIAN se reporta como degradado, no como caído.
- **Alertas sugeridas**: cola > 100 mensajes por más de 5 min, > 5 % de rechazos en 15 min, circuit breaker abierto, certificado a < 15 días.

## 13. Pruebas

| Proyecto | Qué verifica | Notas |
| --- | --- | --- |
| `Domain.Tests` | Reglas del agregado `Document`, transiciones, `Resolution.TakeNumber`, `DianRounding` | Rápidas, sin I/O |
| `Application.Tests` | Handlers con puertos simulados (Moq) | Caminos felices y de error |
| `Dian.Tests` | CUFE, firma, UBL contra fixtures | Sin red |
| `Golden.Tests` | JSON de entrada → XML y CUFE idénticos al PHP | `Verify`; fixtures en `tests/golden/<tipo>/<caso>/{input.json,expected.xml,expected.cufe}` |
| `Integration.Tests` | EF + Dapper + cola con Testcontainers en PostgreSQL (fase 2) y en los 3 motores desde la fase 6 | Matriz de proveedor en CI desde la fase 6 |
| `Contract.Tests` | Colección Postman (Newman) contra el Api levantado | Tokens propios; falla ante cambios de contrato |
| `Architecture.Tests` | Reglas de la sección 1, aislamiento de módulos, consultas Dapper con `company_id`, ausencia de secretos en logs | NetArchTest |
| Seguridad | Aislamiento entre empresas por entidad, API keys revocadas, propiedad por NIT | En `Integration.Tests` |

Procedimiento para generar fixtures golden: `tools/golden-capture` levanta el PHP en Docker (solo en `127.0.0.1`) y le envía cada JSON de entrada con `"query_uuid": true`. En ese modo el PHP arma, firma y calcula el CUFE/CUDE, pero responde antes de llamar a la DIAN; no sale tráfico hacia la DIAN. Guarda el XML firmado sin el elemento `ds:Signature`, el CUFE y la respuesta, y se versionan en el repo. Fecha, hora, consecutivo, clave técnica, software y empresa son fijos, así que el resultado es determinista (comprobado el 15-nov: dos corridas dan los mismos bytes). Instrucciones en `tools/golden-capture/README.md`.

Cobertura mínima exigida en CI: `Domain` y `Dian` 90 %, `Application` 80 %; los golden tests no se pueden desactivar.

## 14. Despliegue

- Imágenes: `Dockerfile.api`, `Dockerfile.worker`, `Dockerfile.web` (Nginx con el build de Angular), multi-etapa, usuario no root, sin secretos en capas.
- `docker-compose.yml` para desarrollo: Postgres, SQL Server, MySQL (perfiles), Azurite/MinIO para blobs, MailHog para correo, Api, Worker y web.
- CI (GitHub Actions): restaurar → compilar con `TreatWarningsAsErrors` → pruebas unitarias → integración (PostgreSQL; matriz de motores desde la fase 6) → golden → contract → análisis de vulnerabilidades → imágenes.
- Migraciones se aplican con `ImpiFact.Migrator` como paso previo al despliegue (job de Kubernetes, tarea ECS, job de Cloud Run o contenedor de inicio en Azure Container Apps).

| Necesidad | Azure | AWS | GCP |
| --- | --- | --- | --- |
| Cómputo | Container Apps / AKS | ECS Fargate / EKS | Cloud Run / GKE |
| Base de datos | Azure Database for PostgreSQL, Azure SQL, MySQL Flexible | RDS PostgreSQL/SQL Server/MySQL | Cloud SQL (los 3) |
| Blobs | Blob Storage | S3 | Cloud Storage |
| Secretos y certificados | Key Vault | Secrets Manager (+ KMS) | Secret Manager |
| Observabilidad | Application Insights / Monitor | CloudWatch / X-Ray | Cloud Logging / Trace |
| Identidad de carga de trabajo | Managed Identity | IAM role del task | Service account |

Las implementaciones de `IBlobStorage` y `ICertificateStore` usan la identidad de la carga de trabajo, sin llaves estáticas. Una instalación mínima (Docker en un servidor) usa Local + `LocalAesGcm` con la llave maestra montada como secreto.

## 15. Decisiones adicionales (ADR)

| ID | Decisión | Resolución |
| --- | --- | --- |
| ADR-09 | SOAP: cliente `WCF (System.ServiceModel) o sobre manual con HttpClient` | Spike en la fase 1b: cliente WCF (System.ServiceModel) frente a sobre SOAP manual; gana el que logre aceptación de la DIAN. Ambos van detrás de IDianClient |
| ADR-10 | Casos de uso: handlers propios con decoradores (Scrutor) | Aprobado (sin MediatR) |
| ADR-11 | Minimal API agrupada por módulo frente a controladores | Minimal API |
| ADR-12 | Autenticación dual con un solo encabezado `Bearer` y prefijo `apk_` | Aprobado, mantiene el contrato Postman |
| ADR-13 | Envío asíncrono con `?wait=true` opcional | Aprobado (refina ADR-02) |
| ADR-14 | Cliente Angular generado desde OpenAPI | Aprobado |

## 16. Reglas de desarrollo

Convenciones que sigue todo el código del repositorio. Los documentos de `docs/` mandan: si el código los contradice, gana el documento; si el documento está mal, se corrige el documento antes de desviarse. La configuración local de herramientas de desarrollo no se versiona (ver `.gitignore`).

**Reglas**

- .NET 10, C# con `Nullable` y `TreatWarningsAsErrors`.
- Clean Architecture: `Domain` sin dependencias, `Application` solo con puertos y `Dian` puro. Las pruebas de arquitectura deben pasar.
- Sin MediatR ni AutoMapper. Casos de uso con `IHandler<TIn, TOut>` y decoradores; mapeo con Mapster.
- Escrituras con EF Core y lecturas con Dapper. Toda consulta incluye `company_id`.
- Dinero con `decimal` y `DianRounding`; nunca `float` ni `double`.
- Ningún secreto en logs, `appsettings` ni base de datos en claro.
- Migraciones por proveedor; no se editan migraciones ya aplicadas.
- Una tarea es un PR pequeño con sus pruebas. No se avanza de fase sin cumplir su puerta de salida.

**Comandos**

- `dotnet build -warnaserror`
- `dotnet test --filter Category!=Integration`
- `dotnet test --filter Category=Integration` (requiere Docker)
- `dotnet ef migrations add <Nombre> -p src/ImpiFact.Infrastructure.Postgres -s src/ImpiFact.Api`

**Cuando falte información de la DIAN:** la tarea se marca como bloqueada, se deja un TODO con el identificador [verificar] del documento y se sigue con lo que no dependa de ella.

## 17. Backlog inicial por fase

Cada tarea es un PR con pruebas. Entre paréntesis, la puerta de salida de la fase (PRD sección 12).

**Fase 0 — Documentación (aprobar PRD)**

- Resolver los [verificar] que no dependan de la DIAN: hecho el 14-nov en `docs/php-rules.md` (razón social, `state_document_id` y validación de `number`).
- Extraer las semillas de catálogos a `Seed/*.json`.
- Extraer del anexo técnico de la DIAN las reglas de validación de totales y sus tolerancias (base de `DianTotalsValidator`), con un caso de prueba por regla.
- Capturar con el PHP casos golden de nota crédito y débito, y 10 de factura (venta, descuentos, impuestos, moneda extranjera, redondeo).

**Fase 1 — Prototipo DIAN** *(1a sin DIAN: tareas 1, 2 y 4 con un certificado de prueba propio. 1b con certificado y `testSetId` reales: tarea 3 y aceptación de la DIAN)*

1. `ImpiFact.Dian`: `InvoiceUblBuilder` + `CufeCalculator` + `DianRounding` con 10 casos golden en verde (no requiere DIAN).
2. `XmlSigner` XAdES-EPES con un certificado de prueba propio; validar la firma con una librería independiente.
3. `SoapClient` WS-Security con `SendTestSetAsync`/`GetStatus` contra habilitación (requiere credenciales).
4. Consola de prueba `ImpiFact.Prototype` (proyecto temporal en `tools/`, fuera de la solución final) que emite una factura de ejemplo extremo a extremo.

**Fase 2 — Base** *(pruebas de arquitectura y aislamiento en verde)*

1. Solución, proyectos, `Directory.*.props`, CI y `docker-compose`.
2. `Result`, `IHandler`, decoradores, `Error`, `IClock`, `ITenantContext`.
3. `DbContext`, convenciones `snake_case`, conversores (JSON, `DateTimeOffset`), interceptores, `UnitOfWork`.
4. Migración inicial en PostgreSQL con el modelo completo; semillas.
5. `Identity`: usuarios, roles, JWT, API keys, middleware de tenant.
6. `Company`: crear empresa, ambientes, software, resoluciones, certificado (`LocalAesGcm`).
7. Pruebas de arquitectura y de aislamiento multi-tenant.

**Fase 3 — Documentos** *(golden: XML y CUFE idénticos al PHP)*

1. `IssueInvoice` + `NumberingSequence` + persistencia + outbox.
2. Notas crédito y débito.
3. Consultas: listado, detalle, por CUFE, `information`, `total_documents`.

**Fase 4 — Operación** *(peticiones Postman de la primera entrega en verde)*

1. Worker: `OutboxProcessor`, `DianStatusPoller`, reintentos y reconciliación (regla 90).
2. `AttachedDocument`, PDF con plantillas, correo por empresa, almacenamiento (Local y una nube).
3. `regenerate-attached-document` y `regeneratepdf`.
4. Planes y consumo.

**Fase 7 — Paridad y salida** *(piloto en producción y colección Postman Community completa en verde)*

1. Piloto con una empresa real usando la primera entrega.
2. Variantes de factura: exportación, contingencia, AIU, mandato, transporte.
3. Documento soporte y su nota de ajuste.
4. Eventos RADIAN y lotes.

**Fases 5, 6 y 8** como en el PRD; se desglosan al cerrar la fase 4.

## 18. Definición de terminado (cualquier tarea)

1. Compila sin advertencias y pasa las pruebas relevantes.
2. Pruebas de arquitectura en verde.
3. Sin secretos, `TODO` sin dueño ni código muerto.
4. Documenta en `docs/` cualquier decisión que se aparte del PRD (nuevo ADR) en el mismo PR.
5. Si toca la BD: migración para PostgreSQL con su prueba de integración; desde la fase 6, para los tres proveedores en la matriz.

## 19. Adiciones tras la revisión crítica

### 19.1 Colas y caché (ADR-22)

- La cola es `outbox_messages` (sección 8). **No hay Redis, RabbitMQ ni Kafka en la v1.** `IOutbox` es la única puerta de entrada, así que un transporte externo se puede agregar después como un relevo que lee del outbox, sin tocar los casos de uso.
- Caché: `HybridCache` con nivel local en memoria. Se cachean catálogos (TTL largo, invalidados al aplicar migraciones de datos) y la búsqueda de API keys por hash (TTL de 60 s; una llave revocada deja de servir como máximo en ese tiempo, y la revocación también limpia la entrada local de la instancia que la ejecuta).
- Limitación de tasa: `RateLimiter` de ASP.NET en memoria por instancia. Con más de una instancia se mueve al *gateway* de la nube (API Management, API Gateway, Cloud Endpoints) o se usa Redis como almacén compartido; se decide cuando exista esa topología.
- No se necesitan bloqueos distribuidos: la exclusión sale de la base (`SKIP LOCKED`, `UPDATE` atómico del consecutivo).

### 19.2 Llaves de Data Protection

`ISecretProtector` usa ASP.NET Data Protection. Con más de una instancia (o tras reiniciar un contenedor) las llaves deben ser compartidas y persistentes, o los campos `*_cipher` quedan ilegibles.

- Tabla `data_protection_keys (id int identity PK, friendly_name varchar(200), xml text)` en el módulo `Platform` (`PersistKeysToDbContext`).
- Las llaves se cifran con un certificado o con la llave del proveedor de secretos (`ProtectKeysWithAzureKeyVault`, KMS o equivalente); en instalación mínima, con la llave maestra local.
- Rotación automática cada 90 días; las llaves antiguas se conservan para poder descifrar.
- Prueba de integración: cifrar con una instancia y descifrar con otra que comparta la base.

### 19.3 Copias de seguridad y recuperación

- Base de datos: recuperación a un punto en el tiempo (PITR) activada en el servicio administrado; objetivos propuestos RPO 15 min y RTO 4 h.
- Blobs: versionado y *soft delete* activados; los XML aceptados por la DIAN no se borran.
- Certificados: en el proveedor de secretos con protección contra purga; copia cifrada y fuera de línea que guarda el propietario de la empresa.
- Prueba de restauración documentada cada trimestre.

### 19.4 Retención y datos personales

- Conservación de XML, PDF y respuestas de la DIAN durante el plazo legal de conservación de documentos electrónicos **\[verificar el plazo vigente con la DIAN o un asesor\]**; política de ciclo de vida: almacenamiento frío después de 1 año, sin borrado automático dentro del plazo.
- Datos personales de clientes finales (nombre, identificación, dirección, correo): tratamiento conforme a la Ley 1581 de 2012 **\[verificar con un asesor\]**; aviso de privacidad y política de tratamiento en el panel, registro de quién accede (`audit_logs`) y logs sin datos personales.
- Un cliente sin documentos emitidos se puede suprimir; con documentos, se anonimiza lo no exigido por el XML ya emitido.

### 19.5 Cambios al backlog

- **Fase 0, tareas nuevas:** `docs/php-rules.md` (inventario de reglas por tipo de documento); levantar el PHP de referencia con su `Dockerfile` y correr `composer install` dentro del contenedor (la máquina de desarrollo no tiene PHP ni Composer) y, si falla, definir el plan B de fixtures; consulta regulatoria sobre proveedor tecnológico; decidir el nombre del producto y los namespaces.
- **Fase 1 dividida:** **1a** sin DIAN (UBL, CUFE, firma con certificado de prueba, golden tests) y **1b** con certificado y `testSetId` reales (SOAP y aceptación). La 1a se hace antes de la fase 2; la 1b cuando haya credenciales.
- **Fase 2:** agregar `data_protection_keys`, `HybridCache` y la prueba de llaves compartidas.
- **Contract tests:** en la fase 4 solo se ejecutan las peticiones de la primera entrega (carpetas 01 a 06, 09 y 10, sin documento soporte, contingencia ni lotes); en la fase 7 se suman la 07, la 08 y esas peticiones. Nunca se ejecutan `payroll`, `payroll-adjust-note`, `eqdoc`, `send-email-employee` ni las carpetas 11 a 14.
- **Criterios de aceptación del modelo:** las pruebas de los tres motores se exigen en la fase 6; en la fase 2 solo PostgreSQL.

### 19.6 ADR nuevos

| ID | Decisión | Resolución |
| --- | --- | --- |
| ADR-22 | Redis y brokers | Sin ellos en la v1; outbox en BD; se reabre con los casos de la sección 19.1 |
| ADR-23 | Puertos de la DIAN | Definidos e implementados en `ImpiFact.Dian`; `Application` los consume |
| ADR-24 | Transacciones de emisión | Transacción 1 corta (consecutivo, documento, CUFE y mensaje `BuildDocumentXml`) y transacción 2 (XML, firma, estado `Queued` y mensaje `SendDocumentToDian`) |
| ADR-25 | Llaves de Data Protection | Persistidas en BD y protegidas por el proveedor de secretos |
