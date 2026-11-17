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

## Desarrollo con el Api local

El panel lee al arrancar `projects/app/public/config.json` (publicado como `/config.json`): la URL del Api, los minutos de inactividad y los indicadores de _features_. El mismo build sirve para cualquier ambiente; en un servidor se monta otro `config.json`.

```json
{
  "apiBaseUrl": "http://127.0.0.1:5080",
  "inactivity": { "warnMinutes": 25, "logoutMinutes": 30 },
  "features": {}
}
```

El de este repositorio apunta al Api de desarrollo (`http://127.0.0.1:5080`). Para iniciar sesión contra él:

1. **Sirva el panel en `127.0.0.1`, no en `localhost`**: `npm start -- --host 127.0.0.1` y abra `http://127.0.0.1:4200`. La cookie de renovación del Api es `SameSite=Strict`: el navegador solo la manda y la guarda si el panel y el Api están en el mismo sitio, y `localhost` y `127.0.0.1` son sitios distintos. Si prefiere `localhost`, cambie también `apiBaseUrl` a `http://localhost:5080`; lo que importa es usar el mismo nombre en los dos.
2. **Permita el origen del panel en el Api** (CORS con credenciales, solo orígenes exactos): por ejemplo, `Cors__Origins__0=http://127.0.0.1:4200` al arrancar el Api.

Sin el Api corriendo el panel arranca igual, sin sesión: la renovación inicial falla y queda anónimo, sin avisos de error.

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
- Componentes _standalone_ con `OnPush`, _signals_ y control flow nativo; sin NgModules ni NgRx clásico. El estado compartido va en NgRx SignalStore (`@ngrx/signals`), uno por _feature_ y los de `core`. La aplicación no usa Zone.js (_zoneless_, el valor por defecto de Angular 22).
- Todo texto visible sale de Transloco (`es-CO`); no se escriben textos fijos en las plantillas.
- Presupuestos de _bundle_ en `angular.json`: inicial ≤ 500 kB y cualquier archivo de script ≤ 250 kB; el _build_ falla al superarlos.

## Librería `core`

Se conecta en `app.config.ts` con `provideHttpClient(withFetch(), withInterceptors(coreInterceptors))`, `provideI18n()` y `provideCore()`. Expone:

| Pieza                                                                  | Uso                                                                                                                                                                                                                        |
| ---------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `provideCore()`                                                        | Lee `config.json` y fija la URL del Api antes del arranque; ya arrancado, recupera la sesión con una renovación; enciende el cierre por inactividad. Acepta `routePaths` (login, inicio, selección de empresa).            |
| `RuntimeConfigService`                                                 | `config()` (signal) e `isEnabled(feature)`.                                                                                                                                                                                |
| `SessionStore`                                                         | Sesión (SignalStore): `status`, `isAuthenticated`, `user`, `roles`, `scopes`, `companyId`, `companies`, `hasRole()`, `hasScope()`, `login()`, `logout()`. El token de acceso vive solo en memoria.                         |
| `coreInterceptors`                                                     | Correlación (`X-Correlation-Id`), errores (`AppError`), renovación ante 401 (una vez, con un solo reintento) y Bearer; las rutas `/api/v1/auth/*` van con `withCredentials`. `SKIP_ERROR_TOAST` evita el aviso automático. |
| `AppError`                                                             | Error del Api ya interpretado: `code`, `message`, `fieldErrors`, `correlationId`, `status`. Los errores por campo los muestra el formulario; el resto abre un aviso.                                                       |
| `authGuard`, `roleGuard(roles)`, `companyGuard`, `unsavedChangesGuard` | Guards funcionales. `unsavedChangesGuard` pide `hasUnsavedChanges()` a la página y pregunta con `UNSAVED_CHANGES_CONFIRM` (reemplazable).                                                                                  |
| `ToastStore`                                                           | Avisos: `show({ kind, text, correlationId?, key? })`, `dismiss(id)`. El shell los dibuja con `ui-toast-region`.                                                                                                            |
| `InactivityMonitor`                                                    | Aviso a los 25 min y cierre a los 30 (de `config.json`); la actividad de cualquier pestaña cuenta y con la pestaña oculta no corre.                                                                                        |
| `withServerList()`                                                     | Feature de SignalStore para listas paginadas en el servidor (ver abajo).                                                                                                                                                   |

**Sesión entre pestañas.** El Api rota la cookie en cada renovación y, si llegan dos a la vez con la misma cookie, responde 200 a una y 409 a la otra. Por eso la renovación se serializa: una sola en vuelo por pestaña y, entre pestañas, un candado de la Web Locks API (`navigator.locks`). La pestaña que renueva comparte el token nuevo por `BroadcastChannel`; la que esperaba el candado y ya lo recibió no renueva otra vez. El cierre de sesión también se avisa por `BroadcastChannel`. Si el navegador no tiene Web Locks, la renovación solo se serializa dentro de la pestaña y un 409 se reintenta una vez; es un respaldo de mejor esfuerzo, porque el Api toma una cookie ya rotada como reutilización y revoca todos los refresh del usuario. Un cierre de sesión espera la renovación en vuelo y revoca la cookie más nueva; esa renovación ya no revive la sesión.

**Listas paginadas en el servidor.** Un store de _feature_ usa `withServerList` con un `loader` que traduce la consulta (`cursor`, `page`, `pageSize`, `sort`, `filters`) al endpoint del cliente generado. El store nunca pagina, ordena ni filtra en el navegador; `pagination()` y `changePage()` se conectan directo a `ui-table` y `ui-paginator`:

```ts
export const DocumentsStore = signalStore(
  withServerList<DocumentSummaryResponse, { status?: string }>({
    filters: {},
    pageSize: 20,
    loader: () => {
      const api = inject(Api);
      return (query) =>
        api
          .invoke(listDocuments, {
            cursor: query.cursor ?? undefined,
            limit: query.pageSize,
            status: query.filters.status,
          })
          .pipe(map((page) => ({ items: page.items ?? [], nextCursor: page.next_cursor })));
    },
  }),
);
// En la página: store.load(); (pageChange)="store.changePage($event)" (filterChange)="store.setFilters($event)"
```

## Librería `ui`

Componentes de presentación sobre [Optimus UI](https://optimus.openng.org/) (fork MIT de PrimeNG; evaluación en [`docs/spikes/optimus-ui.md`](../docs/spikes/optimus-ui.md)). Los _features_ usan solo los componentes `ui-*`, nunca Optimus UI directamente, para poder cambiar de librería sin tocarlos. Todos reciben los textos ya traducidos por _inputs_.

| Componente                                       | Uso                                                                                                                                                                                                                |
| ------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `ui-button`                                      | Botón (`primary`, `secondary`, `danger`, `text`), con `disabled` y `loading`; emite `pressed`.                                                                                                                     |
| `ui-field` + `uiInput`                           | Etiqueta, ayuda y error alrededor de un `<input uiInput>`; enlaza `aria-describedby`, `aria-invalid` y `aria-required`.                                                                                            |
| `ui-table` + `uiTableCell`                       | Tabla paginada en el servidor: no ordena, filtra ni pagina en el navegador; emite `sortChange`, `filterChange`, `pageChange` y `retry`. Estados de carga (esqueletos), vacío y error. Cabecera fija.               |
| `ui-paginator`                                   | Paginación por cursor opaco (`kind: 'cursor'`) o con números de página cuando el Api da el total (`kind: 'pages'`).                                                                                                |
| `ui-dialog`                                      | Diálogo modal con `[(visible)]`, foco atrapado y cierre con Escape; acciones en un elemento `uiDialogFooter`.                                                                                                      |
| `ui-empty-state`, `ui-loading`, `ui-error-state` | Estados vacío, de carga (`role="status"`) y de error (`role="alert"`, con reintento).                                                                                                                              |
| `ui-toast-region`                                | Avisos en dos regiones `aria-live` (errores `assertive`, el resto `polite`), con el código de soporte copiable; emite `dismiss` y `copyReference`. Botones nativos para no cargar Optimus UI en el bundle inicial. |

- **Tema:** `provideUiTheme()` en `app.config.ts` registra el preset de Optimus UI. Los valores de diseño están solo en `projects/ui/src/styles/tokens.css`. El tema sigue al sistema; `data-theme="light"` o `"dark"` en `<html>` lo fuerza y `data-density="compact"` reduce la densidad.
- **Vitrina:** con `npm start`, `http://localhost:4200/dev/ui` muestra cada componente en cada estado. La ruta solo existe en desarrollo (`ngDevMode`); el build de producción la elimina. `e2e/ui-showcase.spec.ts` la recorre con axe en modo claro y oscuro.

## Crear un feature nuevo

1. `npx ng generate library feature-<nombre> --prefix=feat`. Después:
   - borrar el bloque `compilerOptions.paths` que la CLI agrega en `tsconfig.json` (apunta a `dist/` y tapa los alias de `tsconfig.base.json`);
   - en los `tsconfig.*.json` del feature, extender `../../tsconfig.base.json`;
   - cambiar el nombre en su `package.json` a `@impifact/feature-<nombre>` y borrar el `README.md` generado.
2. Exportar sus rutas desde `src/public-api.ts` y registrarlas en `projects/app/src/app/app.routes.ts` con `loadChildren`.
3. El alias `@impifact/feature-*` y las reglas de fronteras ya lo cubren; no hay que tocar ESLint.
