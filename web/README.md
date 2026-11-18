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
- Presupuestos de _bundle_ en `angular.json` (sección 11 de la arquitectura): inicial ≤ 500 kB y cada _feature_ perezoso ≤ 250 kB con un presupuesto `bundle` por nombre (`feature-auth`, …); el _build_ falla al superarlos.

## Librería `core`

Se conecta en `app.config.ts` con `provideHttpClient(withFetch(), withInterceptors(coreInterceptors))`, `provideI18n()` y `provideCore()`. Expone:

| Pieza                                                                                       | Uso                                                                                                                                                                                                                                                                                                                                                      |
| ------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `provideCore()`                                                                             | Lee `config.json` y fija la URL del Api antes del arranque; ya arrancado, recupera la sesión con una renovación; enciende el cierre por inactividad. Acepta `routePaths` (login, inicio, selección de empresa).                                                                                                                                          |
| `RuntimeConfigService`                                                                      | `config()` (signal) e `isEnabled(feature)`.                                                                                                                                                                                                                                                                                                              |
| `SessionStore`                                                                              | Sesión (SignalStore): `status`, `isAuthenticated`, `user`, `roles`, `scopes`, `companyId`, `companies`, `hasRole()`, `hasScope()`, `login()`, `selectCompany(id)` (renovación con `company_id`), `logout({ reason? })`. El token de acceso vive solo en memoria.                                                                                         |
| `coreInterceptors`                                                                          | Correlación (`X-Correlation-Id`), errores (`AppError`), renovación ante 401 (una vez, con un solo reintento) y Bearer; las rutas `/api/v1/auth/*` van con `withCredentials`. `SKIP_ERROR_TOAST` evita el aviso automático; `IDEMPOTENCY_KEY` en el contexto manda `Idempotency-Key`. Los errores de las descargas (`Blob`) también se leen.              |
| `AppError`                                                                                  | Error del Api ya interpretado: `code`, `message`, `fieldErrors`, `correlationId`, `status`. Los errores por campo los muestra el formulario; el resto abre un aviso.                                                                                                                                                                                     |
| `authGuard`, `roleGuard(roles)`, `scopeGuard(scope)`, `companyGuard`, `unsavedChangesGuard` | Guards funcionales; `scopeGuard` exige un alcance del token (`documents:write` para emitir). `unsavedChangesGuard` pide `hasUnsavedChanges()` a la página y pregunta con `UNSAVED_CHANGES_CONFIRM` (reemplazable).                                                                                                                                       |
| `ToastStore`                                                                                | Avisos: `show({ kind, text, correlationId?, key? })`, `dismiss(id)`. El shell los dibuja con `ui-toast-region`.                                                                                                                                                                                                                                          |
| `NotificationsStore`                                                                        | Campana de avisos (SignalStore): `unreadCount`, `latest` (los 5 últimos), `refresh()`, `markRead(aviso)` y `markAllRead()`. Consulta `listNotifications` cada 60 s mientras hay sesión con empresa activa y `documents:read`, sin avisos de error; se pausa con la pestaña oculta, se detiene al cerrar sesión y vuelve a empezar al cambiar de empresa. |
| `InactivityMonitor`                                                                         | `warning()` a los 25 min y cierre a los 30 (de `config.json`) con `/login?reason=inactivity`; la actividad de cualquier pestaña cuenta y con la pestaña oculta no corre. Con el aviso abierto solo `stayConnected()` lo cierra.                                                                                                                          |
| `MyCompaniesStore`                                                                          | Empresas del usuario con razón social, NIT y DV (`GET /api/v1/me/companies`): se cargan al iniciar sesión y se olvidan al cerrarla; `active()`, `byId(id)` y `reload()`. Un error no abre aviso.                                                                                                                                                         |
| `withServerList()`                                                                          | Feature de SignalStore para listas paginadas en el servidor (ver abajo).                                                                                                                                                                                                                                                                                 |

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
| `ui-field` + `uiSelect`                          | Lo mismo para una lista desplegable nativa `<select uiSelect>` (accesible con teclado sin cargar más código de Optimus UI).                                                                                        |
| `ui-table` + `uiTableCell`                       | Tabla paginada en el servidor: no ordena, filtra ni pagina en el navegador; emite `sortChange`, `filterChange`, `pageChange` y `retry`. Estados de carga (esqueletos), vacío y error. Cabecera fija.               |
| `ui-paginator`                                   | Paginación por cursor opaco (`kind: 'cursor'`) o con números de página cuando el Api da el total (`kind: 'pages'`).                                                                                                |
| `ui-dialog`                                      | Diálogo modal con `[(visible)]`, foco atrapado y cierre con Escape; acciones en un elemento `uiDialogFooter`.                                                                                                      |
| `ui-empty-state`, `ui-loading`, `ui-error-state` | Estados vacío, de carga (`role="status"`) y de error (`role="alert"`, con reintento).                                                                                                                              |
| `ui-toast-region`                                | Avisos en dos regiones `aria-live` (errores `assertive`, el resto `polite`), con el código de soporte copiable; emite `dismiss` y `copyReference`. Botones nativos para no cargar Optimus UI en el bundle inicial. |

- **Tema:** `provideUiTheme()` en `app.config.ts` registra el preset de Optimus UI. Los valores de diseño están solo en `projects/ui/src/styles/tokens.css`. El tema sigue al sistema; `data-theme="light"` o `"dark"` en `<html>` lo fuerza y `data-density="compact"` reduce la densidad.
- **Vitrina:** con `npm start`, `http://localhost:4200/dev/ui` muestra cada componente en cada estado. La ruta solo existe en desarrollo (`ngDevMode`); el build de producción la elimina. `e2e/ui-showcase.spec.ts` la recorre con axe en modo claro y oscuro.

## Librería `feature-auth`

Inicio de sesión, recuperación de contraseña, selección de empresa y diálogo de inactividad (tarea 5 del backlog). La aplicación monta `authRoutes` en `app.routes.ts` y el shell carga el diálogo con `loadInactivityWarning()` la primera vez que `InactivityMonitor` avisa.

| Ruta                    | Página                 | Qué hace                                                                                                                                                                                                                                            |
| ----------------------- | ---------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `/login`                | `LoginPage`            | Correo y contraseña con Signal Forms; errores por campo del `AppError`; vuelve a `returnUrl` (solo rutas internas), o pasa por la selección de empresa si hay empresas y ninguna activa, o va al inicio. Con sesión, va al inicio.                  |
| `/recuperar`            | `PasswordRecoveryPage` | Explica que la recuperación aún no está disponible (el Api no tiene endpoint) y vuelve al login.                                                                                                                                                    |
| `/empresas/seleccionar` | `CompanySelectionPage` | Lista las empresas de la sesión con su razón social y NIT (`MyCompaniesStore`, de `GET /api/v1/me/companies`) y renueva con `company_id`; una empresa inactiva sale deshabilitada. Si esa lista no carga, se numeran y se muestra el identificador. |

El diálogo de inactividad («Su sesión se cerrará por inactividad») se abre a los 25 minutos; «Seguir conectado», Escape o cerrarlo cuentan como actividad y «Cerrar sesión» la cierra. A los 30 minutos el monitor cierra la sesión y el login lo explica.

## Librería `feature-masters`

Clientes y productos de la empresa activa (tarea 7 del backlog), para el propietario y el vendedor (`MASTERS_ROLES`; `authGuard`, `companyGuard` y `roleGuard`). La aplicación monta `mastersRoutes` y el menú principal muestra «Clientes» y «Productos» solo a esos roles.

| Ruta                                 | Página             | Qué hace                                                                                                                                                                                                                                                        |
| ------------------------------------ | ------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `/clientes`, `/productos`            | `*ListPage`        | Listado con `withServerList`: página por cursor, orden y búsqueda en el Api. La búsqueda (`?q=`, tras 300 ms sin escribir) y el estado (`?estado=inactive\|all`) viven en la URL, así el enlace se comparte. Editar, desactivar (con confirmación) o reactivar. |
| `/clientes/nuevo`, `/clientes/:id`   | `CustomerFormPage` | Signal Forms con los campos del Api; el DV solo con NIT y lo valida el Api. «Buscar en la DIAN» (`lookupAcquirer`) llena nombre y correo; explica «no registrado», certificado faltante, rechazo (422) y DIAN caída (503).                                      |
| `/productos/nuevo`, `/productos/:id` | `ProductFormPage`  | Código, nombre, descripción, unidad, precio sin impuestos y el impuesto con tarifa o valor por unidad; en la edición, «Producto activo».                                                                                                                        |

Los errores por campo del Api aparecen junto a su campo; los de guardar van en el formulario (con `SKIP_ERROR_TOAST`). Salir con cambios sin guardar pide confirmación (`unsavedChangesGuard`). El Api aún no tiene endpoints de catálogos: los formularios usan listas fijas con los ids sembrados en el backend (`models/catalogs.ts`; ver la sección 16 de la arquitectura).

## Librería `feature-documents`

Lista, detalle y emisión de documentos (tarea 8 del backlog). La aplicación monta `documentsRoutes` en `app.routes.ts`; las tres rutas piden rol `owner` o `seller` y empresa activa, y `/documentos/nuevo` además el alcance `documents:write` (`scopeGuard` de `core`). El menú muestra «Documentos» según `shell/nav-items.ts`.

| Ruta                | Página               | Qué hace                                                                                                                                                                                                                   |
| ------------------- | -------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `/documentos`       | `DocumentListPage`   | `DocumentsListStore` (`withServerList`) con paginación por cursor; filtros en la URL (`desde`, `hasta`, `tipo`, `estado`, `cliente`, `numero`, `total=1`); exportación CSV; por fila: ver, XML, PDF, reenviar y regenerar. |
| `/documentos/:id`   | `DocumentDetailPage` | Encabezado, cliente, líneas, totales, respuesta de la DIAN y archivos. Consulta el estado cada 2, 4, 8, 16 y 30 s mientras esté en `Queued`, `Sent` o `Unconfirmed`; con la pestaña oculta espera y al salir se detiene.   |
| `/documentos/nuevo` | `InvoiceIssuePage`   | Factura con Signal Forms. Los totales salen de `calculateDocumentTotals` y «Emitir» manda esos valores con la `Idempotency-Key` creada al abrir el formulario (contexto `IDEMPOTENCY_KEY` de `core`).                      |

Los archivos y el CSV se piden con HttpClient (el token está solo en memoria) y se guardan desde un `Blob`. Los importes se muestran en pesos con 2 decimales (`cop`), sin operar con ellos. Pendiente en el Api: vista previa del PDF antes de emitir; en el panel: notas crédito y débito y descuentos o cargos.

## Librería `feature-companies`

Configuración de la empresa activa y asistente de paso a producción (tarea 6 del backlog), solo para el propietario (`COMPANY_ROLES`). Es una sola librería con dos chunks perezosos, `feature-companies` (las pantallas) y `feature-production-wizard` (el asistente), cada uno con su presupuesto: comparten el store `CompanySetupStore` y un feature no importa a otro, así que separarlas obligaría a duplicar el store o a llevarlo a `core`. La aplicación monta `companiesRoutes`; el menú muestra «Empresa» (`/empresa`) y «Paso a producción» (`/empresa/produccion`), que llevan a la empresa activa.

| Ruta                         | Página                 | Qué hace                                                                                                                                                                                                                                                                                                                                                              |
| ---------------------------- | ---------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `/empresas/:id`              | `CompanyLayoutPage`    | Razón social, NIT y las secciones. `:id` debe ser la empresa activa (el Api solo deja configurar la de la sesión); si es otra, pasa por la selección de empresa y vuelve. El NIT y el DV salen de `MyCompaniesStore`.                                                                                                                                                 |
| `/empresas/:id/datos`        | `CompanyDataPage`      | Datos de la empresa con Signal Forms (`updateCompany`); errores por campo del Api junto al campo. Catálogos con listas fijas (`models/catalogs.ts`), como `feature-masters`.                                                                                                                                                                                          |
| `/empresas/:id/ambientes`    | `EnvironmentsPage`     | Habilitación o producción por familia; solo se envían las que cambian y pasar a producción pide confirmación con casilla.                                                                                                                                                                                                                                             |
| `/empresas/:id/software`     | `SoftwarePage`         | Identificador, PIN (campo de contraseña, se vacía al enviar) y set de pruebas (TestSetId).                                                                                                                                                                                                                                                                            |
| `/empresas/:id/certificado`  | `CertificatePage`      | Certificados con titular, vencimiento y días restantes; carga de un `.p12`/`.pfx` en base64 con su contraseña, que se lee del campo solo al enviar y nunca pasa por un signal ni el store.                                                                                                                                                                            |
| `/empresas/:id/resoluciones` | `ResolutionsPage`      | Alta o actualización de una resolución, con el ejemplo de habilitación (SETP) y la consulta en vivo a la DIAN de los rangos autorizados (`getNumberingRanges`) para copiar uno.                                                                                                                                                                                       |
| `/empresas/:id/produccion`   | `ProductionWizardPage` | Pasos empresa → certificado → resolución → software y set de pruebas → set de pruebas → producción por familia. El certificado se consulta al Api; sin uno vigente, los dos últimos pasos quedan bloqueados. «Enviar factura de prueba» calcula los totales en el Api, emite por `issueTestSetInvoice` y sigue el estado del documento hasta la respuesta de la DIAN. |

El Api aún no tiene lectura de los datos, ambientes, software ni resoluciones de la empresa (sección 16 de la arquitectura): `CompanySetupStore` recuerda lo que devolvió cada guardado de la sesión y el asistente marca esos pasos «Sin verificar», sin bloquear. Correo (SMTP), logo y plantilla no tienen pantalla porque no hay endpoints.

## Librería `feature-notifications`

Avisos de la empresa activa (tarea 9 del backlog). La aplicación monta `notificationsRoutes` y el encabezado muestra la campana (`shell/notification-bell.component.ts`) a quien tiene `documents:read` con empresa activa.

| Pieza                            | Qué hace                                                                                                                                                                                                                             |
| -------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Campana del encabezado           | Botón con el número sin leer de `NotificationsStore` («99+» desde 100; el nombre accesible lleva el número completo). Al abrirla por primera vez carga `loadNotificationsDropdown()` y sus textos, así no pesa en el bundle inicial. |
| `NotificationsDropdownComponent` | Los últimos avisos, «Marcar como leído», «Marcar todos como leídos» y el enlace a `/notificaciones`. Escape, un clic fuera o navegar la cierran.                                                                                     |
| `/notificaciones`                | `NotificationListPage`: tabla con paginación por cursor en el Api y el filtro de sin leer en la URL (`?estado=sin-leer`). Marcar uno o todos pasa por `NotificationsStore` (el número de la campana baja enseguida).                 |

## Librería `feature-api-keys`

Llaves de API de la empresa activa en `/llaves-api` (tarea 9 del backlog), solo para el propietario (`API_KEYS_ROLES`; el Api exige `config:write`).

- **Crear:** nombre, permisos (casillas con los alcances que la sesión ya tiene, nunca `platform:*`) y vencimiento opcional (final de ese día, hora de Colombia). Un alcance que el Api rechaza (403 `api_key.scope_not_allowed`) se explica en el formulario.
- **Mostrar una vez:** la respuesta trae la llave completa; la página la muestra en un diálogo con «Copiar llave» y la borra de la memoria al cerrarlo. `ApiKeysStore` guarda solo nombre, prefijo, permisos y vencimiento.
- **Revocar:** con confirmación (`revokeApiKey`); la fila queda como «Revocada».
- **Pendiente en el Api:** no hay endpoint para listar las llaves. La tabla muestra solo las creadas en esta sesión del panel (se vacía al recargar, al cambiar de empresa o al cerrar sesión) y la página lo explica.

## Librería `feature-plans`

Plan y consumo de la empresa activa en `/plan` (tarea 9 del backlog), solo para el propietario (`PLANS_ROLES`). Lee el `plan` de `getDashboardSummary`: nombre, periodo (mensual, anual, paquete), estado, inicio y fin del periodo y, por familia de documentos, lo usado, el límite y lo que queda, ya calculados por el Api. Cada familia se dibuja con `ui-meter` (en rojo desde el 90 %, siempre con el texto «120 de 300 usados, quedan 180»); sin límite, solo el texto. Sin plan contratado muestra un estado vacío. Son datos de una sola pantalla: `rxResource` en la página, sin store.

`ui-meter` (librería `ui`) es el medidor accesible (`role="meter"` con `aria-valuetext`) que usan el plan y el tablero.

## Librería `feature-dashboard`

Tablero de `/` (tarea 9 del backlog) para el propietario y el vendedor (`DASHBOARD_ROLES`). La ruta usa `canMatch` (`dashboardMatch`): las demás personas con sesión siguen a la página de inicio de la aplicación, sin redirecciones en ciclo. Todo sale de `getDashboardSummary` (sin aviso automático de error: la página muestra el estado de error con «Reintentar»):

- **Mes actual:** total y documentos por estado; sin documentos, un enlace para emitir (con `documents:write`).
- **Últimos 12 meses:** barras de CSS (sin librería de gráficos), una sola serie con el total; el gráfico tiene un nombre accesible con el resumen y los números por familia van en una tabla desplegable.
- **Plan:** consumo por familia con `ui-meter` y fin del periodo; el propietario tiene el enlace a `/plan`.
- **Certificado:** días que le quedan (en rojo desde 30 días o vencido) o el aviso de que no hay uno activo.
- **Resoluciones por agotarse:** las que el Api marca con pocos números o por vencer.

## Librería `feature-platform`

Administración de la plataforma (tarea 9 del backlog) para el `platform_admin` (`PLATFORM_ROLES`). El Api da `platform:*` solo sin empresa activa, así que las rutas piden también ese alcance (`scopeGuard`) y el menú las oculta si falta.

| Ruta                     | Página              | Qué hace                                                                                                                                                                                                                      |
| ------------------------ | ------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `/plataforma/planes`     | `PlatformPlansPage` | Planes con su periodo, documentos por familia y estado. «Nuevo plan» y «Editar» abren un diálogo con Signal Forms que guarda con `savePlan` (`PUT /plans/{id}`: crea o actualiza); al crear se propone el siguiente id libre. |
| `/plataforma/planes/:id` | `PlanCompaniesPage` | Empresas que tienen activo el plan (`listPlanCompanies`).                                                                                                                                                                     |
| `/plataforma/empresas`   | `AssignPlanPage`    | Asigna un plan a una empresa por su NIT (`assignCompanyPlan`); explica una empresa o un plan que no existen.                                                                                                                  |

Pendiente en el Api: listar las empresas de la plataforma (hoy se escribe el NIT), paginar planes y empresas por plan (llegan completos), y alcances y endpoints para el revendedor (`reseller` no tiene alcances; `/plataforma/revendedores` no se ofrece).

## Crear un feature nuevo

1. `npx ng generate library feature-<nombre> --prefix=feat`. Después:
   - borrar el bloque `compilerOptions.paths` que la CLI agrega en `tsconfig.json` (apunta a `dist/` y tapa los alias de `tsconfig.base.json`);
   - en los `tsconfig.*.json` del feature, extender `../../tsconfig.base.json`;
   - cambiar el nombre en su `package.json` a `@impifact/feature-<nombre>` y borrar el `README.md` generado.
2. Poner todo lo perezoso del feature detrás de un solo archivo de entrada, `src/lib/feature-<nombre>.ts`, cargado con `import('./feature-<nombre>')` desde sus rutas (ver `feature-auth/src/lib/auth.routes.ts`). El chunk toma el nombre del archivo, así su presupuesto se declara por nombre en `angular.json` (`"type": "bundle", "name": "feature-<nombre>"`). `src/public-api.ts` exporta solo las rutas y cargadores: lo que exporte de forma estática entra en el bundle inicial.
3. Registrar las rutas en `projects/app/src/app/app.routes.ts`.
4. Textos: en `src/i18n/es-CO.json` del feature, como _scope_ de Transloco (`provideTranslocoScope('<nombre>')` en sus componentes y claves con el prefijo `<nombre>.`), publicados por la aplicación con una entrada de `assets` en `angular.json` (`"output": "i18n/<nombre>"`). Un _resolver_ los carga antes de abrir la página.
5. El alias `@impifact/feature-*` y las reglas de fronteras ya lo cubren; no hay que tocar ESLint. Agregar `ng build feature-<nombre>` a `build:libs`.
