# ImpiFact

Plataforma de facturación electrónica para Colombia (DIAN): factura, notas crédito y débito, firma XAdES-EPES y envío a la DIAN. Es la reescritura en .NET 10 y Angular de ApiDian Community.

## Estructura

| Carpeta | Contenido |
| --- | --- |
| [`backend/`](backend/) | Solución .NET: núcleo DIAN, API, Worker, pruebas y herramientas |
| [`web/`](web/) | Panel Angular (fase 5) |
| [`docs/`](docs/) | PRD, arquitectura, modelo de datos, reglas del PHP y [lector HTML](docs/ImpiFact-docs.html) |

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
5. Arrancar la API (Scalar, la documentación interactiva, en `/scalar`) y el Worker desde Visual Studio o con `dotnet run`. Los correos de prueba se ven en MailHog: `http://127.0.0.1:8025`.

Los casos golden (XML y CUFE de referencia capturados del PHP) están en `backend/tests/golden`; cómo se generan se explica en [`backend/tools/golden-capture`](backend/tools/golden-capture/README.md).

## Documentación

Los documentos de `docs/` son la especificación: si el código los contradice, gana el documento. Para leerlos todos juntos, abre `docs/ImpiFact-docs.html` o regenéralo con `python docs/build_docs_html.py`.
