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

Los casos golden (XML y CUFE de referencia capturados del PHP) están en `backend/tests/golden`; cómo se generan se explica en [`backend/tools/golden-capture`](backend/tools/golden-capture/README.md).

## Documentación

Los documentos de `docs/` son la especificación: si el código los contradice, gana el documento. Para leerlos todos juntos, abre `docs/ImpiFact-docs.html` o regenéralo con `python docs/build_docs_html.py`.
