# Arquitectura detallada

Especificación de implementación. Complementa la sección 6 del PRD (visión y diagramas) y el modelo de datos. Cuando algo dependa de la DIAN y no se pueda confirmar sin credenciales, se marca **\[verificar\]** y se valida en el prototipo de la fase 1.

## 1. Reglas de dependencia

```
ImpiFact.Domain          ← no depende de nada (ni de EF, ni de JSON, ni de DI)
ImpiFact.Application     ← Domain, Dian
ImpiFact.Dian            ← nada (puro: UBL, CUFE, firma, SOAP; sin acceso a BD; ADR-26)
ImpiFact.Infrastructure  ← Application, Dian, Domain  (EF, Dapper, blobs, correo, PDF, secretos)
ImpiFact.Infrastructure.<Proveedor> ← Infrastructure (migraciones, SQL no portable, dialecto SQL e índices parciales)
ImpiFact.Api             ← Application, Infrastructure, Infrastructure.<Proveedor> (composición)
ImpiFact.Worker          ← Application, Infrastructure, Infrastructure.<Proveedor> (composición)
ImpiFact.Migrator        ← Application, Infrastructure, Infrastructure.<Proveedor> (aplica migraciones y semillas)
```

Reglas verificadas por pruebas de arquitectura (NetArchTest):

1. `Domain` no referencia ningún paquete NuGet salvo la BCL.
2. `Application` no referencia EF Core, Dapper, ASP.NET ni SDKs de nube; solo puertos (interfaces).
3. `Dian` no referencia `Application` ni `Infrastructure`: recibe datos del dominio y devuelve XML, bytes y resultados. Los puertos IDianClient, IUblBuilder, ICufeCalculator e IXmlSigner y sus tipos (DianContext, DianResponse, SigningCertificate) se definen en Dian, que también los implementa; Application los consume. Infrastructure solo aporta HttpClient con resiliencia y el almacén de certificados.
4. Ningún módulo lee tablas de otro: se comunican por casos de uso públicos o por eventos (sección 4.5).
5. Los controladores/endpoints no contienen lógica: mapean HTTP ↔ caso de uso.

## 2. Solución y paquetes

```
ImpiFact/
├─ backend/                   solución .NET: ImpiFact.slnx, Directory.Build.props, Directory.Packages.props, global.json
│  ├─ src/
│  │  ├─ ImpiFact.Domain/
│  │  │  ├─ Common/            Entity, AggregateRoot, ValueObject, DomainException, IDomainEvent
│  │  │  ├─ Catalog/           enums y constantes de códigos DIAN usados por reglas
│  │  │  ├─ Companies/         Company, Resolution, Certificate (metadatos), CompanyEnvironment
│  │  │  ├─ Documents/         Document (agregado), DocumentLine, DocumentTax, estado y transiciones
│  │  │  └─ Billing/           Plan, CompanyPlan, cuotas
│  │  ├─ ImpiFact.Application/
│  │  │  ├─ Abstractions/      IHandler, Result, Error, puertos de infraestructura (sección 5)
│  │  │  ├─ Behaviors/         decoradores: logging, validación, tenant, transacción
│  │  │  └─ Features/<Módulo>/<CasoDeUso>/   Command|Query, Handler, Validator, Mapping, Dto
│  │  ├─ ImpiFact.Dian/
│  │  │  ├─ Ubl/               generadores de XML por tipo de documento
│  │  │  ├─ Cufe/              CUFE, CUDE, CUNE, QR
│  │  │  ├─ Signing/           XAdES-EPES
│  │  │  ├─ Soap/              cliente SOAP con WS-Security
│  │  │  ├─ Zip/               empaquetado y AttachedDocument
│  │  │  └─ Rounding/          DianRounding (redondeo del anexo técnico)
│  │  ├─ ImpiFact.Infrastructure/
│  │  │  ├─ Persistence/       DbContext, configuraciones, UnitOfWork, interceptores, convenciones
│  │  │  ├─ Reads/             consultas Dapper por módulo
│  │  │  ├─ Storage/           IBlobStorage: Local, AzureBlob, S3, Gcs
│  │  │  ├─ Protection/        ICertificateStore, ISecretProtector: KeyVault, AwsSecrets, GcpSecrets, LocalAesGcm
│  │  │  ├─ Email/             IEmailSender (MailKit, SMTP por empresa)
│  │  │  ├─ Pdf/               IPdfRenderer con Gotenberg, plantillas HTML (Handlebars, recursos incrustados) y QR (QRCoder)
│  │  │  ├─ Platform/Outbox/   cola (toma con SKIP LOCKED) y procesador del Worker; el publicador es Platform/EfOutbox
│  │  │  └─ Options/           GenericOptionsService<T> y clases de opciones
│  │  ├─ ImpiFact.Infrastructure.Postgres/ | .SqlServer/ | .MySql/
│  │  ├─ ImpiFact.Api/          Program.cs, endpoints, middleware, auth, OpenAPI
│  │  ├─ ImpiFact.Worker/       BackgroundServices
│  │  └─ ImpiFact.Migrator/     aplica migraciones y semillas
│  ├─ tests/
│  │  ├─ ImpiFact.Domain.Tests/  ImpiFact.Application.Tests/  ImpiFact.Dian.Tests/
│  │  ├─ ImpiFact.Architecture.Tests/  ImpiFact.Integration.Tests/  ImpiFact.Golden.Tests/  ImpiFact.Contract.Tests/
│  └─ tools/                  golden-capture, signature-check, ImpiFact.Prototype (temporal)
├─ web/                       panel Angular (fase 5; workspace de Angular CLI)
├─ mobile/                    app móvil (Enterprise, fase 8; se crea cuando llegue)
├─ docs/                      PRD, arquitectura, modelo de datos, ADRs y reglas del PHP
└─ deploy/                    Dockerfiles, compose, IaC por nube
```

Organización del repositorio (15-nov, ADR-28): monorepo con una carpeta por aplicación (`backend/`, `web/`, `mobile/`), cada una con sus propias herramientas y su CI filtrado por ruta. Los comandos de .NET se ejecutan desde `backend/`. Si la app móvil se hace con Ionic y Capacitor (Angular), vive dentro del workspace de `web/` como otro proyecto y `mobile/` no se crea.

| Proyecto | Paquetes principales |
| --- | --- |
| Domain | ninguno |
| Application | `FluentValidation`, `Mapster`, `Scrutor`, `Microsoft.Extensions.*.Abstractions` |
| Dian | `System.Security.Cryptography.Xml`, `System.IO.Compression` (BCL) |
| Infrastructure | `Microsoft.EntityFrameworkCore`, `Dapper`, `Serilog` (con `Serilog.Extensions.Logging`, `Serilog.Formatting.Compact`, `Serilog.Sinks.Console` y `Serilog.Sinks.OpenTelemetry`), `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`, `OpenTelemetry.Instrumentation.Http`, `Microsoft.Extensions.Diagnostics.HealthChecks`, `MailKit`, `Handlebars.Net` y `QRCoder` (PDF; el HTML lo convierte Gotenberg, un contenedor aparte: ADR-04), `Azure.Storage.Blobs`, `AWSSDK.S3`, `Google.Cloud.Storage.V1`, `Azure.Security.KeyVault.*`, `AWSSDK.SecretsManager`, `Google.Cloud.SecretManager.V1`, `Microsoft.Extensions.Http.Resilience`, `Microsoft.AspNetCore.DataProtection` |
| Infrastructure.Postgres | `Npgsql.EntityFrameworkCore.PostgreSQL` |
| Infrastructure.SqlServer | `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.Data.SqlClient`, `OpenTelemetry.Instrumentation.SqlClient` (trazas y métricas de SqlClient, estable) |
| Infrastructure.MySql | `Microting.EntityFrameworkCore.MySql` (fork de Pomelo, MIT, sobre `MySqlConnector`; ADR-08) |
| Api | `Scalar.AspNetCore`, `Microsoft.AspNetCore.OpenApi`, `Asp.Versioning.Http`, `Microsoft.AspNetCore.Authentication.JwtBearer`, `OpenTelemetry.Instrumentation.AspNetCore` |
| Tests | `NUnit`, `Moq`, `Testcontainers.*`, `NetArchTest.Rules` (sin FluentAssertions ni Verify: ADR-27) |

Versiones de paquetes centralizadas en `Directory.Packages.props`; `Directory.Build.props` fija `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true` y `LangVersion=latest`.

## 3. Módulos

Cada módulo es una carpeta en `Domain`, `Application/Features` y `Infrastructure`, sin proyecto propio (monolito modular simple). Su frontera se hace cumplir con la regla 4.

| Módulo | Responsabilidad | Casos de uso principales |
| --- | --- | --- |
| `Catalog` | Catálogos DIAN de solo lectura | `GetCatalog(tabla)`, `GetMunicipalitiesByFacturador` |
| `Identity` | Usuarios, roles, API keys, JWT | `Login`, `RefreshToken`, `CreateApiKey`, `RevokeApiKey`, `CreateUser` |
| `Company` | Empresa, ambientes, software, certificado, resoluciones, correo, logo | `CreateCompany`, `UpdateCompany`, `SetEnvironment`, `SetSoftware`, `UploadCertificate`, `UpsertResolution`, `SetMailSettings`, `DeleteCompany` |
| `Masters` | Clientes y productos | `ListCustomers`, `GetCustomer`, `CreateCustomer`, `UpdateCustomer`, `DeactivateCustomer`, `LookupAcquirer` (GetAcquirer en vivo), `ListProducts`, `GetProduct`, `CreateProduct`, `UpdateProduct`, `DeactivateProduct` |
| `Documents` | Emisión y consulta de documentos | `IssueInvoice`, `IssueCreditNote`, `IssueDebitNote`, `IssueSupportDocument`, `PreviewInvoice`, `PreviewCreditNote`, `PreviewDebitNote`, `GetDocument`, `ListDocuments`, `RegenerateAttachedDocument`, `RegeneratePdf`, `SendEmail`, `SendEvent`, `ProcessContingency` |
| `Billing` | Planes y consumo | `StorePlan`, `AssignPlan`, `QueryPlans`, `QueryPlanCompanies`, `GetCurrentPlan` (consumo restante) y el puerto `IPlanGate` (revisar la cuota antes de emitir y contar al aceptar) |
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
- **Entre procesos, por outbox:** `BuildDocumentXml`, `SendDocumentToDian`, `PollDianStatus`, `SendEmail`, `RenderPdf`, `RegeneratePdf` y `RegenerateAttachedDocument` (estos dos los encolan las rutas `regeneratepdf` y `regenerate-attached-document`; guardan una revisión nueva del archivo y no envían correo). Se escriben en `outbox_messages` en la misma transacción que el cambio de estado y los procesa el Worker.

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
    Task<X509Certificate2> LoadAsync(Guid companyId, string secretRef, CancellationToken ct);  // en memoria
    Task DeleteAsync(Guid companyId, string secretRef, CancellationToken ct);                   // companyId: datos asociados
}

public interface ISecretProtector { string Protect(string purpose, string plain); string Unprotect(string purpose, string cipher); }  // PIN, SMTP, tokens; el propósito separa las llaves por uso

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

public interface IPdfRenderer { Task<byte[]> RenderAsync(DocumentPdfModel model, CancellationToken ct); }   // ADR-04
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
- Entrada: un modelo propio de `ImpiFact.Dian` (ADR-26), con los códigos de catálogo ya resueltos e importes `decimal`; `Application` traduce el dominio a ese modelo. Salida: `string` XML.
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

Estado al 15-nov (fase 1a): `XmlSigner` firma con RSA-SHA256, C14N inclusivo y digests SHA-256; tres referencias (documento con transformación enveloped, `KeyInfo` y `SignedProperties`); política de firma v2 de la DIAN con hash SHA-256 `dMoMvtcG5aIzgYo0tIsSQeVJBDnUnfSOfBpxXrmor0Y=` y la descripción de la sección 10.2 del anexo; `SignerRole` = `supplier`; `SigningTime` con milisegundos y `-05:00`. Diferencias deliberadas con el PHP, a confirmar con la DIAN en la fase 1b **\[verificar\]**:

- `X509SerialNumber` en decimal, como exige XMLDSig; el PHP lo envía en hexadecimal con `0x`.
- Se incluye `xades:Description` en la política; el PHP no la envía.
- Solo se incluye el certificado de la empresa; la política sugiere la cadena completa.
- El nombre del emisor va en orden RFC 2253 (CN primero), como el PHP; el ejemplo del anexo usa el orden inverso.
- Hash de la política en SHA-256; el anexo muestra SHA-512 para certificados SHA-2.

### 6.4 SOAP con WS-Security

- `Opción A (por defecto hasta el spike): cliente propio sobre HttpClient; opción B: cliente WCF con CustomBinding y un MessageInspector para la seguridad. E`l sobre SOAP 1.2 se construye a mano con WS-Addressing y un `wsse:Security` que incluye `Timestamp`, `BinarySecurityToken` (el certificado) y una firma `ds:Signature` sobre el `wsa:To` **\[verificar con la DIAN en la fase 1\]**.
- URLs por ambiente en `DianOptions`: habilitación `https://vpfe-hab.dian.gov.co/WcfDianCustomerServices.svc`, producción `https://vpfe.dian.gov.co/WcfDianCustomerServices.svc`.
- URLs de consulta del QR por ambiente, también en `DianOptions`: habilitación `https://catalogo-vpfe-hab.dian.gov.co/document/searchqr?documentkey=`, producción `https://catalogo-vpfe.dian.gov.co/document/searchqr?documentkey=`. `ImpiFact.Dian` las recibe en su modelo de entrada (`SalesInvoice.QrCodeBaseUrl`, 16-nov) y Application se las pasa desde `DianOptions`; el núcleo no depende de `IOptions` (ADR-26).
- Lo que no va en configuración: espacios de nombres, algoritmos, política de firma, NIT de la DIAN y códigos de catálogo. Son reglas del anexo técnico: si la DIAN las cambia, se cambia el código con sus pruebas golden.
- Operaciones: `SendBillSync`, `SendTestSetAsync`, `SendBillAsync`, `GetStatus`, `GetStatusZip`, `SendEventUpdateStatus`, `GetNumberingRange`, `GetAcquirer`, `GetXmlByDocumentKey`.
- Los archivos se envían como ZIP en base64 con el nombre de archivo que exige la DIAN por tipo de documento.
- Resiliencia (`Microsoft.Extensions.Http.Resilience`): tiempo de espera 60 s por intento, 3 reintentos con retroceso exponencial y jitter solo ante errores transitorios (timeout, 5xx, red); rechazo de negocio no se reintenta; *circuit breaker* tras 5 fallos en 30 s. Valores en `ResilienceOptions`.
- La cadena de resiliencia va alrededor de cada intento en el cliente SOAP, no en el `HttpClient`: cada reintento arma y firma un sobre nuevo, con su propia `wsu:Timestamp`. La firma cubre solo `wsa:To` (supuesto C1, como el PHP), así que su valor no cambia entre reintentos; si la DIAN detectara repeticiones por la firma, también rechazaría los envíos del PHP.
- Si algún intento de una llamada pudo llegar a la DIAN (tiempo agotado o conexión cortada después de enviar, o un 502/504 de un intermediario), la llamada se informa como «enviada sin respuesta» aunque el último intento termine en 503, en un SOAP Fault o con el *circuit breaker* abierto: el documento pasa a `Unconfirmed` y se consulta su estado (sección 7).
- Cada llamada guarda una fila en `dian_submissions` y la solicitud/respuesta en `document_files`. De la solicitud se guarda el ZIP enviado (`SentZip`), no el sobre SOAP: el sobre solo agrega el certificado público, la marca de tiempo y la firma, que cambian en cada intento, y repetiría el documento completo en base64; el ZIP es lo que la DIAN valida. De la respuesta se guarda el sobre completo (`DianResponse`) y el `ApplicationResponse` si viene.

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
| 10 | Worker | Regla 90 (la DIAN ya procesó un documento con ese número: NIT del emisor, prefijo y número): consulta el estado con GetStatus y el CUFE y reconcilia en vez de fallar. Si GetStatus responde el código 66 (la DIAN no tiene ese CUFE), el documento procesado con ese número es otro: el nuestro pasa a `Rejected` con la descripción «El número {número} ya fue usado en la DIAN por otro documento. Revise la numeración de la resolución y emita la factura de nuevo.» |

Reglas:

- El consecutivo se consume en el paso 3; si el documento termina `Failed` queda como hueco registrado.
- Los pasos 7 a 10 son idempotentes: reintentar un mensaje no duplica archivos (`UQ document_id, kind, revision`) ni intentos (`UQ document_id, attempt`).
- El reconsultado de `Unconfirmed` se hace cada 1 minuto durante \~10 minutos y luego cada 15 minutos hasta 24 h; pasado ese plazo pasa a `Failed` con alerta.
- Un documento que la DIAN pudo recibir nunca pasa a `Failed` desde `Queued`. Si el Worker se rinde con `SendDocumentToDian` y algún intento guardado en `dian_submissions` pudo llegar (sin resultado guardado, con un resultado de la DIAN como la regla 90, o una consulta de estado), el documento pasa `Queued → Sent → Unconfirmed` y se programa `PollDianStatus` con GetStatus y el CUFE; `Failed` directo desde `Queued` solo cuando ningún intento pudo llegar. Lo mismo antes de tomar un SOAP Fault del que envía como rechazo: si un intento anterior pudo llegar, cuenta como «sin respuesta».
- GetStatus con código 66 (la DIAN todavía no registra ese CUFE) no es una decisión aunque traiga mensajes: se sigue consultando hasta el plazo. La excepción es la regla 90: si la DIAN dijo que ya procesó un documento con ese número y GetStatus no conoce nuestro CUFE, el número lo usó otro documento (por ejemplo, un envío anterior del set de pruebas con otro CUFE) y el documento pasa a `Rejected` de inmediato, como cualquier rechazo (sin consumo del plan y con el aviso de rechazo). Se guarda el código 66, el último que respondió la DIAN, y la explicación en la descripción; el 90 queda en `dian_submissions`. Lo mismo en una consulta posterior (`PollDianStatus`): un 66 de un documento con un envío (SendBillSync o SendTestSetAsync) que recibió la regla 90 lo rechaza, así se resuelven los documentos que quedaron sin confirmar antes de esta regla.
- Todo documento que la DIAN rechaza deja un aviso para la empresa (`DocumentRejected`, «La DIAN rechazó un documento»): su número y el motivo, con las primeras tres reglas incumplidas y la indicación de corregirlo y emitir uno nuevo o, si la DIAN no trae reglas, la descripción del resultado (en el número ya usado, la explicación de arriba).
- Consumo del plan (paso 9, 17-nov): al pasar a `Accepted`/`AcceptedWithWarnings`, el Worker suma un documento de la familia «factura» (tipos 1 a 5) en `plan_usage` del plan contratado activo de la empresa, en el mismo guardado que la aceptación. La suma es un `INSERT … ON CONFLICT DO UPDATE SET used = used + 1` (`IPlanUsageSql`, propio de cada motor) que la unidad de trabajo ejecuta en la transacción del guardado, después de los cambios de EF (`IPendingWrites`): si el guardado de la aceptación falla (otro proceso aceptó el mismo documento y cambió su `row_version`), la suma tampoco queda; dos aceptaciones simultáneas de la misma empresa se esperan en la fila y no pierden cuentas. Es la misma excepción a la regla «Escrituras (16-nov)» que el consecutivo. Sin plan contratado no se cuenta nada (sin límites, como el plan por defecto del PHP), y los documentos del set de pruebas (ZipKey en `track_id`) no cuentan, como en el PHP.
- Una consulta de estado nunca deja el documento sin otra consulta pendiente: sin certificado activo cuenta como una consulta sin decisión, y si el Worker se rinde con `PollDianStatus` por cualquier otra falla, se programa otra consulta o, pasado el plazo de 24 h, pasa a `Failed` con alerta (`Unconfirmed → Failed`; un documento en `Sent` pasa antes por `Unconfirmed`).
- El JSON de respuesta mantiene los campos legados (`success`, `message`, `ResponseDian`, `cufe`, `urlinvoicexml`, `urlinvoicepdf`, …); el conjunto exacto se fija con los contract tests de la colección Postman.
- Notas crédito/débito: mismo flujo. La factura de referencia es opcional, como en el PHP (operaciones sin referencia 22/8 y 5); si `billing_reference.uuid` corresponde a un documento de la empresa se enlaza `reference_document_id` (17-nov), y ese documento debe estar `Accepted` o `AcceptedWithWarnings` (fase 4): si no, 422 con `code = document.reference_not_accepted` y el error en `billing_reference.uuid`, antes de tomar número. Un CUFE que la empresa no tiene (factura emitida fuera de ImpiFact) se acepta sin enlace, como en el PHP, y el set de pruebas (`/{testSetId}`) no exige la aceptación, porque sus facturas se aceptan de forma asíncrona.
- Plan (paso 2, fase 4): con las reglas y los mensajes del PHP, después del certificado y antes de los catálogos: plan inactivo, mensual vencido al mes o anual al año, o documentos usados iguales a la cantidad del plan → 403 con el mensaje del PHP (`plan.inactive` o `plan.limit_reached`). El periodo 0 no tiene límites; sin plan contratado activo tampoco (el plan por defecto del PHP). El set de pruebas no revisa el plan, como `testSetStore`. Es una revisión blanda, como en el PHP: dos emisiones simultáneas con un solo documento disponible pasan las dos, porque el consumo se cuenta al aceptar.
- Cálculo previo (`POST /api/v1/documents/calculate`, fase 5): el panel no hace cuentas (ADR-21 del frontend). El Api calcula con las líneas del borrador el valor de cada línea, sus impuestos, los impuestos agrupados por `tax_id` y tarifa y `legal_monetary_totals` (`DocumentTotalsCalculator`): cada importe se redondea una vez con `DianRounding` y los totales suman lo ya redondeado, así lo calculado siempre pasa `DianTotalsValidator` en el paso 2. Excepción: si base × tarifa termina entre ,495 y ,5, el impuesto de la línea se escribe un centavo menos, porque la regla AX07 compara el producto sin redondear a pesos enteros. Con los casos golden reproduce los valores del XML salvo el total con impuestos de `rounding-raw-decimals` (4918.12 en vez de 4918.11: el PHP sumaba lo que el cliente mandaba sin redondear). Es cálculo puro: sin consecutivo, sin persistencia, sin la DIAN, sin plan; pide `documents:write` y una empresa.
- Vista previa (`preeliminar-view`): valida (paso 2), arma el documento en memoria en estado `Draft` y genera UBL y PDF sin tomar consecutivo ni persistir nada; devuelve el PDF.
- Contingencia: `invoice-contingency` emite sin enviar (`ContingencyPending`); `send_pendings` los encola.

## 8. Worker

Proceso aparte (`ImpiFact.Worker`) con la misma imagen base que el Api y otro punto de entrada. Se puede escalar horizontalmente: el *locking* de la cola evita que dos instancias tomen el mismo mensaje.

| Servicio | Intervalo | Qué hace |
| --- | --- | --- |
| `OutboxProcessor` | continuo (sondeo 1–5 s con espera adaptativa) | Toma mensajes `status = 0` con `next_attempt_at <= now` usando `SKIP LOCKED`/`READPAST`, los marca en proceso (`locked_until = now + 5 min`) y despacha al handler por `type`. Reintentos con retroceso: 10 s, 1 min, 5 min y 30 min (el escalón de 2 h solo se usa si `MaxAttempts` sube de 5); al quinto fallo queda `Failed` y genera notificación. Cada handler tiene un plazo (`HandlerTimeoutSeconds`, 270 s) menor que el bloqueo, para que otra instancia no lo tome mientras sigue corriendo; un mensaje que se volvió a tomar más veces que `MaxAttempts` sin registrar resultado queda `Failed` sin ejecutarse. Antes de guardar el fallo final se avisa al handler (`OnFailedAsync`) para que deje en estado final lo que el mensaje iba a cambiar; si el proceso se cae entre los dos pasos, el mensaje sigue en proceso y se vuelve a tomar al vencer su bloqueo, así el aviso se repite y nada queda a medias. |
| `DianStatusPoller` | 1 min | Reconsulta documentos `Unconfirmed` (sección 7). |
| `ContingencySender` | 5 min | Encola `send_pendings` de documentos `ContingencyPending` cuando la DIAN vuelve a responder. |
| `CertificateExpiryWatcher` | diario | Crea notificaciones y correo a 30, 15 y 5 días del vencimiento del certificado activo, contados en el calendario de Colombia. Cada umbral se avisa una vez por certificado; si el Worker no corrió el día exacto, se avisa el umbral más estrecho alcanzado. El correo (`SendNoticeEmail`, por la bandeja de salida) sale con el servidor de la plataforma hacia el correo de la empresa. |
| `ResolutionRangeWatcher` | diario | Avisa (notificación, sin correo) cuando queda menos del 10 % del rango o faltan menos de 30 días para `valid_to`; una vez por resolución y condición, y otra vez si la resolución cambia de rango o de vigencia. |
| `Housekeeping` | diario | Purga `outbox_messages` hechos con más de 30 días, `idempotency_keys` vencidas, `refresh_tokens` expirados y `password_reset_tokens` vencidos (usados, revocados o no), por tandas de `DeleteBatchSize` filas. Mover `request`/`response` antiguos a almacenamiento frío queda para después: en la fase 4 no hay almacenamiento frío configurado. |

Cada mensaje del outbox lleva `company_id`; el Worker lo establece en `ITenantContext` antes de ejecutar el handler. Un fallo de un tenant nunca detiene el procesamiento de los demás. La excepción es `SendPasswordResetEmail` (17-nov, fase 5): un mensaje de la plataforma, sin empresa, que envía el enlace para restablecer la contraseña (sección 9.2) con el servidor de correo de la plataforma y necesita `Panel:BaseUrl`. `Housekeeping` borra los tokens de `password_reset_tokens` ya vencidos (18-nov, fase 5); si el mensaje sigue en la bandeja y su token ya no existe, no envía nada.

Las tareas diarias (`Housekeeping` y los dos vigilantes) corren una vez al día a `ScheduledJobs:RunAtUtc` y, si `RunOnStartup` está activo, también al arrancar el Worker, para no saltarse un día. Con varias instancias del Worker:

- Cada tarea toma un *advisory lock* de PostgreSQL (`pg_try_advisory_lock`, un bloqueo con nombre por sesión que no toca tablas) en una conexión propia mientras corre; la instancia que no lo obtiene salta la tarea. Si la instancia se cae, la base cierra la sesión y el bloqueo se suelta solo.
- Cada aviso tiene una llave estable (por ejemplo `certificate-expiry:{id}:30`) y el id de su notificación sale de esa llave (los primeros 16 bytes de su SHA-256, como uuid versión 8). La llave primaria de `notifications` impide el duplicado aunque la tarea corra dos veces seguidas o dos instancias guarden el mismo aviso a la vez, sin columnas ni migraciones nuevas. El mensaje `SendNoticeEmail` se guarda en la misma transacción que la notificación, así el correo tampoco se duplica.

## 9. Capa API

### 9.1 Pipeline (orden de middleware)

1. `ForwardedHeaders` (detrás de proxy/ingress).
2. `CorrelationId` (acepta `X-Correlation-Id` o genera uno; entra al contexto de Serilog).
3. `ExceptionHandler` → `ProblemDetails` (RFC 9457) más los campos legados `success` y `message`.
4. `HttpsRedirection`, `HSTS`, CORS con lista explícita (`Cors:Origins`, con credenciales para la cookie del panel; nunca `*`).
5. `RateLimiter` (por API key, por IP y más estricto en login).
6. `Authentication` → `TenantResolution` → `Authorization`.
7. Endpoints.

### 9.2 Autenticación

- **Esquema de política** que decide por el encabezado `Authorization: Bearer <token>`: si el token empieza por `apk_` es API key; si no, JWT. Así el Postman legado (que usa Bearer) funciona sin cambios.
- **API key**: se busca por `key_hash` (SHA-256), se verifica `revoked_at`/`expires_at`, se actualiza `last_used_at` (sin bloquear la petición) y se crean los claims `company_id` y `scopes`.
- **JWT** del panel: vida 15 min, refresh de 14 días rotativo (`refresh_tokens`), claims `sub`, `role` y `company_id` activa; cambiar de empresa emite un token nuevo.
- **Rotación del refresh y detección de reúso**: cada renovación revoca el token presentado con `replaced_by` apuntando al nuevo. Presentar otra vez un token ya revocado es señal de robo: se revocan todos los refresh vigentes del usuario (en una unidad de trabajo propia) y responde 401 `identity.invalid_refresh_token`.
- **Período de gracia de la rotación** (`Jwt:RefreshRotationGraceSeconds`, 30 s por defecto, de 0 a 300; 0 lo apaga): caso real, el navegador corta una renovación justo después de que el servidor rotó el token, se queda con la cookie vieja y su renovación siguiente cerraba todas las sesiones del usuario. Un token presentado otra vez se toma como respuesta perdida, no como robo, solo si se cumplen las tres cosas: lo revocó una rotación (tiene `replaced_by`), eso pasó hace menos que el período de gracia, y su reemplazo sigue sin usar (vigente, nunca rotado). Entonces se revoca ese reemplazo (con `replaced_by` al nuevo) y se entrega un par nuevo de la misma sesión (el mismo usuario; la empresa activa se elige como en cualquier renovación), sin tocar las demás sesiones. Fuera del período, con el reemplazo ya usado, o con un token cerrado por un logout o por la respuesta de robo, sigue la respuesta de robo sin cambios. La lectura del reemplazo usa el plazo propio de las revocaciones y no la cancelación de la petición, para que la respuesta de robo termine aunque el navegador corte. Concurrencia: dos solicitudes con el mismo token viejo intentan reclamar el mismo reemplazo con `RevokeAsync`, como una rotación normal reclama el token presentado; si una ya lo revocó, la otra lo ve revocado y recibe la respuesta de robo, y si lo leyeron a la vez, `row_version` hace fallar el segundo guardado (409). Si llegan una tras otra, la segunda rota el reemplazo que recibió la primera, que deja de servir: nunca quedan dos refresh vivos. Costo aceptado: quien robe un token y lo use dentro del período, antes de que el dueño use el reemplazo, obtiene un par; cuando el dueño presente su token revocado fuera del período, la respuesta de robo cierra todas las sesiones, la del atacante incluida.
- **Sesión del panel en el navegador** (17-nov, ADR-20 de «Arquitectura frontend»): `POST /api/v1/auth/login`, `/refresh` y `/logout` con el encabezado `X-Client: panel` ponen el refresh en la cookie `impifact_refresh` (`HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/api/v1/auth`, vence con el token) y no lo devuelven en el cuerpo; sin el encabezado, las integraciones siguen recibiéndolo en el JSON como antes. La renovación lee la cookie cuando el cuerpo no trae `refresh_token` (el cuerpo puede faltar o traer solo `company_id`) y reutiliza la misma rotación y detección de reúso; un 401 borra la cookie, y el panel sin cookie recibe 401. `Secure` solo se omite en Development, por HTTP y hacia `localhost` o `127.0.0.1`.
- **Cierre de sesión** `POST /api/v1/auth/logout`: revoca el refresh presentado (cuerpo o cookie) sin reemplazo; como cada renovación revoca el anterior, solo el último de la cadena estaba vigente y la cadena queda cerrada. Las demás sesiones del usuario siguen vivas y el JWT ya entregado vale hasta vencer. Responde 204 aunque el token no exista o ya esté revocado (un token revocado aquí no cuenta como reúso) y, con `X-Client: panel`, borra la cookie.
- **Plazo de las revocaciones**: el cierre de sesión y la revocación de todas las sesiones por un refresh reusado no usan la cancelación de la petición (si el navegador corta, la revocación debe terminar igual); cada una tiene su propio plazo de 10 segundos (`ISessionPolicy.RevocationTimeout`). Si ese plazo se cumple porque la base no termina la escritura, el motor cancela la consulta y la API responde el mismo 503 `identity.store_unavailable` (con `Retry-After`) que ante una base caída, no un 500, y no borra la cookie, para que el panel lo intente otra vez (`SessionRevocation`). Solo se traduce la cancelación que causa ese plazo; la de una petición abandonada sigue siendo una cancelación.
- **CSRF** (solicitud falsificada desde otro sitio) sobre la cookie, en dos capas: `SameSite=Strict` (el navegador no la manda desde otro sitio) y la cookie solo se acepta con `X-Client: panel`; sin él, 403 `identity.client_header_required` y la sesión no se toca. Un formulario no puede poner ese encabezado y otra página de otro origen solo puede hacerlo tras el *preflight* de CORS, que solo aprueban los orígenes de `Cors:Origins`; si un subdominio vecino comprometido planta su propia cookie para el dominio padre, la solicitud llega con la cookie dos veces y se rechaza con 401 `identity.invalid_refresh_token` sin tocar la sesión real. Se eligió `Strict` y no `Lax` porque la cookie solo la usan llamadas `fetch`, nunca navegaciones. Requiere que el panel y la API estén en el mismo sitio (mismo dominio registrable) y, en desarrollo, con el mismo nombre de host.
- **Empresas de quien llama** (17-nov, fase 5): `GET /api/v1/me/companies` (`listMyCompanies`), con cualquier credencial y sin exigir empresa activa, devuelve `items` con `id`, `name` (razón social), `nit`, `dv`, `roles` e `is_active`, ordenados por razón social (es-CO, sin distinguir mayúsculas). Un usuario del panel ve las empresas de `user_companies` (todas, activas o no; la empresa activa del token no reduce la lista), que son las únicas que el login y la renovación le dejan activar; por eso un `platform_admin` sin pertenencias recibe la lista vacía. `roles` es una lista porque `user_companies` permite más de un rol por empresa. Una API key de empresa ve solo su empresa, con `roles` vacío; una de plataforma, la lista vacía. Sin paginar: un usuario pertenece a pocas empresas (se agregan de una en una). Lee `companies` por el puerto `IUserCompanyReads`, porque Identity no usa las entidades del módulo Company.
- **Recuperación de contraseña** (17-nov, fase 5; tabla `password_reset_tokens`, Modelo de datos sección 3.2): dos rutas anónimas con el límite estricto del login y `Cache-Control: no-store`.
  - `POST /api/v1/auth/password-reset/request` (`requestPasswordReset`) con `{"email"}` responde siempre 202 con el mismo `message`, exista o no la cuenta, esté activa o no. En todos los casos crea y cifra un token (así el tiempo de respuesta se parece); solo para un usuario activo revoca sus tokens sin usar, guarda el resumen SHA-256 del nuevo (32 bytes aleatorios, vigencia 30 minutos) y encola `SendPasswordResetEmail` en la misma transacción. El mensaje no lleva el token en claro: lleva el id de la fila y el token cifrado con `ISecretProtector` (propósito `password-reset.token`). Como la solicitud es anónima y la bandeja de salida solo deja escribir sin empresa a la plataforma o al sistema, el mensaje se escribe con el puerto `IPasswordResetEmailQueue`, que usa la misma `EfOutbox` actuando como sistema y solo para ese tipo, siempre sin empresa. Queda una diferencia de milisegundos por las escrituras, que el límite por IP vuelve impráctica de medir; dos solicitudes simultáneas del mismo usuario pueden dejar dos tokens vigentes, los dos para el mismo correo.
  - El Worker (`SendPasswordResetEmailHandler`) lee el correo y el nombre de la base al enviar (no viajan en la bandeja), no envía si el token ya no sirve (usado, revocado, vencido o usuario inactivo), descifra el token solo para armar `{Panel:BaseUrl}/restablecer?token=...` y envía con el servidor de la plataforma un texto en español que dice que vence en 30 minutos, que es de un solo uso y que cierra las sesiones. Un token que no se puede descifrar falla el mensaje sin reintentos. Ni el token, ni el enlace, ni el correo van a los registros.
  - `POST /api/v1/auth/password-reset/confirm` (`confirmPasswordReset`) con `{"token", "new_password"}`: primero la política de contraseñas (la misma de `POST /api/v1/users`, de 10 a 256 caracteres; 422 con `errors.new_password`, sin gastar el token); luego un token desconocido, vencido, usado, revocado o de un usuario inactivo responde el mismo 422 `identity.invalid_reset_token` («El enlace para restablecer la contraseña no es válido o ya venció. Solicite uno nuevo.»). Con un token válido lo marca usado (la versión de fila hace fallar a la segunda de dos confirmaciones simultáneas), guarda el resumen nuevo con el resumidor de siempre, cambia `security_stamp` y revoca todos los `refresh_tokens` del usuario, y responde 204. La revocación se guarda aparte, de inmediato (la misma de la detección de reúso): si la transacción no se confirma, las sesiones quedan cerradas y la contraseña sin cambiar, que es el lado seguro. Los JWT ya entregados valen hasta vencer (15 minutos como máximo).
- **Plataforma**: llave con `company_id` nulo y alcance `platform:*` reemplaza `X-Admin-Token` para crear/borrar empresas, planes y revendedores.
- **Propiedad por NIT**: toda ruta con `{nit}` o con NIT en el cuerpo compara contra la empresa del token; si no coincide responde 403 sin revelar si el NIT existe.
- Políticas de autorización por alcance (`documents:write`, `documents:read`, `config:write`, `platform:*`) y por rol en el panel. Los usuarios del panel reciben sus alcances según sus roles al emitir el JWT (16-nov), en claims `scope`, así las mismas políticas sirven para usuarios y API keys: `owner` → `documents:read`, `documents:write` y `config:write`; `seller` → `documents:read` y `documents:write`; `customer` → `documents:read`; `platform_admin` → `platform:*`; `reseller` → ninguno por ahora. Los alcances de empresa solo se dan con empresa activa en el token y `platform:*` solo sin empresa activa.

### 9.3 Endpoints

- Estilo *Minimal API* agrupado por módulo (`MapGroup`), un archivo por caso de uso o por grupo pequeño; cada endpoint solo valida la forma HTTP, llama al handler y traduce el `Result`.
- Rutas: `/api/v1/...` es la ruta canónica (la usa el panel). Las del contrato legado (`/api/ubl2.1/...`, `/api/information/...`, `/api/table/...`, etc., ver anexo A) se publican como alias del mismo handler para los contract tests. No aparecen en la documentación OpenAPI (llevan `ExcludeFromDescription()`) y se borran en la fase 7, con el cierre de la referencia PHP.
- Los DTO de entrada replican los nombres JSON del contrato (`[JsonPropertyName]`) incluyendo los typos heredados.
- Planes (carpeta 09 del Postman, fase 4): `PUT /api/ubl2.1/plan` (alias `PUT /api/v1/plans/{id}`), `GET /api/ubl2.1/plan/query[/{id}]` (`GET /api/v1/plans[/{id}]`) y `GET /api/ubl2.1/plan/queryusersbyplan/{id}` (`GET /api/v1/plans/{id}/companies`) piden `platform:*` sin empresa (en el PHP eran públicas); `GET /api/ubl2.1/plan/infoplanuser` (`GET /api/v1/plans/current`) pide `documents:read` de la empresa. La asignación `PUT /api/v1/companies/{nit}/plan` con `{"plan_id": n}` (plataforma) es propia de ImpiFact: desactiva el plan contratado activo y crea otro que empieza ahora, así volver a asignar el mismo plan lo renueva. Diferencias con el PHP: un id inexistente es 404 (el PHP daba 500), el id 0 queda reservado, un plan nuevo sin nombre es 422 y el nombre único no choca consigo mismo; `queryusersbyplan` devuelve solo los datos de identificación de cada empresa; `infoplanuser` repite el mismo plan en `plan` a `plan4`, porque un plan contratado trae la cantidad de cada familia. `POST /api/v1/plans` (`createPlan`, 18-nov, sin equivalente en el PHP) crea un plan con el id que asigna el servidor (el siguiente al mayor; un `id` en el cuerpo se ignora) y responde 201 con `Location`; el panel lo usa al crear y deja el id de solo lectura al editar con `PUT /api/v1/plans/{id}`. No usa la identidad de la columna `plans.id`: el `PUT` heredado inserta ids elegidos por quien llama sin mover esa secuencia, que podría repetirlos. Dos altas simultáneas que calculan el mismo id chocan con la llave primaria; la transacción se revierte (`persistence.conflict`) y el endpoint repite la solicitud hasta 5 veces, ya con el plan de la otra a la vista. Los ids siguen limitados a 999, como en el `PUT`. Una cantidad 0 en una familia no deja emitir documentos de esa familia (`PlanRules`).
- Notificaciones (campana del panel, fase 5, sin equivalente en el PHP): `GET /api/v1/notifications` (cursor `(created_at, id)` como el listado de documentos, `limit` 20 por defecto y máximo 100, filtro `unread=true|false` y `unread_count` total), `POST /api/v1/notifications/{id}/read` (204) y `POST /api/v1/notifications/read-all` (`{"marked_count": n}`). Piden `documents:read` con empresa activa: cada fila de `notifications` es de una empresa (`company_id` obligatorio), así que la plataforma no tiene avisos propios. Se ven los avisos de toda la empresa (`user_id` nulo) y, con sesión del panel, los dirigidos al usuario; uno de otra empresa o de otro usuario responde 404 (`notification.not_found`). Marcar es idempotente: el aviso ya leído no se reescribe. Usan el índice existente `(company_id, user_id, is_read, created_at DESC)`, sin migración; no hay `read_at`: la marca es `is_read` y la fecha del cambio queda en `updated_at` con el reloj de la aplicación.
- Listado, exportación y tablero del panel (fase 5, sin equivalente en el PHP), con `documents:read` de la empresa activa:
  - `GET /api/v1/documents` (`listDocuments`) pagina por cursor `(issued_at, id)` descendente (formato de cursor sin cambios, `limit` 50 por defecto y máximo 200; el panel pide hasta 100) y filtra en el servidor por `kind` (`invoice`, `credit_note`, `debit_note`) o `document_type_id` (si llegan los dos deben coincidir), uno o varios `status` (separados por coma o repitiendo el parámetro), `from`/`to` (días de Colombia), `customer` (texto en la identificación o el nombre del adquiriente copiado en `customer_snapshot`, sin distinguir mayúsculas; los comodines de LIKE se escapan) y `number` (número completo con prefijo, o solo dígitos en cualquier prefijo). Cada fila suma `kind` y `email_status`. `include_total=true` agrega `total_count` con un `count(*)` de los mismos filtros, que recorre todas las filas que cumplen: se pide en la primera página o al cambiar filtros.
  - `GET /api/v1/documents/export.csv` (`exportDocumentsCsv`) usa los mismos filtros y orden, cuenta primero y responde 422 `documents.export_too_large` si pasa de 50.000 filas; si no, escribe el CSV mientras lee las filas de a una (`QueryUnbufferedAsync` de Dapper), sin cargarlas en memoria. Formato para hoja de cálculo en es-CO: UTF-8 con BOM, `;`, CRLF, importes con coma decimal sin separador de miles, encabezados y estados en español, y un apóstrofo delante de los textos que empiezan por `=`, `+`, `-` o `@` (inyección de fórmulas).
  - `GET /api/v1/dashboard/summary` (`getDashboardSummary`) arma el tablero con cuatro consultas fijas: un conteo agrupado por mes de Colombia, tipo y estado de los últimos 12 meses; el plan vigente con `IPlanStore` y `PlanRules.PeriodEnd` de Billing (usado, límite y restante por familia); el certificado activo que vence más tarde (días en el calendario de Colombia); y las resoluciones activas por agotarse con `ResolutionAlertRule` (`Application/Abstractions/Maintenance`), la misma regla del vigilante diario (menos del 10 % del rango o menos de 30 días de vigencia).
  - Índices: el listado y el conteo usan `ix_documents_company_issued` (company_id, issued_at DESC) y recorren el índice hacia atrás hasta juntar la página; los demás filtros se evalúan sobre esas filas, siempre dentro de la empresa. No se agregó migración: un índice por tipo o por texto del adquiriente (trigramas) se evaluará con volúmenes reales.
- Lecturas de la configuración de la empresa para el panel (fase 5, sin equivalente en el PHP), con `config:write` y el `{nit}` de la empresa de la credencial (otro NIT, exista o no, responde el mismo 403 `company.forbidden`):
  - `GET /api/v1/companies/{nit}` (`getCompany`) devuelve `CompanyResponse`, los mismos nombres que recibe `updateCompany` más `identification_number`, `dv`, `environments` e `is_active`: el panel prefija el formulario y reenvía ese cuerpo.
  - `GET /api/v1/companies/{nit}/environments` (`getCompanyEnvironments`) y `GET /api/v1/companies/{nit}/software` (`getCompanySoftware`): del software, `identifier`, `url`, `test_set_id` y `has_pin` por familia y ambiente; el PIN nunca sale.
  - `GET /api/v1/companies/{nit}/resolutions` (`listCompanyResolutions`) lista todas las resoluciones (activas e inactivas, de los dos ambientes) con `kind`, rango, vigencia, `next_number` (el consecutivo `R:{id}`, nunca por debajo de `from`), `remaining` y `days_left` (con `ResolutionAlertRule`) y `has_technical_key` en lugar de la clave técnica, que solo se ve en la respuesta de `saveCompanyResolution`.
  - `PUT /api/v1/companies/{nit}/resolutions/{id}/next-number` (`setResolutionNextNumber`, fase 6) fija `next_number` del consecutivo `R:{id}`: para una empresa que viene de otro sistema de facturación o que ya usó números del rango ante la DIAN (19-nov: una empresa recreada en el panel empezó en 990000000 cuando la DIAN ya tenía el 990000001 de un set de pruebas anterior y respondió con la regla 90). El número debe estar en `from`-`to` y no puede bajar (422 con `errors.next_number`); resolución ajena o inexistente, 404 `resolution.not_found`. Usa el mismo UPDATE de la reserva de un número (`INumberingSql.RaiseAboveAsync`, `GREATEST` en PostgreSQL) por el puerto `INumberingAdvance`: deja `max(actual, pedido)` y bloquea la fila, así una emisión simultánea toma su número antes (y el UPDATE ve el valor que dejó) o espera y sigue desde el nuevo; si el valor que quedó es mayor que el pedido, el caso de uso responde 422 y la transacción se revierte. Nunca baja ni repite un número.
  - `GET /api/v1/companies/{nit}/setup-status` (`getCompanySetupStatus`) resume lo guardado para el asistente: datos obligatorios, certificado activo con días por delante, software y `test_set_id` de factura en el ambiente actual de la factura, `test_set_accepted` (alguna respuesta GetStatusZip guardada con `is_valid`; nulo si no hay) y, para factura y notas, si hay una resolución activa de ese ambiente vigente hoy y con números (la de factura además con clave técnica y fechas). No llama a la DIAN.
  - El consecutivo y las respuestas de la DIAN son tablas de Documents: el módulo Company las lee por el puerto `ICompanyConfigurationReads` (`Application/Abstractions/Companies`).
- Listado de API keys (fase 5): `GET /api/v1/api-keys` (`listApiKeys`) con la política de credenciales (`config:write` de la empresa o `platform:*` sin empresa) y las mismas reglas de crear y revocar: un usuario del panel debe ser `owner` de la empresa activa; sin empresa solo la plataforma, que ve las llaves de plataforma (`company_id` nulo). Cursor `(created_at, id)` descendente, `limit` 20 por defecto y máximo 100. La consulta (`IApiKeyReads`) nunca lee `key_hash`.
- Empresas de la plataforma (fase 5): `GET /api/v1/platform/companies` (`listPlatformCompanies`, `platform:*` sin empresa) ordena por `lower(name), id` con cursor de esa misma pareja (calculada por la base), busca con ILIKE en NIT o razón social (comodines escapados, sin caracteres de control) y trae el plan contratado activo más reciente (`LATERAL` sobre `ix_company_plans_company_id_is_active`). El plan es de Billing: Company lo lee por el puerto `IPlatformCompanyReads`.
- Documentación con `Microsoft.AspNetCore.OpenApi` y Scalar en `/scalar` (solo en desarrollo y staging, protegido en producción). Solo documenta las rutas `/api/v1`, en español: introducción con el flujo de integración, credenciales, alcances, errores e idempotencia; esquema Bearer para escribir la credencial en Scalar; descripción por sección y por ruta, con el permiso que pide cada una (`ImpiFact.Api/OpenApi`).

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
- `download/{nit}/{archivo}/{type_response?}` (también bajo `/api/download`) reconoce los nombres del PHP: `FES-`, `NCS-` o `NDS-{prefijo}{número}.xml` (XML firmado) y `.pdf`, `Attachment-{prefijo}{número}.xml` (AttachedDocument) y `ZipAttachm-{prefijo}{número}.zip` (el ZIP del correo, armado al vuelo). Entrega la revisión más reciente; `BASE64` responde `{success, message, filebase64}`, `INLINE` el archivo sin nombre de descarga. Alias canónico: `GET /api/v1/documents/{id}/files/{xml|pdf|attached-document|zip}`. Con `documents:read`.

### 9.6 Rutas utilitarias de la primera entrega

Ninguna espera a la DIAN (decisión C5 de la fase 4). Solo `numbering-range` y la consulta de adquiriente (`GET /api/v1/customers/lookup`, sección 9.7) la llaman en vivo desde la API; las demás responden con lo que guardó el Worker o encolan el trabajo.

| Ruta heredada (`/api/ubl2.1`) | Alias canónico | Alcance | Qué hace |
| --- | --- | --- | --- |
| `POST status/document/{trackId}[/{GuardarEn}]` | `GET /api/v1/documents/by-cufe/{cufe}/dian-status` | `documents:read` | Última respuesta guardada de SendBillSync, GetStatus o GetStatusZip del documento: `{success, message, ResponseDian, reqzip: null, rptazip, document}` |
| `POST status/zip/{trackId}[/{GuardarEn}]` | `GET /api/v1/documents/zip/{zipKey}/dian-status` | `documents:read` | Última respuesta guardada de GetStatusZip para esa ZipKey |
| `POST xml/document/{trackId}[/{GuardarEn}]` | `GET /api/v1/documents/by-cufe/{cufe}/xml` | `documents:read` | XML firmado guardado con la forma de GetXmlByDocumentKey; solo documentos aceptados |
| `POST regeneratepdf[/{prefix}/{number}/{cufe}]` | `POST /api/v1/documents/{id}/pdf/regenerate` | `documents:write` | Encola `RegeneratePdf` |
| `POST regenerate-attached-document` | `POST /api/v1/documents/{id}/attached-document/regenerate` | `documents:write` | Encola `RegenerateAttachedDocument`; exige ApplicationResponse guardado y certificado activo |
| `POST send-email` | `POST /api/v1/documents/{id}/email` | `documents:write` | Deja `email_status` sin enviar y encola `SendEmail` con los destinatarios de la solicitud original |
| `POST numbering-range` | `GET /api/v1/companies/{nit}/numbering-ranges?software_id=` | `config:write` | GetNumberingRange en vivo con el NIT de la empresa como emisor y proveedor: `{success, message, ResponseDian, certificate_days_left, operation_code, operation_description, numbering_ranges}`; SOAP Fault 422, sin respuesta 503 |

- `ResponseDian` es el sobre SOAP completo convertido con las reglas del PHP (`DianLegacyJson`): hijos por nombre sin prefijo, atributos en `_attributes`, texto solo como valor y repetidos como lista.
- Las regeneraciones y el correo solo valen para documentos `Accepted` o `AcceptedWithWarnings` (si no, 409 `document.not_accepted`); un documento de otra empresa responde 404. El documento se busca por `cufe` o por `prefix` y `number`, en ese orden, como el PHP.
- `config/environment` y `certificate-end-date` ya estaban en el módulo Company (fase 2).
- `config/certificate` (GET) **no se implementa**; se reemplaza por consulta de metadatos (`certificates-listing`).

### 9.7 Clientes y productos (fase 5)

Maestros del panel (`/clientes` y `/productos`, Arquitectura frontend sección 7). Todas las rutas piden `documents:write`: lo tienen el propietario, el vendedor y las API keys de emisión, y no el rol cliente del portal ni las llaves de solo consulta, que con `documents:read` verían los datos de todos los clientes de la empresa. Un id de otra empresa responde 404.

| Ruta | `operationId` | Qué hace |
| --- | --- | --- |
| `GET /api/v1/customers?search=&status=&sort=&cursor=&limit=` | `listCustomers` | Página de clientes. `search` busca dentro del número o del nombre sin distinguir mayúsculas; `status` `active` (por defecto), `inactive` o `all`; `sort` `name` (por defecto) o `identification_number`, con `-` para descendente; cursor por clave (columna, id), 20 por defecto y 100 como máximo |
| `GET /api/v1/customers/{id}` | `getCustomer` | Un cliente |
| `POST /api/v1/customers` | `createCustomer` | 201 con `Location`. Reglas del cliente de la emisión (identificación alfanumérica hasta 15, `dv` obligatorio con NIT y verificado, correo, catálogos). Mismo tipo y número en la empresa, aunque esté desactivado: 409 `customer.duplicate` |
| `PUT /api/v1/customers/{id}` | `updateCustomer` | Reemplaza todos los campos; `is_active: true` reactiva |
| `POST /api/v1/customers/{id}/deactivate` | `deactivateCustomer` | 204, idempotente. Nunca borra la fila (los documentos pueden referirse a ella) |
| `GET /api/v1/customers/lookup?type=&number=` | `lookupAcquirer` | GetAcquirer en vivo con el certificado activo y el ambiente de facturación de la empresa; a la DIAN va el código del tipo (31 para NIT), no el id del catálogo. 200 `{found, status_code, message, name, email}` (código 200 de la DIAN: encontrado; 404: no registrado); sin certificado 404, vencido 422, otro código de la DIAN 422 `dian.rejected`, SOAP Fault 422 `dian.fault`, sin respuesta 503. No guarda nada ni registra la identificación consultada |
| `GET /api/v1/products?search=&status=&sort=&cursor=&limit=` | `listProducts` | Como clientes; `search` en código o nombre, `sort` `name` (por defecto) o `code` |
| `GET /api/v1/products/{id}` | `getProduct` | Un producto |
| `POST /api/v1/products` | `createProduct` | 201. `code` único por empresa (409 `product.duplicate`), `name` (va en la línea), `unit_measure_id`, `unit_price` (hasta 6 decimales), `type_item_identification_id` (4 si no llega) y el impuesto: `tax_id` con `tax_percent` (obligatorio salvo con 2 y 10) o `tax_per_unit_amount` (obligatorio con 10), las reglas de `tax_totals` de la emisión |
| `PUT /api/v1/products/{id}` | `updateProduct` | Reemplaza todos los campos; `is_active: true` reactiva |
| `POST /api/v1/products/{id}/deactivate` | `deactivateProduct` | 204, idempotente, sin borrar |

- Desactivar no usa `deleted_at`: el índice único de `customers` y de `items` incluye las filas con borrado lógico, así que una fila oculta impediría crear otra igual sin que el panel pudiera verla ni reactivarla.
- Por confirmar con la DIAN real: los nombres de los campos de la respuesta de GetAcquirer (`StatusCode`, `Message`, `ReceiverName`, `ReceiverEmail`; el lector no depende del espacio de nombres) y que `identificationType` sea el código DIAN del tipo y no el id que mandaba el PHP.

### 9.8 Catálogos de la DIAN (fase 5)

`GET /api/v1/catalogs/{name}` (`listCatalog`) publica los catálogos sembrados que usan los formularios del panel, con cualquier credencial (usuario o API key, sin alcance ni empresa activa): son iguales para todas las empresas.

- Nombres: `identification-types`, `organization-types`, `regimes`, `liabilities`, `municipalities`, `departments`, `countries`, `unit-measures`, `taxes`, `payment-forms`, `payment-methods`, `document-types`, `currencies`, `item-identification-types`, `credit-note-discrepancy-responses` y `debit-note-discrepancy-responses` (`CatalogDefinitions`). Otro nombre responde 404 `catalog.not_found`.
- Respuesta `{name, version, items, next_cursor}`; cada fila `{id, code, name, is_active}` más `department_id` (municipios), `country_id` (departamentos) o `description` (impuestos). Orden por `id`; solo filas activas salvo `include_inactive=true`.
- `q` (hasta 100 caracteres, sin caracteres de control) busca dentro del código o del nombre sin distinguir mayúsculas ni tildes. El caso de uso lee el catálogo completo (el más largo tiene unas 1.100 filas) y filtra en memoria, porque PostgreSQL no compara sin tildes sin la extensión `unaccent`.
- `municipalities` y `unit-measures` se paginan por cursor (el id de la última fila, opaco para el cliente; `limit` 50 por defecto y 200 como máximo); los demás salen completos y `next_cursor` es nulo.
- `department_id` (18-nov) deja en `municipalities` solo los municipios de ese departamento (por ejemplo `department_id=11`, Cauca: 42 municipios, entre ellos Popayán) y se combina con `q` y con el cursor. Un valor que no es un entero mayor que 0, o el filtro en otro catálogo, responde 422 con el error en `department_id`. El panel pide primero el departamento y luego busca sus municipios.
- Caché: `version` son los primeros 16 bytes del SHA-256 de todas las filas del catálogo (también las inactivas) y de la forma de la respuesta; es el `ETag` fuerte de la respuesta (el cuerpo de una URL solo depende de esa versión y de sus parámetros) y `If-None-Match` igual responde 304 sin cuerpo. `Cache-Control: public, max-age=3600`: los datos no son de ninguna empresa. Un cambio del sincronizador de catálogos o de `is_active` cambia la versión.

### 9.9 Logo de la empresa (fase 5)

El logo va en el encabezado del PDF (la representación gráfica) de los documentos que se generen desde que se sube, y en la vista previa. Equivale a `PUT config/logo` del PHP, que lo recibía en Base64 y lo guardaba como `{nit}{dv}.jpg`; la ruta heredada no se publica todavía.

| Ruta | `operationId` | Alcance | Qué hace |
| --- | --- | --- | --- |
| `PUT /api/v1/companies/{nit}/logo` | `saveCompanyLogo` | `config:write` | `multipart/form-data` con el campo `logo`. Solo PNG o JPEG reconocidos por sus primeros bytes (el tipo declarado no cuenta; SVG, GIF y demás no), de 1 byte a 512 KiB; si no, 422 con `errors.logo`. El cuerpo completo tiene un tope de 1 MiB. Responde `{content_type, size_bytes}` |
| `GET /api/v1/companies/{nit}/logo` | `getCompanyLogo` | `documents:read` | La imagen con su tipo, `Cache-Control: private, no-cache` y `X-Content-Type-Options: nosniff`; sin logo, 404 `company.logo_not_found` |
| `DELETE /api/v1/companies/{nit}/logo` | `deleteCompanyLogo` | `config:write` | 204, también sin logo |

- El `{nit}` debe ser el de la credencial (403 `company.forbidden`), como el resto del módulo Company.
- Se guarda en `company_logos` (una fila por empresa con `storage_key` y `content_type`, tabla de la migración inicial) y el archivo en `IBlobStorage` con la clave `{empresa}/company/logo-{id}.{png|jpg}`. Cada subida escribe un archivo nuevo y la fila le apunta en la transacción del comando; el archivo reemplazado queda huérfano, como en `document_files`. Quitar el logo borra la fila y el archivo.
- El PDF lo pide por el puerto `ICompanyLogos` (`Application/Abstractions/Companies`), así Documents no usa el módulo Company. `GotenbergPdfRenderer` manda la imagen a Gotenberg como `logo.png` o `logo.jpg` junto al HTML, y la plantilla la muestra arriba del título solo si existe.

### 9.10 Vista previa del PDF de la factura y de las notas (fase 5)

`POST /api/v1/documents/invoices/preview` (`previewInvoice`, `documents:write`) es el `invoice/preeliminar-view` del PHP: recibe el mismo cuerpo de `issueInvoice` y responde el PDF (`application/pdf`, `Content-Disposition: inline`, `Cache-Control: no-store`) en la misma solicitud. El botón «Vista previa» del formulario de emisión (Arquitectura frontend, sección 8.1) lo abre sin consumir consecutivo.

- Revisa lo mismo que la emisión con las mismas piezas (`IssueDocumentRules`, `IssueChecks.SelectResolution` y `LoadCatalogsAsync`, `DianTotalsValidator`): forma del cuerpo (422), empresa activa (403 `company.inactive`), resolución activa del tipo y el prefijo (422), catálogos y totales (422 `validation.failed`). No pide certificado ni software, no revisa el plan, no toma número, no calcula el CUFE, no firma, no guarda nada (no es un comando: no abre transacción) y no llama a la DIAN; `Idempotency-Key` no cuenta.
- El modelo sale de `DocumentPdfModelBuilder.BuildPreviewAsync`, que comparte con el PDF del documento emitido el armado de líneas, impuestos, retenciones, pagos, cargos, totales y logo; el adquiriente toma los mismos valores por defecto que la copia de la emisión. Cambia el número (`{prefijo} - BORRADOR`), el CUFE («VISTA PREVIA - SIN CUFE»), la validación de la DIAN («Sin validar»), el QR (un texto de muestra, no la URL de consulta) y la marca de agua «VISTA PREVIA - SIN VALIDEZ» de la plantilla (`IsPreview`). El PHP mostraba el siguiente consecutivo y un CUFE vacío.
- La API registra la generación del PDF (`AddPdfRendering`: plantillas, cliente de Gotenberg y `IPdfRenderer`) y convierte en línea. Si Gotenberg no responde, responde con error, no devuelve un PDF o se pasa de `Pdf:TimeoutSeconds`, la respuesta es 503 `pdf.unavailable` con `Retry-After`; si quien llama cancela la solicitud, no se responde 503.
- Notas (18-nov, B10): `POST /api/v1/documents/credit-notes/preview` (`previewCreditNote`) y `/debit-notes/preview` (`previewDebitNote`) usan el mismo caso de uso (`PreviewDocumentHandler`) con la especificación de cada nota: sus líneas, `requested_monetary_totals` en la débito, la resolución NC o ND y «VISTA PREVIA - SIN CUDE»; el PDF no imprime resolución, como el de la nota emitida. No revisan que la factura de `billing_reference` esté aceptada: una nota se prepara mientras la factura espera respuesta y esa regla se revisa al emitir.
- Pendiente: el alias heredado `POST /api/ubl2.1/invoice/preeliminar-view`, que en el PHP devolvía JSON con el PDF en Base64.

## 10. Panel Angular (`web/`)

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
  "Database":   { "Provider": "Postgres", "ConnectionString": "", "CheckPendingMigrationsAtStartup": true },   // Postgres | SqlServer | MySql; obligatorio, sin valor por defecto
  "Dian":       { "HabilitacionUrl": "https://vpfe-hab.dian.gov.co/WcfDianCustomerServices.svc",
                  "ProduccionUrl":   "https://vpfe.dian.gov.co/WcfDianCustomerServices.svc",
                  "HabilitacionQrUrl": "https://catalogo-vpfe-hab.dian.gov.co/document/searchqr?documentkey=",
                  "ProduccionQrUrl":   "https://catalogo-vpfe.dian.gov.co/document/searchqr?documentkey=",
                  "TimeoutSeconds": 60 },
  "Storage":    { "Provider": "Local", "Local": { "Root": "/data" },    // Local | S3 (fase 4) | AzureBlob | Gcs (fase 6)
                  "AzureBlob": { "Container": "impifact" },
                  "S3": { "Bucket": "", "Region": "", "ServiceUrl": "", "ForcePathStyle": false,
                          "ServerSideEncryption": true, "SignedUrlMaxMinutes": 60 },
                  "Gcs": { "Bucket": "" } },
  "Secrets":    { "Provider": "LocalAesGcm", "LocalAesGcm": { "CertificateDirectory": "/data/certificates" },  // MasterKeyFile: ruta del secreto montado
                  "KeyVaultUri": "", "AwsRegion": "", "GcpProject": "" },
  "Jwt":        { "Issuer": "impifact", "Audience": "impifact-web", "AccessMinutes": 15, "RefreshDays": 14, "RefreshRotationGraceSeconds": 30 },  // SigningKey (PEM) es secreto
  "ForwardedHeaders": { "KnownProxies": [], "KnownNetworks": [] },          // vacío = solo loopback
  "ApiKey":     { "Prefix": "apk_" },
  "Outbox":     { "PollSeconds": 2, "LockMinutes": 5, "MaxAttempts": 5, "BatchSize": 20, "HandlerTimeoutSeconds": 270 },
  "ScheduledJobs": { "RunAtUtc": "08:00", "RunOnStartup": true, "OutboxRetentionDays": 30, "DeleteBatchSize": 1000 },   // solo el Worker
  "Resilience": { "Retries": 3, "BaseDelaySeconds": 2, "BreakerFailures": 5, "BreakerSeconds": 30,
                  "BreakDurationSeconds": 30, "TotalSeconds": 120 },               // 2 × TotalSeconds < Outbox:HandlerTimeoutSeconds
  "Cors":       { "Origins": [ "https://app.example.com" ] },
  "RateLimit":  { "PermitPerMinutePerKey": 120, "PermitPerMinutePerIp": 300, "LoginPerMinute": 10 },
  "Pdf":        { "GotenbergUrl": "http://127.0.0.1:3000", "TimeoutSeconds": 60 },   // Worker y API (vista previa)
  "Email":      { "Host": "", "Port": 587, "Encryption": "starttls", "FromAddress": "", "FromName": "ImpiFact", "TimeoutSeconds": 60 },   // solo el Worker; Username y Password por variables de entorno o el proveedor de secretos
  "Panel":      { "BaseUrl": "" },   // solo el Worker; obligatorio: dirección pública del panel para los enlaces de los correos
  "Observability": { "LogFormat": "" },   // "json" o "text"; vacío: texto en Development y JSON en los demás (sección 12)
  "WorkerHealth":  { "Host": "127.0.0.1", "Port": 8091 }   // solo el Worker: /health/live y /health/ready; sin puerto no abre ninguno; en contenedores WorkerHealth__Host=0.0.0.0
}
```

- `Resilience` (llamadas a la DIAN): `Retries` reintentos después del primero; `BaseDelaySeconds` espera base del retroceso exponencial con jitter (cerca de 2, 4 y 8 s); `BreakerFailures` fallos dentro de `BreakerSeconds` abren el *circuit breaker*, que queda abierto `BreakDurationSeconds`; `TotalSeconds` es el plazo de una llamada con todos sus intentos y esperas. El Worker exige al arrancar que dos llamadas (un envío y la consulta GetStatus de la regla 90) quepan en el plazo de un handler: `2 × TotalSeconds < Outbox:HandlerTimeoutSeconds` (120 s y 270 s por defecto). Con 60 s por intento, una llamada que se agota alcanza a hacer dos intentos, no cuatro.
- `ScheduledJobs` (solo el Worker; sección 8): `RunAtUtc` es la hora del día en UTC, en horas y minutos, en que corren las tareas diarias (08:00 UTC son las 03:00 en Colombia); `RunOnStartup` las corre también al arrancar; `OutboxRetentionDays` (de 1 a 3650) son los días que se guardan los mensajes hechos de la bandeja de salida; `DeleteBatchSize` (de 1 a 100000) son las filas que borra cada sentencia de la limpieza. Un valor fuera de rango detiene el arranque.
- `Storage:S3` (S3 de AWS o un servicio compatible; MinIO en desarrollo, perfil `s3` de `deploy/docker-compose.yml`): `Bucket` es obligatorio y debe existir (lo crea el despliegue, no la aplicación); `Region` es obligatoria salvo que haya `ServiceUrl` (URL absoluta `http` o `https` de un servicio compatible, sin usuario, contraseña ni consulta), y en ese caso se firma con `us-east-1` si falta; `ForcePathStyle` pone el bucket en la ruta de la URL, como piden MinIO y la mayoría de los compatibles; `ServerSideEncryption` (activo por omisión) guarda cada objeto con SSE-S3 (AES-256 con llaves del servicio; en MinIO exige `MINIO_KMS_SECRET_KEY`); `SignedUrlMaxMinutes` (de 1 a 10080, 7 días) es la vigencia máxima de una URL prefirmada de descarga. Cada archivo de `document_files` guarda en `storage_provider` el proveedor donde quedó.
- Credenciales de S3: no hay llaves en `appsettings`. El cliente usa la cadena estándar de AWS: variables `AWS_ACCESS_KEY_ID` y `AWS_SECRET_ACCESS_KEY`, perfil de `~/.aws` o el rol de la instancia o de la tarea (lo recomendado en la nube, con permisos `s3:PutObject`, `s3:GetObject` y `s3:DeleteObject` solo sobre el bucket). Con MinIO local se usan esas mismas variables con el usuario de MinIO.
- `Pdf` (Worker y, desde el 18-nov, API para la vista previa; ADR-04): `GotenbergUrl` es la dirección de Gotenberg (`http://gotenberg:3000` dentro de la red de contenedores) y `TimeoutSeconds` el plazo de una conversión; en el Worker debe ser menor que `Outbox:HandlerTimeoutSeconds` (se valida al arrancar). En la API es lo que puede esperar una solicitud de vista previa antes de responder 503: conviene bajarlo (por ejemplo `Pdf__TimeoutSeconds=15`) y la API necesita llegar a Gotenberg por la red (`Pdf__GotenbergUrl`). A Gotenberg solo se le manda el HTML del documento, su hoja de estilos, la imagen del QR y el logo de la empresa; nunca secretos.
- `Panel:BaseUrl` (solo el Worker, obligatorio): dirección pública del panel con que se arman los enlaces de los correos, por ejemplo `https://panel.impifact.co` o con ruta base `https://impifact.co/panel`; el de restablecer la contraseña es `{BaseUrl}/restablecer?token=...`. Debe ser una URL absoluta `https` (`http` solo hacia `localhost` o una IP de loopback, para desarrollo), sin consulta, fragmento ni usuario; si falta o no cumple, el Worker no arranca. En despliegue va como `Panel__BaseUrl`.
- Secretos (cadena de conexión, llave de JWT, llave maestra local, credenciales SMTP por defecto) **nunca** van en `appsettings`; el arranque falla (`ValidateOnStart`) si falta alguno.
- Llave de JWT: asimétrica (RS256/ES256) con rotación; la pública se expone en `/.well-known/jwks.json` si se necesita.
- `Cors:Origins`: orígenes exactos del panel tal como los manda el navegador en `Origin` (esquema, host y puerto si no es el de siempre; sin ruta, sin barra final y nunca `*`); un valor que no cumple detiene el arranque. Esos orígenes reciben `Access-Control-Allow-Credentials: true`; con la lista vacía no se permite ninguno.
- Archivos de ejemplo `appsettings.Development.json` y `.env.example` se versionan sin secretos.

## 12. Observabilidad

Lo arma `AddImpiFactObservability` (`Infrastructure/Observability`) en la API (`impifact-api`) y en el Worker (`impifact-worker`). Construido en la fase 6.

- **Logs**: Serilog como proveedor de `Microsoft.Extensions.Logging`, así los niveles siguen saliendo de `Logging:LogLevel` (por categoría y por variables como `Logging__LogLevel__Default`). Una línea JSON por evento en la consola (`RenderedCompactJsonFormatter`: `@t`, `@m`, `@l`, `@x`, `@tr` y `@sp` de la traza, y las propiedades); en Development, texto legible de una línea (`[hh:mm:ss INF] Categoría: mensaje`), para correr con `dotnet run`. `Observability:LogFormat` (`json` o `text`) fija el formato; otro valor detiene el arranque.
  - Propiedades: `CorrelationId` (sección 9.1), `CompanyId` y `UserId` (del contexto de tenant, desde `TenantResolutionMiddleware`), `RequestPath` y `RequestId` (de ASP.NET Core) y `SourceContext` (la categoría). En el Worker, cada línea de un handler lleva `OutboxMessageId`, `OutboxMessageType` y `CompanyId`; los handlers nombran el `DocumentId` en sus mensajes.
  - Sin datos sensibles (`SensitiveDataRedactor`, una segunda barrera: el código no registra secretos): las propiedades cuyo nombre tiene `password`, `secret`, `token`, `refresh`, `session`, `authorization`, `cookie`, `apikey`, `privatekey`, `signingkey`, `masterkey`, `pfx`, `connectionstring` o la palabra `pin` quedan como `***`; en los textos se tapan `Bearer …`, `Basic …`, los JWT, las API keys `apk_…` y los valores de los parámetros con esos nombres, de una consulta (`?password=***&token=***`, también en la `QueryString` que registra ASP.NET Core) o de una cookie (`impifact_refresh=***; Path=/…; HttpOnly`, con o sin comillas, aunque el texto se registre con un nombre neutro); los valores simples sin formato fijo, como un `Uri`, se revisan por su texto (los números, fechas, Guid y booleanos quedan igual, con su tipo); el texto de las excepciones (`@x`, `{Exception}` y los atributos `exception.*` de OTLP), con sus internas y las de un `AggregateException`, pasa por la misma limpieza en todos los destinos (`ExceptionRedactingSink`, que conserva la traza de la pila, `@tr` y `@sp`; si había algo que tapar, el atributo `exception.type` de OTLP dice `RedactedException` y el tipo original queda al principio de `exception.stacktrace`); los arreglos de bytes (certificados, archivos) quedan como `[N bytes]`. Lo prueban `StructuredLoggingTests` (una solicitud con `Authorization`, cookie, consulta y propiedades con secretos) y `CredentialLoggingTests`.
- **Trazas y métricas**: OpenTelemetry, solo si está la variable estándar `OTEL_EXPORTER_OTLP_ENDPOINT` (con `OTEL_EXPORTER_OTLP_PROTOCOL`: `grpc` por defecto o `http/protobuf`); sin ella no se registra nada de OpenTelemetry y no sale nada del proceso. Exporta por OTLP las trazas y métricas de ASP.NET Core (sin las rutas `/health`), de `HttpClient` (DIAN, Gotenberg, S3), de la base y un tramo `outbox <tipo>` por cada mensaje que procesa el Worker (fuente `ImpiFact`), con sus llamadas dentro. Las trazas y métricas de la base las aporta el motor elegido con `IDatabaseProvider.ConfigureTracing` y `ConfigureMetrics`, que `AddInfrastructure` suma al proveedor de trazas y de métricas del host (Infrastructure compartido no nombra ningún controlador): en PostgreSQL la fuente y el medidor `Npgsql`, que el controlador emite por sí mismo; en SQL Server la instrumentación estable `OpenTelemetry.Instrumentation.SqlClient`, porque SqlClient no tiene fuente propia. Las dos cubren también las consultas de EF Core; no se usa `OpenTelemetry.Instrumentation.EntityFrameworkCore`, que sigue en versión preliminar. Los registros salen por el mismo OTLP (`Serilog.Sinks.OpenTelemetry`), después de quitarles los datos sensibles. Las direcciones salen sin los valores de su consulta y el SQL sin los valores de sus parámetros. Funciona con Azure Monitor, CloudWatch/X-Ray o Cloud Trace mediante el *collector*.
- **Métricas de negocio** (`ImpiFactMetrics`, medidor `ImpiFact`; también se ven con `dotnet-counters monitor --counters ImpiFact`). Los nombres son un contrato con los tableros y las alertas: no se cambian. Ninguna etiqueta lleva NIT, correos, nombres ni ids.

  | Instrumento | Tipo y unidad | Etiquetas | Qué mide |
  | --- | --- | --- | --- |
  | `impifact.documents.issued` | contador, `{document}` | `document.kind` | Documentos que la DIAN recibió por primera vez (salen de `Queued`) |
  | `impifact.documents.accepted` | contador, `{document}` | `document.kind` | Aceptados, con o sin notificaciones |
  | `impifact.documents.rejected` | contador, `{document}` | `document.kind` | Rechazados por la DIAN |
  | `impifact.dian.call.duration` | histograma, `s` | `dian.operation`, `dian.outcome` | Cada llamada a la DIAN con sus reintentos; `dian.outcome` es `answered`, `not_sent` (incluido el *circuit breaker* abierto) o `no_answer` |
  | `impifact.outbox.messages.processed` | contador, `{message}` | `outbox.message_type` | Mensajes cuyo handler terminó |
  | `impifact.outbox.messages.failed` | contador, `{message}` | `outbox.message_type`, `outbox.outcome` | Intentos fallidos: `retry` (se reintenta) o `failed` (no se reintenta más) |
  | `impifact.outbox.queue.lag` | histograma, `s` | `outbox.message_type` | Cuánto esperó un mensaje desde que debía correr hasta que el Worker lo tomó |
  | `impifact.emails.sent` y `impifact.emails.failed` | contadores, `{email}` | ninguna | Correos que el servidor aceptó o que fallaron |

  `document.kind` es `invoice` (tipos 1 a 3), `credit_note`, `debit_note` u `other`. Los documentos se cuentan después de guardar: un guardado que falla no cuenta. Pendientes: documentos en `Unconfirmed` y días al vencimiento de los certificados, que piden una lectura periódica de la base.
- **Health checks**: anónimos y sin detalles (ni descripciones ni excepciones). `/health/live` no revisa nada: 200 mientras el proceso responda. `/health/ready` revisa la base (abre una conexión), el almacenamiento (lee una clave que no existe; en S3, sin `s3:ListBucket` la respuesta es `AccessDenied` y queda como degradado) y el proveedor de secretos (con `LocalAesGcm`, que la llave maestra se pueda leer), cada uno con 5 s de plazo: si uno falla, 503. La DIAN se reporta como degradada (200), nunca caída, si en los últimos 5 minutos ese proceso hizo al menos 3 llamadas y más de la mitad no tuvo respuesta o recibió un 5xx que no es un SOAP Fault; la prueba no llama a la DIAN. Cuerpo: `{"status":"Healthy","checks":{"database":"Healthy","storage":"Healthy","secrets":"Healthy","dian":"Healthy"}}`. El Worker abre las mismas dos rutas con un Kestrel mínimo en `WorkerHealth:Host` y `WorkerHealth:Port` (127.0.0.1 y 8091 en su `appsettings.json`; sin puerto no abre ninguno; una dirección que no es IP detiene el arranque), para las sondas de Docker, Kubernetes y Container Apps. Por defecto escucha solo en loopback, para no abrir el puerto a la red local al correr en una máquina de desarrollo; los contenedores deben fijar `WorkerHealth__Host=0.0.0.0`, porque la sonda llega desde fuera del contenedor. El 8081 no se usa: es el del ApiDian de referencia de `backend/tools/golden-capture`. Su estado de la DIAN es el de sus envíos y consultas, que son casi todas las llamadas.
- **Panel local**: perfil `observability` de `deploy/docker-compose.yml` (.NET Aspire Dashboard en `http://127.0.0.1:18888`, OTLP en los puertos 4317 y 4318) con `OTEL_EXPORTER_OTLP_ENDPOINT=http://127.0.0.1:4317` en la API y el Worker.
- **Alertas sugeridas**:

  | Alerta | Condición |
  | --- | --- |
  | Cola atrasada | p95 de `impifact.outbox.queue.lag` > 300 s durante 5 min (la cola no baja) |
  | Rechazos | `impifact.documents.rejected` / (`accepted` + `rejected`) > 5 % en 15 min |
  | DIAN sin respuesta | más del 50 % de `impifact.dian.call.duration` con `dian.outcome` distinto de `answered` en 5 min, o `dian` en `Degraded` en el `/health/ready` del Worker durante 5 min (*circuit breaker* abierto) |
  | Mensajes perdidos | `impifact.outbox.messages.failed` con `outbox.outcome=failed` > 0 en 15 min |
  | Correo | `impifact.emails.failed` > 10 % de los intentos en 15 min |
  | Disponibilidad | `/health/ready` en 503 durante 2 min (API o Worker) |
  | Certificado | a menos de 15 días de vencer (cuando exista la métrica; mientras, el aviso del panel) |

## 13. Pruebas

| Proyecto | Qué verifica | Notas |
| --- | --- | --- |
| `Domain.Tests` | Reglas del agregado `Document`, transiciones, `Resolution.TakeNumber`, `DianRounding` | Rápidas, sin I/O |
| `Application.Tests` | Handlers con puertos simulados (Moq) | Caminos felices y de error |
| `Dian.Tests` | CUFE, firma, UBL contra fixtures | Sin red |
| `Golden.Tests` | JSON de entrada → XML y CUFE idénticos al PHP | Comparación byte a byte (ADR-27); fixtures en `backend/tests/golden/<tipo>/<caso>/{input.json,expected.xml,expected.cufe}` |
| `Integration.Tests` | EF + Dapper + cola con Testcontainers en PostgreSQL (fase 2) y en los 3 motores desde la fase 6 | Matriz de proveedor en CI desde la fase 6. Los hosts de prueba eligen el motor como la aplicación, con `TestDatabase` (`Database__Provider`, PostgreSQL si falta): `TestDatabase.CreateServer()` da un PostgreSQL propio por host o, en SQL Server, una base propia en un contenedor compartido por la corrida (arranca en segundos y pide unos 2 GB) o, en MySQL, un `mysql:8.4` propio por host con los datos en memoria y sin esperar al disco (sus migraciones son cientos de sentencias DDL); con MySQL: `Database__Provider=MySql dotnet test backend/tests/ImpiFact.Integration.Tests`. `EngineSqlTests` prueba bajo concurrencia el SQL propio de cada motor; las pruebas que leen el catálogo o los códigos de error del motor (`SchemaTests`, unicidad, llaves foráneas, bloqueos) lo hacen con las consultas de cada motor |
| `Contract.Tests` | Colección Postman oficial (copia limpia en el repo, sin credenciales) reproducida en orden por .NET, sin Newman ni Node: la API con `WebApplicationFactory`, PostgreSQL de Testcontainers, el Worker en el mismo proceso y la DIAN falsa. Revisa el código 2xx y las claves del primer nivel de las respuestas de ejemplo del PHP (`Category=Contract`) | Tokens propios; falla ante cambios de contrato y ante una diferencia que no esté en `KnownDeviations` (lista única, con el motivo de cada una) |
| `Architecture.Tests` | Reglas de la sección 1, aislamiento de módulos, consultas Dapper con `company_id` y sin sintaxis de un solo motor, filtros de índice y tipos de columna fuera de las configuraciones comunes, cada host con todos los motores, ausencia de secretos en logs | NetArchTest |
| Seguridad | Aislamiento entre empresas por entidad, API keys revocadas, propiedad por NIT | En `Integration.Tests` |

Procedimiento para generar fixtures golden: `backend/tools/golden-capture` levanta el PHP en Docker (solo en `127.0.0.1`) y le envía cada JSON de entrada con `"query_uuid": true`. En ese modo el PHP arma, firma y calcula el CUFE/CUDE, pero responde antes de llamar a la DIAN; no sale tráfico hacia la DIAN. Guarda el XML firmado sin el elemento `ds:Signature`, el CUFE y la respuesta, y se versionan en el repo. Fecha, hora, consecutivo, clave técnica, software y empresa son fijos, así que el resultado es determinista (comprobado el 15-nov: dos corridas dan los mismos bytes). Instrucciones en `backend/tools/golden-capture/README.md`.

Cobertura mínima exigida en CI: `Domain` y `Dian` 90 %, `Application` 80 %; los golden tests no se pueden desactivar.

## 14. Despliegue

- Imágenes: `Dockerfile.api`, `Dockerfile.worker`, `Dockerfile.web` (Nginx con el build de Angular), multi-etapa, usuario no root, sin secretos en capas.
- `docker-compose.yml` para desarrollo: Postgres, SQL Server (perfil `sqlserver`, con `MSSQL_SA_PASSWORD` en `deploy/.env`), MySQL (perfil), Azurite/MinIO para blobs, MailHog para correo, Api, Worker y web.
- CI (GitHub Actions): restaurar → compilar con `TreatWarningsAsErrors` → pruebas unitarias → integración (PostgreSQL; matriz de motores desde la fase 6) → golden → contract → análisis de vulnerabilidades → imágenes.
- **Migraciones pendientes al arrancar**: la API y el Worker revisan al arrancar, con `GetPendingMigrationsAsync` de EF Core (igual en los tres motores), que la base no tenga migraciones pendientes (`PendingMigrationsGuard`, en Infrastructure). Si falta alguna no arrancan (ningún servicio en segundo plano llega a correr): escriben una sola línea crítica, «La base de datos tiene migraciones pendientes: <nombres>. Corra el Migrator (dotnet run --project src/ImpiFact.Migrator) antes de arrancar.», y terminan con código 1. Caso real: el Worker arrancó sobre una base sin `password_reset_tokens` y Housekeeping falló con `42P01: relation "password_reset_tokens" does not exist`. Es una revisión del esquema, no de la disponibilidad: si la base no responde (error transitorio), queda una advertencia y el proceso arranca como antes, porque `/health/live` no depende de la base. `Database:CheckPendingMigrationsAtStartup` (por defecto `true`) solo se apaga en las pruebas que levantan la API sobre una base vacía y crean el esquema con el host ya arriba.
- Migraciones se aplican con `ImpiFact.Migrator migrate` como paso previo al despliegue (job de Kubernetes, tarea ECS, job de Cloud Run o contenedor de inicio en Azure Container Apps); el mismo comando sincroniza los catálogos DIAN. `ImpiFact.Migrator create-platform-key <nombre>` crea la primera API key de plataforma de una instalación nueva. Solo en desarrollo, `ImpiFact.Migrator seed-demo` crea una empresa de prueba lista para emitir en habilitación (resolución SETP de pruebas y certificado autofirmado, que la DIAN rechaza) y muestra una API key de esa empresa; fuera de Development exige `--force`.

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
| ADR-04 | Generación del PDF (representación gráfica), abierto en el PRD (17-nov) | Plantillas HTML con Handlebars.Net (MIT; escapa el HTML por defecto, porque nombres, descripciones y notas son texto del usuario) portadas de la plantilla 1 de ApiDian (factura, nota crédito y nota débito: mismo contenido, sin buscar igualdad de píxeles), convertidas a PDF por Gotenberg (contenedor aparte con Chromium, MIT; `POST /forms/chromium/convert/html`) y QR con QRCoder (MIT). El modelo imprimible (`DocumentPdfModel`) y el puerto `IPdfRenderer` viven en Application y no saben del motor. Se descartó QuestPDF: su licencia gratuita depende de los ingresos de la organización que lo usa y excluye al sector público y a las empresas que cotizan en bolsa, así que no sirve para un producto que instalan otras empresas. Gotenberg se fija a una versión exacta (`8.39.0-chromium`) en el compose y en las pruebas. Ojo: Handlebars.Net 2.4 no escapa el HTML de una parte compartida llamada sola en su línea con sangría (`    {{> parte}}`); las plantillas llaman las partes sin sangría y el cargador rechaza las que la tengan |
| ADR-09 | SOAP: cliente `WCF (System.ServiceModel) o sobre manual con HttpClient` | Spike en la fase 1b: cliente WCF (System.ServiceModel) frente a sobre SOAP manual; gana el que logre aceptación de la DIAN. Ambos van detrás de IDianClient |
| ADR-10 | Casos de uso: handlers propios con decoradores (Scrutor) | Aprobado (sin MediatR) |
| ADR-11 | Minimal API agrupada por módulo frente a controladores | Minimal API |
| ADR-12 | Autenticación dual con un solo encabezado `Bearer` y prefijo `apk_` | Aprobado, mantiene el contrato Postman |
| ADR-13 | Envío asíncrono con `?wait=true` opcional | Aprobado (refina ADR-02) |
| ADR-14 | Cliente Angular generado desde OpenAPI | Aprobado |
| ADR-08 | Proveedor de EF Core para MySQL, abierto en el PRD (19-nov) | `Microting.EntityFrameworkCore.MySql` 10.0.14, el fork de Pomelo que mantiene Microting (MIT), con `MySqlConnector` 2.6.2 (MIT) como controlador. Verificado en NuGet y GitHub el 19-nov-2026: Pomelo oficial no tiene versión para EF Core 10 (su 9.0.0 exige EF Core 9.0.x; último commit en agosto de 2025; issue #2007 y PR #2019 abiertos); el de Oracle (`MySql.EntityFrameworkCore` 10.0.12, octubre de 2026) es estable pero GPL-2.0 con la excepción FOSS, que no sirve para la licencia propia de reventa y viajaría en todas las imágenes; el fork es estable (10.0.14, octubre de 2026), activo y MIT. Conserva la API pública de Pomelo (`UseMySql`, `MySqlServerVersion`, `HasCharSet` en `Microsoft.EntityFrameworkCore`); solo cambian los espacios internos (`Microting.EntityFrameworkCore.MySql.*` en lugar de `Pomelo.EntityFrameworkCore.MySql.*`) y el ensamblado. Volver a Pomelo oficial cuando publique para EF Core 10 es cambiar el paquete y revisar esos nombres; las migraciones usan las mismas anotaciones `MySql:*`. Se fija MySQL 8.4 (LTS; la 8.0 salió de soporte en abril de 2026) como versión del servidor en lugar de detectarla |

## 16. Reglas de desarrollo

Convenciones que sigue todo el código del repositorio. Los documentos de `docs/` mandan: si el código los contradice, gana el documento; si el documento está mal, se corrige el documento antes de desviarse. La configuración local de herramientas de desarrollo no se versiona (ver `.gitignore`).

**Reglas**

- Idioma (15-nov): comentarios y documentación XML (`///`) en español neutro y profesional; identificadores, nombres de pruebas, mensajes de excepción y registros en inglés, como es convención en .NET.
- Claridad (15-nov): los comentarios explican qué hace el código y por qué, en palabras simples, para alguien que no conoce las normas de firma XML ni la DIAN. Ninguna sigla ni término técnico sin explicar la primera vez que aparece en cada archivo (por ejemplo, «canonicalización (C14N): escribir el XML de forma estándar para que el mismo contenido dé siempre los mismos bytes»); los nombres oficiales (CUFE, UBL, XAdES) se conservan y se explican.
- Ramas (16-nov): `develop` es la rama de integración y `main` solo recibe versiones. Cada cambio sale de `develop` en una rama con prefijo de Conventional Commits (`feat/`, `fix/`, `docs/`, `test/`, `refactor/`, `chore/`) y vuelve a `develop`; `main` se actualiza desde `develop` al cerrar una fase, con una etiqueta de versión.
- Escrituras (16-nov): toda fila de empresa se escribe por el `DbContext` y `SaveChanges`, que aplican la guardia por empresa, `created_at`/`updated_at` y `row_version`. No se usan `ExecuteUpdate` ni `ExecuteDelete` sobre entidades auditadas o de borrado lógico: saltan esos interceptores. Un caso de uso no llama a otro comando decorado: la transacción es una por solicitud.
- .NET 10, C# con `Nullable` y `TreatWarningsAsErrors`.
- Clean Architecture: `Domain` sin dependencias, `Application` solo con puertos y `Dian` puro. Las pruebas de arquitectura deben pasar.
- Sin MediatR ni AutoMapper. Casos de uso con `IHandler<TIn, TOut>` y decoradores; mapeo con Mapster.
- Escrituras con EF Core y lecturas con Dapper. Toda consulta incluye `company_id`.
- Portabilidad de la base (19-nov, fase 6): ningún código de un motor fuera de `ImpiFact.Infrastructure.<Motor>`. Cada motor trae un `IDatabaseProvider` con sus servicios; la API, el Worker y el Migrator registran todos los disponibles y `Database:Provider` elige uno (sin valor o con uno que no existe, no arrancan y el mensaje lista los disponibles). Lo que cambia de un motor a otro en las lecturas Dapper de Infrastructure lo da `ISqlDialect` (límite y salto de filas, `LIKE` sin mayúsculas con escape `\`, texto de una propiedad JSON, año y mes con un desfase fijo, unión lateral, literales booleanos); el resto del SQL es común: paginación por clave sin comparar filas (`col >= @v and (col > @v or id > @id)`), `group by` por expresión y nunca por posición, y nada de convertir un uuid a texto en SQL. Se eligió un dialecto con pocos trozos frente a copiar cada consulta en cada motor (diez consultas largas por triplicado) o pasarlas a LINQ (cambiaría el plan, la intercalación y el orden que fijan las pruebas). Los índices parciales se declaran con `HasPortableFilter` y los escribe el personalizador del modelo de cada motor (`PortableIndexFilters.Apply`). Una prueba de arquitectura rechaza `ilike`, `::`, `->>`, `limit`, `to_char`, `at time zone`, `||`, `true`/`false` y columnas booleanas sueltas en el SQL de Infrastructure, y las palabras reservadas de SQL Server usadas como columna o alias.
- Proveedor SQL Server (19-nov, fase 6): `ImpiFact.Infrastructure.SqlServer` (`Database:Provider` = `SqlServer`, SQL Server 2019 o posterior y Azure SQL) con su `SqlServerDatabaseProvider`, su personalizador del modelo, su `ISqlDialect`, su SQL propio y una sola migración `InitialSchema` generada del mismo modelo. Decisiones: (1) toda columna de texto lleva la intercalación `Latin1_General_100_CS_AS_SC` (distingue mayúsculas y acentos, como PostgreSQL), así un índice único, un `=` o un `join` dan lo mismo en los dos motores sin depender de la intercalación del servidor (la de fábrica de SQL Server no distingue mayúsculas); la búsqueda sin mayúsculas del dialecto pide `Latin1_General_100_CI_AS_SC`, como `ilike`. (2) JSON en `nvarchar(max)` con una restricción `ISJSON` por columna (`ck_<tabla>_<columna>_json`): SQL Server 2022 no tiene tipo JSON. (3) `uuid` como `uniqueidentifier` y los instantes como `datetimeoffset` con 7 decimales, los mismos de .NET: el valor vuelve exacto (con 3 decimales SQL Server redondea, y un instante recién guardado podía quedar después de «ahora»). (4) La migración inicial enciende `READ_COMMITTED_SNAPSHOT`: una lectura no espera a quien escribe, como en PostgreSQL. (5) Las llaves numéricas que genera la base (`plans`, `audit_logs`, `data_protection_keys`) toman su valor de una secuencia por tabla y no de `IDENTITY`: como `generated by default as identity`, aceptan el id que trae el INSERT (el alta y el guardado por id de los planes lo eligen en la aplicación). (6) SqlClient no marca `DbException.IsTransient`: `IDatabaseProvider.IsTransient` decide por el número de error qué es una falla pasajera (servidor caído, red, tiempo agotado, interbloqueo), que el arranque tolera y el almacén de credenciales responde con 503. (7) SqlClient entrega `datetimeoffset` como `DateTimeOffset`, que Dapper no convierte a `DateTime`: las lecturas piden la columna con `ISqlDialect.Instant` (`cast(col as datetime2)` en SQL Server, tal cual en los demás). (8) `percent` es palabra reservada en SQL Server: la columna va con `ISqlDialect.Identifier` (`[percent]`) y el alias es `TaxPercent`; la prueba de arquitectura rechaza `percent`, `key`, `file`, `plan`, `rule` y `user` sueltos en el SQL común. (9) SQL Server ordena un `uniqueidentifier` empezando por sus últimos 6 bytes, así que el orden de los uuid v7 en la base no es el de creación: el desempate por `id` de los listados por clave sigue siendo estable (el cursor usa el mismo orden que la consulta), pero ninguna regla puede depender de que el `id` ordene por tiempo. (10) El SQL propio está en el Modelo de datos detallado, secciones 5 y 8; la violación de índice único son los errores 2627 y 2601. Se eligió la intercalación por columna frente a cambiar la de la base (`ALTER DATABASE … COLLATE` exige la base sola y no sirve en una base que ya existe con otra).
- Proveedor MySQL (19-nov, fase 6, ADR-08): MySQL no tiene `RETURNING` ni índices parciales: su consecutivo usa `LAST_INSERT_ID(expr)` y `ROW_COUNT()` en el mismo comando, la toma del outbox es `SELECT … FOR UPDATE SKIP LOCKED` + `UPDATE` + `SELECT` en una transacción corta, el consumo del plan `INSERT … ON DUPLICATE KEY UPDATE`, el bloqueo de tareas `GET_LOCK`/`RELEASE_LOCK` (de la sesión, como el advisory lock), la llave repetida es el error 1062 y cada sesión se pone en `READ COMMITTED` al abrirse (MySQL usa `REPEATABLE READ` por defecto). Los detalles de tipos e índices están en «Modelo de datos detallado», sección 8.
- Dinero con `decimal` y `DianRounding`; nunca `float` ni `double`.
- Ningún secreto en logs, `appsettings` ni base de datos en claro.
- Migraciones por proveedor; no se editan migraciones ya aplicadas.
- Una tarea es un PR pequeño con sus pruebas. No se avanza de fase sin cumplir su puerta de salida.

**Comandos**

- `dotnet build -warnaserror`
- `dotnet test --filter Category!=Integration`
- `dotnet test --filter Category=Integration` (requiere Docker)
- `dotnet ef migrations add <Nombre> -p src/ImpiFact.Infrastructure.Postgres -s src/ImpiFact.Infrastructure.Postgres` y lo mismo con `src/ImpiFact.Infrastructure.SqlServer`: una migración por motor, generada del mismo modelo
- `Database__Provider=SqlServer dotnet test --filter Category=Integration`: la misma suite contra SQL Server (Testcontainers con `mcr.microsoft.com/mssql/server:2022-latest`)

**Cuando falte información de la DIAN:** la tarea se marca como bloqueada, se deja un TODO con el identificador [verificar] del documento y se sigue con lo que no dependa de ella.

## 17. Backlog inicial por fase

Cada tarea es un PR con pruebas. Entre paréntesis, la puerta de salida de la fase (PRD sección 12).

**Fase 0 — Documentación (aprobar PRD)**

- Resolver los [verificar] que no dependan de la DIAN: hecho el 14-nov en `docs/php-rules.md` (razón social, `state_document_id` y validación de `number`).
- Extraer las semillas de catálogos a `Seed/*.json`.
- Extraer del anexo técnico de la DIAN las reglas de validación de totales y sus tolerancias (base de `DianTotalsValidator`), con un caso de prueba por regla.
- Capturar con el PHP casos golden de nota crédito y débito, y 10 de factura (venta, descuentos, impuestos, moneda extranjera, redondeo). Hecho el 15-nov: 14 casos en `backend/tests/golden`.

**Fase 1 — Prototipo DIAN** *(1a sin DIAN: tareas 1, 2 y 4 con un certificado de prueba propio. 1b con certificado y `testSetId` reales: tarea 3 y aceptación de la DIAN)*

1. `ImpiFact.Dian`: `InvoiceUblBuilder` + `CufeCalculator` + `DianRounding` con 10 casos golden en verde (no requiere DIAN).
2. `XmlSigner` XAdES-EPES con un certificado de prueba propio; validar la firma con una librería independiente.
3. `SoapClient` WS-Security con `SendTestSetAsync`/`GetStatus` contra habilitación (requiere credenciales).
4. Consola de prueba `ImpiFact.Prototype` (proyecto temporal en `backend/tools/`, fuera de la solución final) que emite una factura de ejemplo extremo a extremo.

**Fase 2 — Base** *(pruebas de arquitectura y aislamiento en verde)*

1. Solución, proyectos, `Directory.*.props`, CI y `docker-compose`.
2. `Result`, `IHandler`, decoradores, `Error`, `IClock`, `ITenantContext`.
3. `DbContext`, convenciones `snake_case`, conversores (JSON, `DateTimeOffset`), interceptores, `UnitOfWork`.
4. Migración inicial en PostgreSQL con el modelo completo; semillas.
5. `Identity`: usuarios, roles, JWT, API keys, middleware de tenant.
6. `Company`: crear empresa, ambientes, software, resoluciones, certificado (`LocalAesGcm`).
7. `DianOptions` validado al arrancar (sección 4.3), con las URLs del servicio y del QR por ambiente (sección 6.4); las URLs del QR salen de `InvoiceUblBuilder` y entran por el modelo de entrada de `ImpiFact.Dian`.
8. Pruebas de arquitectura y de aislamiento multi-tenant.

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
5. Cierre de la referencia PHP: se borran las rutas heredadas (`/api/ubl2.1/...` y demás), que ya no aparecían en la documentación OpenAPI.

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
- **Contract tests:** en la fase 4 solo se ejecutan las peticiones de la primera entrega (carpetas 01 a 06, 09 y 10, sin documento soporte, contingencia ni lotes); en la fase 7 se suman la 07, la 08 y esas peticiones. Nunca se ejecutan `payroll`, `payroll-adjust-note`, `eqdoc`, `send-email-employee` ni las carpetas 11 a 14. La prueba escribe una tabla por carpeta con las solicitudes que pasan, las diferencias conocidas y las que quedan fuera de la fase.
- **Criterios de aceptación del modelo:** las pruebas de los tres motores se exigen en la fase 6; en la fase 2 solo PostgreSQL.

### 19.6 ADR nuevos

| ID | Decisión | Resolución |
| --- | --- | --- |
| ADR-22 | Redis y brokers | Sin ellos en la v1; outbox en BD; se reabre con los casos de la sección 19.1 |
| ADR-23 | Puertos de la DIAN | Definidos e implementados en `ImpiFact.Dian`; `Application` los consume |
| ADR-24 | Transacciones de emisión | Transacción 1 corta (consecutivo, documento, CUFE y mensaje `BuildDocumentXml`) y transacción 2 (XML, firma, estado `Queued` y mensaje `SendDocumentToDian`) |
| ADR-25 | Llaves de Data Protection | Persistidas en BD y protegidas por el proveedor de secretos |
| ADR-26 | Entrada de los generadores UBL (15-nov) | `ImpiFact.Dian` define su propio modelo de entrada con los códigos de catálogo ya resueltos y no referencia a `Domain`; `Application` hace el mapeo. Así el núcleo DIAN se prueba solo, sin base de datos ni dominio |
| ADR-28 | Organización del repositorio (15-nov) | Monorepo con una carpeta por aplicación: `backend/` (.NET), `web/` (Angular) y `mobile/` (fase 8). Se descartaron `apps/` + `libs/` estilo Nx (el backend y el panel no comparten código) y los repos separados (más trabajo para una persona y el cliente del API se genera desde el OpenAPI del backend) |
| ADR-27 | Librerías de pruebas (15-nov) | Aserciones de NUnit, sin FluentAssertions: desde la versión 8 tiene licencia comercial de pago. Los golden se comparan byte a byte con un mensaje que muestra la primera línea distinta, sin Verify |
