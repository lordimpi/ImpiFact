# web

Panel de administración de ImpiFact en Angular (fase 5).

La especificación está en [`docs/Arquitectura frontend.md`](../docs/Arquitectura%20frontend.md): workspace de Angular CLI con una librería por _feature_, cliente del API generado desde el OpenAPI del backend y sin cálculos fiscales en el navegador.

## Requisitos

- Node.js 24 (LTS). La versión está fijada en [`.nvmrc`](.nvmrc); con nvm basta `nvm use`.
- npm 11 (viene con Node 24).

## Instalación y uso

Todos los comandos se ejecutan dentro de `web/`.

```bash
npm ci                 # instala las dependencias exactas de package-lock.json
npm start              # servidor de desarrollo en http://localhost:4200
npm run lint           # ESLint (incluye fronteras entre librerías) y formato con Prettier
npm test               # pruebas unitarias con Vitest, una sola pasada
npm run build          # compilación de producción en dist/app; falla si se superan los presupuestos
npm run e2e            # pruebas E2E con Playwright (levanta ng serve si no está corriendo)
npm run api:generate   # regenera openapi.json y el cliente de API desde el backend (necesita el SDK de .NET)
npm run api:check      # lo mismo y falla si quedan diferencias sin commitear (lo corre el CI)
```

Antes de la primera ejecución de `npm run e2e` hay que descargar el navegador: `npx playwright install chromium`.

Otros comandos: `npm run format` corrige el formato con Prettier y `npm run build:libs` compila las librerías por separado con ng-packagr.

## Estructura

| Carpeta              | Contenido                                                                                                   |
| -------------------- | ----------------------------------------------------------------------------------------------------------- |
| `projects/app`       | Aplicación: arranque, rutas raíz y shell (encabezado, menú, contenido). Textos en `public/i18n/es-CO.json`. |
| `projects/core`      | Sesión, empresa activa, interceptores, guards, configuración, errores e i18n.                               |
| `projects/ui`        | Componentes de presentación sin lógica de negocio y los design tokens (`src/styles/tokens.css`).            |
| `projects/api`       | Cliente generado desde el OpenAPI. No se edita a mano.                                                      |
| `projects/feature-*` | Una librería por _feature_ (`feature-auth`, `feature-documents`, …). Se crean con su tarea del backlog.     |
| `e2e`                | Pruebas E2E con Playwright.                                                                                 |

Las librerías se importan por alias: `@impifact/core`, `@impifact/ui`, `@impifact/api` y `@impifact/feature-<nombre>` (definidos en `tsconfig.base.json`).

## Cliente de API

`projects/api` se genera desde el OpenAPI (la descripción estándar de la API HTTP) del backend, en dos pasos:

1. `npm run api:export`: la herramienta `backend/tools/openapi-export` levanta la API en memoria, sin servidor ni base de datos, y escribe `projects/api/openapi.json`. Necesita el SDK de .NET de `backend/global.json`.
2. `npm run api:client`: [ng-openapi-gen](https://github.com/cyclosproject/ng-openapi-gen) (versión fijada en `package.json`, configuración en `projects/api/ng-openapi-gen.json`) escribe `projects/api/src/generated`.

`npm run api:generate` hace los dos. Lo generado y `openapi.json` se commitean y no se editan a mano (ESLint y Prettier no los revisan). `npm run api:check` regenera y falla si algo cambió; el CI del panel lo corre, y el del backend verifica que `openapi.json` esté al día con la API.

Cada operación es una función con el nombre de su `operationId` (`createCompany`, `issueInvoice`, `listDocuments`…), que se llama con el servicio `Api` sobre `HttpClient`, así pasa por los interceptores:

```ts
const api = inject(Api);
api.invoke(createCompany, { nit, dv, body }); // Observable<CompanySavedResponse>
```

La URL base sale de `ApiConfiguration.rootUrl` (vacía por defecto: rutas relativas). `core` la fija al arrancar con `provideApiRootUrl(() => ...)`, que acepta una promesa (la configuración de ejecución). `login` y `refreshSession` devuelven `SessionTokens | PanelSessionTokens`; con `X-Client: panel` llega la segunda (sin `refresh_token`, que va en la cookie) y la unión se puede asignar directamente a `PanelSessionTokens`.

## Reglas

- Fronteras entre librerías (sección 2 de la arquitectura), verificadas por `npm run lint` con `eslint-plugin-boundaries`: `app` importa todo; un `feature-*` importa `core`, `ui` y `api`, nunca otro `feature-*`; `core` solo importa `api`; `ui` y `api` no importan nada del workspace.
- Componentes _standalone_ con `OnPush`, _signals_ y control flow nativo; sin NgModules ni NgRx. La aplicación no usa Zone.js (_zoneless_, el valor por defecto de Angular 22).
- Todo texto visible sale de Transloco (`es-CO`); no se escriben textos fijos en las plantillas.
- Presupuestos de _bundle_ en `angular.json`: inicial ≤ 500 kB y cualquier archivo de script ≤ 250 kB; el _build_ falla al superarlos.

## Librería `ui`

Componentes de presentación sobre [Optimus UI](https://optimus.openng.org/) (fork MIT de PrimeNG; evaluación en [`docs/spikes/optimus-ui.md`](../docs/spikes/optimus-ui.md)). Los _features_ usan solo los componentes `ui-*`, nunca Optimus UI directamente, para poder cambiar de librería sin tocarlos. Todos reciben los textos ya traducidos por _inputs_.

| Componente                                       | Uso                                                                                                                                                                                                  |
| ------------------------------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `ui-button`                                      | Botón (`primary`, `secondary`, `danger`, `text`), con `disabled` y `loading`; emite `pressed`.                                                                                                       |
| `ui-field` + `uiInput`                           | Etiqueta, ayuda y error alrededor de un `<input uiInput>`; enlaza `aria-describedby`, `aria-invalid` y `aria-required`.                                                                              |
| `ui-table` + `uiTableCell`                       | Tabla paginada en el servidor: no ordena, filtra ni pagina en el navegador; emite `sortChange`, `filterChange`, `pageChange` y `retry`. Estados de carga (esqueletos), vacío y error. Cabecera fija. |
| `ui-paginator`                                   | Paginación por cursor opaco (`kind: 'cursor'`) o con números de página cuando el Api da el total (`kind: 'pages'`).                                                                                  |
| `ui-dialog`                                      | Diálogo modal con `[(visible)]`, foco atrapado y cierre con Escape; acciones en un elemento `uiDialogFooter`.                                                                                        |
| `ui-empty-state`, `ui-loading`, `ui-error-state` | Estados vacío, de carga (`role="status"`) y de error (`role="alert"`, con reintento).                                                                                                                |

- **Tema:** `provideUiTheme()` en `app.config.ts` registra el preset de Optimus UI. Los valores de diseño están solo en `projects/ui/src/styles/tokens.css`. El tema sigue al sistema; `data-theme="light"` o `"dark"` en `<html>` lo fuerza y `data-density="compact"` reduce la densidad.
- **Vitrina:** con `npm start`, `http://localhost:4200/dev/ui` muestra cada componente en cada estado. La ruta solo existe en desarrollo (`ngDevMode`); el build de producción la elimina. `e2e/ui-showcase.spec.ts` la recorre con axe en modo claro y oscuro.

## Crear un feature nuevo

1. `npx ng generate library feature-<nombre> --prefix=feat`. Después:
   - borrar el bloque `compilerOptions.paths` que la CLI agrega en `tsconfig.json` (apunta a `dist/` y tapa los alias de `tsconfig.base.json`);
   - en los `tsconfig.*.json` del feature, extender `../../tsconfig.base.json`;
   - cambiar el nombre en su `package.json` a `@impifact/feature-<nombre>` y borrar el `README.md` generado.
2. Exportar sus rutas desde `src/public-api.ts` y registrarlas en `projects/app/src/app/app.routes.ts` con `loadChildren`.
3. El alias `@impifact/feature-*` y las reglas de fronteras ya lo cubren; no hay que tocar ESLint.
