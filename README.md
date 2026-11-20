# ImpiFact

Plataforma de facturación electrónica para Colombia (DIAN): factura, notas crédito y débito, firma XAdES-EPES y envío a la DIAN, con un backend en .NET 10 y un panel en Angular.

## Estructura

| Carpeta | Contenido |
| --- | --- |
| [`backend/`](backend/) | Solución .NET: núcleo DIAN, API, Worker, pruebas y herramientas |
| [`web/`](web/) | Panel Angular (fase 5) |
| [`docs/`](docs/) | PRD, arquitectura, modelo de datos, reglas de negocio y [lector HTML](docs/ImpiFact-docs.html) |

La app móvil llegará en la fase 8, en `mobile/`.

## Backend

Requiere el SDK de .NET 10. Desde `backend/`:

```sh
dotnet build -warnaserror
dotnet test
```

### Correr en local

Nada de esto se versiona: cada equipo arma sus propios archivos.

1. `deploy/.env` con `POSTGRES_PASSWORD=<contraseña propia>` (y `POSTGRES_PORT=5433` si ya hay un PostgreSQL en el 5432). Luego, desde la raíz: `docker compose -f deploy/docker-compose.yml up -d postgres gotenberg mailhog`.
2. Carpeta `.local/` en la raíz con `master.key` (32 bytes al azar en base64, por ejemplo `openssl rand -base64 32`), `certificates/` y `storage/`. La llave maestra cifra los certificados de las empresas: si se pierde, hay que volver a subirlos.
3. `Properties/launchSettings.json` en `ImpiFact.Api`, `ImpiFact.Worker` y `ImpiFact.Migrator` con las variables de entorno: `Database__ConnectionString`, `Secrets__LocalAesGcm__MasterKeyFile`, `Secrets__LocalAesGcm__CertificateDirectory` y `Storage__Local__Root` (rutas a `.local/`); el Worker además lleva `Email__Host=127.0.0.1`, `Email__Port=1025`, `Email__Encryption=none` y `Email__FromAddress`.
4. Correr una vez el Migrator (aplica las migraciones y los catálogos) y crear la primera llave de plataforma con `dotnet run --project src/ImpiFact.Migrator -- create-platform-key <nombre>`; se muestra una sola vez.
5. Opcional, para probar desde Scalar: `dotnet run --project src/ImpiFact.Migrator -- seed-demo` crea (o actualiza) la empresa de prueba NIT 900123456-8 lista para emitir en habilitación, con software, resoluciones SETP/NC/ND y un certificado autofirmado (la DIAN lo rechaza, sirve solo en local), y muestra una API key nueva en cada corrida. Pide `DOTNET_ENVIRONMENT=Development` (o `--force`) y las mismas variables `Secrets__LocalAesGcm__*` que la API.
6. Arrancar la API (Scalar, la documentación interactiva, en `/scalar`) y el Worker desde Visual Studio o con `dotnet run`. Los correos de prueba se ven en MailHog: `http://127.0.0.1:8025`.
7. Opcional, para ver registros, trazas y métricas: `docker compose -f deploy/docker-compose.yml --profile observability up -d aspire-dashboard` y una sola variable de entorno en la API y el Worker, `OTEL_EXPORTER_OTLP_ENDPOINT=http://127.0.0.1:4317`. El panel queda en `http://127.0.0.1:18888`; el enlace con su llave de acceso sale en `docker compose -f deploy/docker-compose.yml logs aspire-dashboard`. Sin la variable no se exporta nada.
8. Opcional, para probar los proveedores de nube sin cuentas ni costo: cada uno tiene su emulador en un perfil de `deploy/docker-compose.yml` (`azurite`, `gcs`, `keyvault`, `aws` y `gcp`). Se eligen con `Storage__Provider` (`Local`, `S3`, `AzureBlob` o `Gcs`) y `Secrets__Provider` (`LocalAesGcm`, `KeyVault`, `AwsSecrets` o `GcpSecrets`), sin llaves en la configuración: en la nube entran con la identidad de la carga de trabajo. Las variables de cada proveedor y de cada emulador están en las secciones 11 y 14 de `docs/Arquitectura detallada.md`. Cambiar `Secrets__Provider` deja ilegibles los certificados y los secretos guardados con el anterior.

Salud de los procesos: la API responde `/health/live` (el proceso está arriba) y `/health/ready` (base de datos, almacenamiento y secretos; la DIAN solo como degradada). El Worker responde las mismas dos rutas en `WorkerHealth:Host` y `WorkerHealth:Port` (127.0.0.1:8091 por defecto; en contenedores, `WorkerHealth__Host=0.0.0.0`). Las métricas y alertas sugeridas están en la sección 12 de `docs/Arquitectura detallada.md`.

Los casos golden (XML y CUFE de referencia) están en `backend/tests/golden`; cómo se generan se explica en [`backend/tools/golden-capture`](backend/tools/golden-capture/README.md).

## Documentación

Los documentos de `docs/` son la especificación: si el código los contradice, gana el documento. Para leerlos todos juntos, abre `docs/ImpiFact-docs.html` o regenéralo con `python docs/build_docs_html.py`.
