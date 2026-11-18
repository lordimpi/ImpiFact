# Arquitectura del frontend (Angular)

Especificación de implementación del panel `web/`. Complementa la sección 10 de la pestaña «Arquitectura detallada», que queda como resumen; ante cualquier diferencia manda este documento. Las versiones exactas de Angular y de las librerías se fijan al crear el proyecto con la última versión estable y se registran en `package.json`.

## 1. ¿Microfrontends? No

**Decisión (ADR-15): un solo panel Angular, modular por *features*, sin microfrontends.**

Por qué:

1. Hay un solo desarrollador y un solo despliegue. Los microfrontends resuelven un problema de **equipos independientes que despliegan por separado**; aquí solo agregarían Module Federation, versiones compartidas de Angular y un *shell* extra por mantener.
2. Los módulos del panel comparten sesión, empresa activa, permisos y navegación. Separarlos obliga a sincronizar ese estado entre aplicaciones.
3. El backend ya es un monolito modular; un frontend distribuido frente a un backend único no aporta aislamiento real.
4. Rendimiento: la carga perezosa de rutas ya da lo que se busca (el usuario descarga solo lo que usa).

Qué se hace en su lugar, para poder separar después sin reescribir: cada *feature* vive en su propia **librería del workspace** con fronteras de importación verificadas por lint. Si Enterprise crece con equipos propios (nómina, POS, RIPS), esa librería se extrae como remoto de Module Federation o como app aparte. Criterio para reabrir la decisión: más de dos equipos tocando el panel, o módulos que deban desplegarse con ritmo distinto.

## 2. Workspace

Angular CLI con librerías (sin Nx por ahora; migrar a Nx si el número de librerías lo justifica).

```
web/
├─ projects/
│  ├─ app/                       aplicación: bootstrap, rutas raíz, shell (layout, menú)
│  ├─ core/                      lib: auth, sesión, empresa activa, interceptores, guards, config, errores
│  ├─ ui/                        lib: componentes de presentación sin lógica de negocio
│  ├─ api/                       lib: cliente generado desde OpenAPI (no se edita a mano)
│  ├─ feature-auth/
│  ├─ feature-companies/         configuración de la empresa y asistente de paso a producción (dos chunks)
│  ├─ feature-documents/
│  ├─ feature-masters/           clientes y productos
│  ├─ feature-api-keys/
│  ├─ feature-plans/
│  ├─ feature-platform/          reseller y administrador de plataforma
│  └─ feature-notifications/
├─ e2e/                          Playwright
├─ eslint.config.js  tsconfig.base.json  package.json
```

Reglas de dependencia (se hacen cumplir con `eslint-plugin-boundaries` en `web/eslint.config.js`; un *feature* nuevo queda cubierto por el patrón `projects/feature-*` sin tocar la configuración):

| Desde | Puede importar | No puede importar |
| --- | --- | --- |
| `app` | todo | — |
| `feature-*` | `core`, `ui`, `api` | otro `feature-*` (se comunican por rutas o por servicios de `core`) |
| `ui` | nada del workspace | `core`, `api`, `feature-*` |
| `core` | `api` | `ui`, `feature-*` |
| `api` | nada | todo lo demás |

## 3. Capas dentro de cada *feature*

```
feature-documents/src/lib/
├─ pages/            componentes de ruta ("smart"): leen estado, orquestan, navegan
├─ components/       componentes de presentación del feature ("dumb"): inputs/outputs, sin servicios
├─ state/            servicios con signals (store del feature)
├─ forms/            builders de formularios tipados y validadores
├─ models/           tipos propios del feature (solo si no los da `api`)
└─ documents.routes.ts   rutas perezosas del feature (export default)
```

- Componentes *standalone* con `ChangeDetectionStrategy.OnPush`; `inject()` en vez de constructores; entradas y salidas con las funciones de *signals* (`input()`, `output()`, `model()`).
- Plantillas con *control flow* nativo (`@if`, `@for` con `track`, `@switch`) y `@defer` para bloques pesados (visor de PDF, gráficos).
- Detección de cambios sin Zone.js (*zoneless*) si la versión de Angular lo marca estable; si no, `OnPush` + signals.
- Un archivo por componente, nombres `kebab-case` con sufijo de rol (`document-list.page.ts`, `tax-summary.component.ts`).

## 4. Estado

- **Estado compartido de un feature** (listas con filtros, detalle que otras partes leen): un store de **NgRx SignalStore** (`@ngrx/signals`) por feature, en `state/`, con `withState` (datos, `loading`, `error`), `withComputed` (lo derivado) y `withMethods` (las únicas formas de cambiarlo, que llaman al cliente generado). Las listas paginadas usan el *feature* reutilizable de SignalStore de `core`, `withServerList`, que guarda solo la página visible en un arreglo `items` junto con la paginación, el orden, los filtros y los estados de carga; `withEntities` queda para colecciones que se editan por id en el cliente sin volver a pedir la página (por ejemplo, las líneas de un documento en edición). Refresco explícito tras mutaciones.
- **Datos de una sola pantalla** que nadie más comparte: `httpResource()`/`resource()` directamente en el componente, sin store.
- **Estado de UI** local: signals dentro del componente.
- **Estado global mínimo** en `core`: sesión (usuario, roles), empresa activa, preferencias, notificaciones.
- No se usa el NgRx clásico (acciones, *reducers*, *effects* y selectores): SignalStore da el mismo orden con mucho menos código (ADR-16).
- **Paginación siempre en el servidor:** toda lista pide al Api solo la página visible, con orden y filtros aplicados allí; el cliente nunca descarga la lista completa.
- Persistencia en el navegador: solo preferencias no sensibles (tema, densidad, última empresa) en `localStorage`. Nada de tokens ni datos fiscales.

## 5. Cliente de API

- Se **genera** desde el OpenAPI que publica el Api con un generador que produzca servicios de Angular sobre `HttpClient` (`ng-openapi-gen` u OpenAPI Generator `typescript-angular`), para que pasen por los interceptores. Se descarta `openapi-fetch` por saltarse `HttpClient`.
- Generador: `ng-openapi-gen` (versión fijada), con una función por operación, nombrada por su `operationId`, y el servicio `Api` que la ejecuta sobre `HttpClient` (`api.invoke(createCompany, { nit, dv, body })`); sin NgModule y con Observables. La URL base es `ApiConfiguration.rootUrl`, que `core` fija al arrancar con `provideApiRootUrl`.
- Script `npm run api:generate` exporta `openapi.json` con `backend/tools/openapi-export` (la API en memoria, sin servidor ni base de datos), regenera `projects/api/src/generated` y ambos se commitean. `npm run api:check` regenera y falla en CI si hay diferencias sin commitear (el contrato no puede divergir en silencio); el CI del backend verifica además que `openapi.json` esté al día con la API.
- Nombres con typos heredados del contrato JSON (`integral_salarary`, `seze`) se conservan en los tipos generados; los formularios los mapean a nombres limpios en `forms/`.
- Manejo de errores centralizado: un interceptor convierte `ProblemDetails` en un `AppError` con `code`, `message`, `fieldErrors` y `correlationId`; los formularios muestran `fieldErrors` junto al campo y el resto sale en un *toast* con el `correlationId` copiable.

## 6. Autenticación y seguridad en el cliente

| Tema | Decisión |
| --- | --- |
| Token de acceso | JWT de 15 min, **solo en memoria** (un signal en `core`), nunca en `localStorage` |
| Refresh | Cookie `impifact_refresh` (`HttpOnly; Secure; SameSite=Strict; Path=/api/v1/auth`) emitida por el Api cuando la solicitud trae `X-Client: panel`; el cliente llama a `/auth/login`, `/auth/refresh` y `/auth/logout` con ese encabezado y `withCredentials`, al arrancar y ante un 401. El cuerpo no trae `refresh_token`; sin cookie, `/auth/refresh` responde 401 (no hay sesión) |
| Interceptores | `authInterceptor` (adjunta Bearer), `refreshInterceptor` (un solo refresh en vuelo; reintenta una vez; un segundo 401 cierra la sesión), `errorInterceptor`, `correlationInterceptor` (`X-Correlation-Id`) |
| Refresh entre pestañas | Dos refresh simultáneos con la misma cookie dan 200 y 409, así que se serializan también entre pestañas con la Web Locks API (`navigator.locks`); la pestaña que renueva comparte el token nuevo por `BroadcastChannel` y la que esperaba el candado ya no renueva. Sin Web Locks, solo dentro de la pestaña y un 409 se reintenta una vez: es un respaldo de mejor esfuerzo, porque si dos pestañas renuevan a la vez y una llega con la cookie ya rotada, el Api lo toma como reutilización del token y revoca **todos** los refresh del usuario (cierra la sesión en todos sus dispositivos). Una renovación en vuelo nunca revive una sesión cerrada: el cierre la espera y revoca la cookie más nueva |
| Guards | `authGuard` (sesión), `roleGuard(roles)`, `companyGuard` (empresa activa elegida), `unsavedChangesGuard` |
| Permisos en UI | Se leen de los claims y ocultan o deshabilitan acciones; **el backend es la autoridad**, nunca se confía en el cliente |
| XSS | Sin `innerHTML` ni `bypassSecurityTrust*` salvo en un wrapper revisado (`SafeHtml` solo para el PDF/XML renderizado); plantillas Angular escapan por defecto |
| CSP | Cabeceras en el servidor estático: `default-src 'self'`, sin `unsafe-inline` (estilos con *nonce* o hash), `frame-ancestors 'none'`, `connect-src` solo el Api |
| Secretos | Ninguno en el bundle. API keys se muestran una vez con botón de copiar y se borran del estado al cerrar el diálogo |
| Certificado `.pfx` | Se sube directo al Api en base64 dentro del JSON (`uploadCompanyCertificate`, lo que acepta el Api); la clave se escribe en un campo `type=password`, se lee solo al enviar, no pasa por signals, stores ni logs del cliente y el campo se vacía después de cada envío |
| Cierre de sesión | Llama a `/auth/logout` (revoca el refresh), limpia estado y redirige; sincroniza entre pestañas con `BroadcastChannel` |
| Inactividad | Aviso a los 25 min y cierre a los 30 (configurable en `config.json`); la actividad de cualquier pestaña cuenta para todas; con la pestaña oculta no corre el temporizador y al volver se compara el tiempo transcurrido |

Cambio en el backend, hecho el 17-nov (fase 5, B1): el refresh acepta y emite la cookie `HttpOnly`, existe `/auth/logout` y CORS permite credenciales para los orígenes de `Cors:Origins`. La cookie solo se acepta con `X-Client: panel` (defensa contra CSRF junto con `SameSite=Strict`), así que el panel y la API deben estar en el mismo sitio (mismo dominio registrable) y, en desarrollo, con el mismo nombre de host (`localhost` o `127.0.0.1`, sin mezclarlos). Detalle en «Arquitectura detallada», sección 9.2.

## 7. Mapa de rutas y roles

| Ruta | Feature | Roles |
| --- | --- | --- |
| `/login`, `/recuperar` | `feature-auth` | público |
| `/empresas/seleccionar` | `feature-auth` | todos los autenticados |
| `/` (tablero) | `feature-dashboard` (los demás roles ven la página de inicio de `app`) | owner, seller |
| `/empresas/:id/{datos,ambientes,software,certificado,resoluciones}` (`/empresa` lleva a la activa); `correo`, `logo` y `plantilla` esperan su endpoint | `feature-companies` | owner |
| `/empresas/:id/produccion` (`/empresa/produccion`) | `feature-companies`, chunk `feature-production-wizard` | owner |
| `/documentos`, `/documentos/nuevo`, `/documentos/:id` | `feature-documents` | owner, seller (`nuevo`: con alcance de emisión) |
| `/clientes`, `/productos` | `feature-masters` | owner, seller |
| `/llaves-api` | `feature-api-keys` | owner |
| `/plan` | `feature-plans` | owner |
| `/plataforma/{empresas,planes,revendedores}` | `feature-platform` (hoy `planes`, `planes/:id` y `empresas`) | platform\_admin con `platform:*` (reseller cuando el Api le dé alcances) |
| `/notificaciones` | `feature-notifications` | todos |
| `/portal` (documentos del cliente) | `feature-documents` (vista restringida) | customer |

Todas las rutas de feature son perezosas (`loadChildren`). El menú lateral se construye desde un arreglo de configuración filtrado por rol.

## 8. Pantallas críticas

### 8.1 Emisión de documento (`/documentos/nuevo`)

Es la pantalla más compleja y no puede duplicar el redondeo del backend.

- Formulario tipado con secciones: tipo de documento, cliente (búsqueda o alta rápida), pago, líneas (tabla editable), descuentos/cargos, notas.
- Los totales e impuestos **no se calculan en el cliente**: el panel llama a un endpoint de cálculo del Api (`POST /api/v1/documents/calculate`, que usa `DianRounding`) y muestra el resultado; el envío final reutiliza esos valores. Así el CUFE no depende de aritmética de JavaScript. Es una tarea nueva del backend (fase 3).
- Botón «Vista previa» (`preeliminar-view`) abre el PDF sin consumir consecutivo; «Emitir» envía con `Idempotency-Key` generado al abrir el formulario.
- Tras emitir, la pantalla de detalle sigue el estado (sección 9).

Hecho en la fase 5 (tarea 8, `feature-documents`): factura con cliente (búsqueda en `listCustomers` y enlace a `/clientes/nuevo`), forma y medio de pago, líneas desde un producto (`listProducts`) o libres con IVA del 19, 5 o 0 %, y notas. Cada cambio en las líneas pide `calculateDocumentTotals` tras una pausa corta; «Emitir» solo se habilita con los totales del servidor al día y el cuerpo de `issueInvoice` lleva esos mismos valores (totales, impuestos y valor de cada línea). El cliente generado no expone el encabezado `Idempotency-Key`: la página pone la llave en el contexto de la solicitud (`IDEMPOTENCY_KEY` de `core`) y `idempotencyInterceptor` la manda. «Vista previa» (F8b, 18-nov) manda el mismo cuerpo a `previewInvoice` y abre el PDF en una pestaña que se abre en el clic (evita el bloqueo de ventanas emergentes); un 503 (`pdf.unavailable`) muestra un aviso claro. Notas crédito y débito (F8b, 18-nov) en rutas propias, `/documentos/nota-credito` y `/documentos/nota-debito` (su formulario es otro: factura de referencia y concepto, sin pago; cada una tiene su enlace y el detalle de una factura aceptada las abre con `?factura=<id>`): la factura se busca entre las aceptadas de la empresa, el concepto sale del catálogo de discrepancias, los totales de `calculateDocumentTotals` y la emisión lleva `Idempotency-Key`; el Api rechaza una factura no aceptada (`document.reference_not_accepted`) y la página muestra el error. Pendiente: los descuentos o cargos y la vista previa de las notas (el Api la está construyendo).

### 8.2 Asistente de paso a producción

Pasos con estado persistido en el servidor (empresa → certificado → resolución → set de pruebas → producción por producto). Cada paso valida con el Api antes de habilitar el siguiente; un paso completado puede reabrirse. Mensajes de error de la DIAN se muestran con su explicación en español.

### 8.3 Listado de documentos

Paginación y orden en el servidor (por clave `issued_at, id`), filtros por fecha, tipo, estado y cliente guardados en la URL (compartibles), exportación CSV desde el Api, acciones por fila: ver, XML, PDF, reenviar correo, regenerar PDF, enviar evento.

Backend listo (fase 5, B5): `listDocuments` acepta `kind` (`invoice`, `credit_note`, `debit_note`), `status` (uno o varios, `status=Accepted,Rejected`), `from`/`to`, `customer` (texto en la identificación o el nombre), `number` (con prefijo o solo dígitos), `cursor` y `limit` (el panel pide hasta 100); cada fila trae `kind` y `email_status`. `include_total=true` agrega `total_count` para mostrar páginas numeradas, con una consulta más: pedirlo solo en la primera página o al cambiar filtros, y avanzar siempre con `next_cursor`. `exportDocumentsCsv` (`GET /api/v1/documents/export.csv`) recibe los mismos filtros; como el token vive solo en memoria, el panel lo pide con su cliente HTTP (no con un enlace) y guarda el archivo recibido. Responde 422 `documents.export_too_large` sobre 50.000 filas: el panel muestra el mensaje y sugiere acotar las fechas.

Panel listo (fase 5, tarea 8): los filtros viven en la URL con nombres en español (`desde`, `hasta`, `tipo`, `estado=Queued,Sent`, `cliente`, `numero` y `total=1` para pedir el conteo), así una búsqueda se comparte o se recarga. El conteo es informativo (`withServerList` lo guarda en `totalCount` sin dejar de paginar por cursor). La lista no ofrece orden por columnas porque el Api ordena siempre por `issued_at, id`. Las descargas, el CSV y los errores que llegan como archivo pasan por HttpClient: `errorInterceptor` lee el ProblemDetails del `Blob`.

## 9. Estado en tiempo real

- Detalle de documento: *polling* con retroceso (2 s, 4 s, 8 s hasta 30 s) mientras el estado sea `Queued`, `Sent` o `Unconfirmed`; se detiene con un estado final o al salir de la pantalla.
- Notificaciones (campana): *polling* cada 60 s con `NotificationsStore` de `core` (hecho en la tarea 9: solo con empresa activa y `documents:read`, se pausa con la pestaña oculta y se detiene al cerrar sesión). Si se requiere inmediatez se agrega SSE en el Api (`/api/v1/events`) sin cambiar los componentes, porque ambos alimentan el mismo servicio de `core`.
- Todo *polling* se cancela con `takeUntilDestroyed` y se pausa si la pestaña está oculta.

## 10. Sistema de diseño y UX

- Librería de componentes: **Optimus UI (@openng/optimus-ui, fork comunitario de PrimeNG)** (ADR-17), por que PrimeNG 22 (Angular 22 en adelante) dejó la licencia MIT y exige licencia comunitaria con requisitos o comercial por desarrollador, mientras Optimus UI conserva la línea abierta y el catálogo (tabla, árbol, calendario, carga de archivos). Es un proyecto joven: antes de comprometerse se verifica licencia, ritmo de mantenimiento, soporte de la versión de Angular elegida y accesibilidad (spike en la tarea 4 del backlog). Los componentes propios de `ui` envuelven a Optimus UI para poder cambiarlo si el proyecto se estanca.
- Resultado del spike (`docs/spikes/optimus-ui.md`, 17-nov): Optimus UI 2.0.2 pasa (MIT, Angular 22, mantenimiento activo) y se adopta en parte. Botón, campo de texto, diálogo y esqueleto vienen de Optimus UI con un preset reducido que apunta sus variables a nuestros *tokens*; la tabla y el paginador son propios sobre HTML semántico, porque los de Optimus UI superan el presupuesto de 250 kB por archivo y no manejan cursor. Componentes de `ui`: `ui-button`, `ui-field` con las directivas `uiInput` y `uiSelect` (lista desplegable nativa), `ui-table` (con `uiTableCell` para celdas propias), `ui-paginator`, `ui-dialog`, `ui-empty-state`, `ui-loading`, `ui-error-state`; el tema se registra con `provideUiTheme()`. Vitrina de desarrollo en `/dev/ui` (no existe en producción) con prueba de axe en modo claro y oscuro.
- *Design tokens* (colores, tipografía, espaciado, radios, densidad) como variables CSS en un solo archivo (`projects/ui/src/styles/tokens.css`); modo claro y oscuro con `light-dark()`: sigue `prefers-color-scheme` y el atributo `data-theme="light" | "dark"` en `<html>` lo fuerza; `data-density="compact"` reduce la densidad.
- Datos: tablas con cabecera fija y paginación servidor, estados vacíos, de carga (esqueletos) y de error con acción de reintento en todas las listas.
- Accesibilidad WCAG 2.2 AA: foco visible, navegación por teclado, etiquetas en todos los campos, contraste verificado, anuncios con `aria-live` para toasts y cambios de estado.
- Idioma y formato: español de Colombia (`es-CO`) como base; moneda COP con 2 decimales y separadores locales; fechas `YYYY-MM-DD` en entrada hacia la DIAN y formato local en pantalla. Textos con `@angular/localize` o Transloco (ADR-18) desde el primer día, sin cadenas fijas en las plantillas.
- Responsive: diseño *mobile-first*; el panel completo es usable en tablet, y las pantallas de consulta (listado, detalle, descarga) en móvil.

## 11. Rendimiento

- Rutas perezosas por feature; `@defer` para visor de PDF y gráficos; precarga de rutas frecuentes (`withPreloading`).
- Presupuestos de *bundle* en `angular.json`: inicial ≤ 500 kB y falla el *build* al superarlos; cualquier feature perezoso ≤ 250 kB, con un presupuesto `bundle` por el nombre de su chunk (`feature-<nombre>`, que sale del archivo de entrada perezoso del feature). No hay presupuesto `anyScript`: caía también sobre `main`, que ya cuenta en el inicial, y solo producía avisos.
- Imágenes con `NgOptimizedImage`; PDFs y XML descargados como blob, nunca incrustados en el estado.
- Metas: LCP ≤ 2,5 s y INP ≤ 200 ms en 4G simulado; medidas con Lighthouse CI en el pipeline.

## 12. Pruebas

| Nivel | Herramienta | Qué cubre |
| --- | --- | --- |
| Unitarias | Ejecutor por defecto de la CLI vigente (Vitest en Angular 22) + *Testing Library* | Servicios de estado, validadores, *pipes*, componentes de presentación |
| Componentes | Testing Library con selectores por rol y etiqueta | Interacción de formularios y tablas |
| E2E | Playwright | Login, selección de empresa, alta de empresa, emisión de factura (contra Api con DIAN simulada), descarga de PDF, revocar API key |
| Contrato | Regeneración del cliente en CI | Falla si el OpenAPI cambia sin actualizar `projects/api` |
| Accesibilidad | `axe-core` en Playwright | Sin violaciones graves en las pantallas principales |
| Visual (opcional) | Capturas de Playwright | Páginas de `ui` |

Cobertura mínima: `core` y servicios de estado 80 %. El E2E corre contra el docker-compose con un servidor DIAN simulado (`WireMock` o un `IDianClient` de prueba).

## 13. Construcción y despliegue

- `Dockerfile.web`: etapa de compilación con Node LTS y etapa final Nginx sin privilegios de superusuario.
- **Configuración en tiempo de ejecución**: `/config.json` (URL del Api, minutos de inactividad, indicadores de *features*; en el repositorio, `web/projects/app/public/config.json`) montado por contenedor y leído al arrancar por `provideCore`; el mismo artefacto sirve para desarrollo, staging y producción. No se hornean URLs en el *build*.
- Cabeceras de seguridad y caché en Nginx: archivos con hash `immutable`, `index.html` y `config.json` sin caché, CSP, `X-Content-Type-Options`, `Referrer-Policy`, `Permissions-Policy`.
- Alternativas estáticas por nube: Azure Static Web Apps, S3 + CloudFront, Cloud Storage + Cloud CDN; mismas cabeceras y `config.json` por entorno.
- *Fallback* de rutas a `index.html` para el *router* de Angular.

## 14. Convenciones de código

- TypeScript en modo `strict`, sin `any` (regla ESLint como error); `noUncheckedIndexedAccess` activado.
- ESLint (`angular-eslint`) + Prettier; commits con *conventional commits*.
- Accesibilidad y plantillas verificadas por las reglas `@angular-eslint/template/*` (`accessibility` incluidas).
- Sin lógica de negocio fiscal en el cliente: nada de cálculos de impuestos, CUFE ni validación de rangos; el cliente solo valida forma (obligatorio, longitud, formato).
- Cada pantalla define estados de carga, vacío, error y éxito antes de considerarse terminada.

## 15. Decisiones (ADR)

| ID | Decisión | Resolución |
| --- | --- | --- |
| ADR-15 | Microfrontends | No; librerías por feature con fronteras por lint |
| ADR-16 | Gestión de estado | NgRx SignalStore por feature y en `core` para el estado compartido; signals del componente para el estado local; `httpResource()` para lecturas de una sola pantalla; sin NgRx clásico (decidido el 17-nov) |
| ADR-17 | Biblioteca de UI | Optimus UI (fork de PrimeNG), envuelta en `ui`; confirmada por el spike del 17-nov (`docs/spikes/optimus-ui.md`) para botón, campo, diálogo y esqueleto; tabla y paginador propios por presupuesto de *bundle* y paginación por cursor. Se reabre si pasan 6 meses sin versión publicada |
| ADR-18 | i18n | Transloco o `@angular/localize`; decidir al crear el proyecto (preferencia: Transloco por carga en tiempo de ejecución) |
| ADR-19 | Cliente de API | Generado con servicios Angular sobre `HttpClient` |
| ADR-20 | Sesión | JWT en memoria y refresh en cookie `HttpOnly` |
| ADR-21 | Cálculo de totales | En el backend (`/documents/calculate`); el cliente no calcula |

## 16. Dependencias con el backend

Tareas que el frontend necesita y que se suman al backlog del Api:

1. ~~Fase 2: refresh por cookie `HttpOnly`, `/auth/logout`, CORS con credenciales para el origen del panel.~~ Hecho el 17-nov (fase 5, B1): encabezado `X-Client: panel`, cookie `impifact_refresh` y `Cors:Origins` con credenciales.
2. ~~Fase 2: `openapi.json` estable y versionado, con `operationId` legibles y *tags* por módulo.~~ Hecho el 17-nov (fase 5, B2): `operationId` en camelCase fijados por prueba, documento determinista y respuesta de la sesión del panel (`PanelSessionTokens`, sin `refresh_token`) descrita como `oneOf`.
3. ~~Fase 3: `POST /api/v1/documents/calculate` (totales e impuestos con `DianRounding`).~~ Hecho el 17-nov (fase 5, B3): `calculateDocumentTotals` recibe las líneas (cantidad, precio, descuentos y cargos, impuestos por tarifa o por unidad), los descuentos y cargos globales y los anticipos, y devuelve cada importe con 2 decimales listo para la emisión; con `documents:write`.
4. ~~Fase 4: `GET /api/v1/notifications` y, opcional, SSE.~~ Hecho el 17-nov (fase 5, B4), sin SSE: `GET /api/v1/notifications` (`listNotifications`) pagina por cursor del más reciente al más antiguo, filtra con `unread=true|false` y trae `unread_count` para el número de la campana; `POST /api/v1/notifications/{id}/read` (`markNotificationRead`, 204) y `POST /api/v1/notifications/read-all` (`markAllNotificationsRead`, `marked_count`) son idempotentes. Piden `documents:read` de la empresa activa; un aviso de otra empresa o de otro usuario responde 404. El estado leído es una sola marca (`is_read`) por aviso, compartida por todos los usuarios cuando el aviso es de toda la empresa.
5. Fase 4: URLs firmadas de descarga para XML y PDF (el panel las abre en nueva pestaña).
6. ~~Fase 5: endpoints de lectura para el tablero (documentos por estado y mes, consumo del plan, certificados por vencer).~~ Hecho el 17-nov (fase 5, B5): `GET /api/v1/dashboard/summary` (`getDashboardSummary`) devuelve `current_month` (documentos del mes por estado), `months` (los últimos 12 meses por familia, también los vacíos), `plan` (usado, límite y restante por familia y `period_end`; nulo sin plan contratado), `certificate` (certificado activo con `days_left`; nulo sin certificado) y `resolutions` (las activas con menos del 10 % del rango o menos de 30 días de vigencia, la misma regla de los avisos). Meses y días en el calendario de Colombia; pide `documents:read`. El listado con filtros y la exportación CSV quedan descritos en la sección 8.3.
7. ~~Fase 5 (encontrada en la tarea 5 del backlog): las empresas del usuario con nombre. El login y la renovación solo traen `companies` como identificadores, así que la selección de empresa (`/empresas/seleccionar`) las numera y muestra el identificador; hace falta un endpoint (por ejemplo, `GET /api/v1/me/companies`) con identificador, razón social y NIT de cada empresa a la que el usuario tiene acceso.~~ Hecho el 17-nov (fase 5, B7): `GET /api/v1/me/companies` (`listMyCompanies`) devuelve `items` con `id`, `name` (razón social), `nit`, `dv`, `roles` (lista: un usuario puede tener más de un rol en una empresa) e `is_active`, ordenados por razón social, sin paginar. No exige empresa activa y la empresa activa no reduce la lista. Una empresa inactiva aparece con `is_active: false` para que el selector la muestre deshabilitada.
8. ~~Fase 5 (encontrada en la tarea 5): recuperación de contraseña (pedir el cambio por correo y fijar la nueva con un token de un solo uso). Mientras no exista, `/recuperar` solo lo explica.~~ Hecho el 17-nov (fase 5, B7): `POST /api/v1/auth/password-reset/request` (`requestPasswordReset`, `{"email"}`) responde siempre 202 con el mismo `message`; el correo trae el enlace `{Panel:BaseUrl}/restablecer?token=...` (vence en 30 minutos, de un solo uso). `POST /api/v1/auth/password-reset/confirm` (`confirmPasswordReset`, `{"token", "new_password"}`) responde 204 y cierra todas las sesiones del usuario; un token que no sirve responde 422 `identity.invalid_reset_token` y una contraseña fuera de la política 422 con `errors.new_password`. Las dos son anónimas y comparten el límite del login (429). El panel necesita la página `/restablecer` que lea `token` de la URL y, al terminar, lleve al login.
9. ~~Fase 5 (encontrada en la tarea 7): catálogos de la DIAN para los formularios (tipos de identificación, tipos de organización, regímenes, responsabilidades fiscales, municipios, unidades de medida e impuestos), por ejemplo `GET /api/v1/catalogs/{nombre}` con id y nombre, y búsqueda por texto en municipios (1122) y unidades (1093). Mientras no existan, `feature-masters` usa listas fijas con los ids de las tablas sembradas (`models/catalogs.ts`): todas las opciones de los catálogos cortos, las capitales de departamento y las unidades más usadas; un id guardado que no esté en la lista se muestra como «Otro (id N)».~~ Hecho el 18-nov (fase 5, B8): `GET /api/v1/catalogs/{name}` (`listCatalog`) con cualquier credencial, para `identification-types`, `organization-types`, `regimes`, `liabilities`, `municipalities`, `departments`, `countries`, `unit-measures`, `taxes`, `payment-forms`, `payment-methods`, `document-types`, `currencies`, `item-identification-types`, `credit-note-discrepancy-responses` y `debit-note-discrepancy-responses`. Cada fila trae `id`, `code`, `name` e `is_active` (municipios con `department_id`, departamentos con `country_id`, impuestos con `description`), en orden de `id` y solo las activas salvo `include_inactive=true`. `q` busca en código o nombre sin distinguir mayúsculas ni tildes. `municipalities` y `unit-measures` se paginan por cursor (`limit` 50 por defecto, máximo 200); los demás salen completos. La respuesta lleva `version`, el mismo valor del `ETag` (304 con `If-None-Match`), y `Cache-Control: public, max-age=3600`. En el panel (F8b, 18-nov): `CatalogsStore` y `catalogSearch` de `core` reemplazan las listas fijas de `feature-masters` y `feature-companies`. Los catálogos cortos se piden una vez por sesión y se guardan en `sessionStorage` con su versión (al recargar, `If-None-Match` y 304); municipios y unidades se buscan con `q` y se paginan con «Cargar más». Hueco: el Api no busca por `id`, así que el nombre de un valor guardado (al editar) se resuelve recorriendo las páginas en orden de id hasta encontrarlo (hasta 12 páginas de 100 en el peor caso, guardadas en memoria); un filtro `ids=` lo haría en una consulta.
10. ~~Fase 5 (lista movida de la fase 4): logo de la empresa para `/empresas/:id/logo`.~~ Hecho el 18-nov (fase 5, B8): `PUT /api/v1/companies/{nit}/logo` (`saveCompanyLogo`, `config:write`) con `multipart/form-data` y el campo `logo` (PNG o JPEG reconocidos por su contenido, hasta 512 KiB; si no, 422 con `errors.logo`), `GET` (`getCompanyLogo`, `documents:read`, la imagen o 404 `company.logo_not_found`) y `DELETE` (`deleteCompanyLogo`, 204). El logo sale en el encabezado de los PDF que se generen después.
11. ~~Fase 5 (lista movida de la fase 4): vista previa del PDF antes de emitir (`preeliminar-view`).~~ Hecho el 18-nov (fase 5, B8): `POST /api/v1/documents/invoices/preview` (`previewInvoice`, `documents:write`) con el mismo cuerpo de `issueInvoice` responde el PDF (`application/pdf`, `inline`) con la marca «VISTA PREVIA - SIN VALIDEZ», el número `{prefijo} - BORRADOR` y sin CUFE; no toma consecutivo, no guarda nada y no consume el plan. Errores: 422 como la emisión, 503 `pdf.unavailable` si el conversor no responde. El panel puede pedirlo como `Blob` y abrirlo con `URL.createObjectURL`. Las notas, hecho el 18-nov (fase 5, B10): `POST /api/v1/documents/credit-notes/preview` (`previewCreditNote`) y `/debit-notes/preview` (`previewDebitNote`) con el cuerpo de `issueCreditNote` o `issueDebitNote`, con «VISTA PREVIA - SIN CUDE» y sin revisar que la factura referenciada esté aceptada.
12. ~~Fase 5 (encontrada en la tarea 6): lectura de la configuración de la empresa. El Api solo tiene escrituras para los datos, los ambientes, el software y las resoluciones (`updateCompany`, `setCompanyEnvironment`, `setCompanySoftware`, `saveCompanyResolution`); los certificados sí se listan. Hacen falta, por ejemplo, `GET /api/v1/companies/{nit}` (datos y ambientes por familia), `GET /api/v1/companies/{nit}/software` (sin PIN, con `test_set_id`) y `GET /api/v1/companies/{nit}/resolutions`. Mientras no existan, `feature-companies` recuerda lo que devolvió cada guardado de la sesión: el formulario de datos empieza con la razón social y lo demás vacío, y el asistente de paso a producción marca esos pasos «Sin verificar» (no bloquean).~~ Hecho el 18-nov (fase 5, B9), todo con `config:write` y el `{nit}` propio: `GET /api/v1/companies/{nit}` (`getCompany`) con los mismos campos que acepta `updateCompany` (más `identification_number`, `dv`, `environments` e `is_active`), `GET .../environments` (`getCompanyEnvironments`), `GET .../software` (`getCompanySoftware`: `identifier`, `url`, `test_set_id` y `has_pin`, nunca el PIN), `GET .../resolutions` (`listCompanyResolutions`: todas, con `kind`, rango, vigencia, `next_number`, `remaining`, `days_left` y `has_technical_key`, nunca la clave técnica) y `GET .../setup-status` (`getCompanySetupStatus`: `company_complete`, `certificate_valid`, `software_configured`, `test_set_id_configured`, `test_set_accepted` —nulo si la DIAN aún no respondió—, `environments` y `resolutions` con `valid` por factura, nota crédito y nota débito). El panel puede quitar los «Sin verificar» del asistente. Siguen sin endpoints el correo saliente (SMTP) de la empresa y la plantilla del PDF, así que esas pantallas no existen todavía.
13. ~~Fase 5 (encontrada en la tarea 9): listar las API keys de la empresa (por ejemplo, `GET /api/v1/api-keys` con id, nombre, prefijo, alcances, vencimiento y si está revocada, nunca el texto completo). Mientras no exista, `/llaves-api` muestra solo las llaves creadas en esa sesión del panel.~~ Hecho el 18-nov (fase 5, B9): `GET /api/v1/api-keys` (`listApiKeys`, misma política que crear y revocar) devuelve en `items` las llaves de la empresa activa (con credencial de plataforma, las de plataforma), revocadas o no, de la más nueva a la más antigua: `id`, `name`, `key_prefix`, `scopes`, `created_at`, `expires_at`, `last_used_at`, `is_revoked` y `revoked_at`; nunca el texto ni el resumen de la llave. Cursor `(created_at, id)` con `limit` 20 por defecto y máximo 100, `next_cursor` nulo en la última página. En una empresa, un usuario del panel debe ser `owner`.
14. ~~Fase 5 (encontrada en la tarea 9): listar las empresas de la plataforma con búsqueda y paginación para `/plataforma/empresas`; hoy el plan se asigna escribiendo el NIT.~~ Hecho el 18-nov (fase 5, B9): `GET /api/v1/platform/companies` (`listPlatformCompanies`, `platform:*` sin empresa) devuelve en `items` `id`, `name`, `nit`, `dv`, `email`, `is_active`, `plan_id` y `plan_name` (el plan contratado activo; nulos sin plan), ordenados por razón social sin distinguir mayúsculas. `search` busca en el NIT o la razón social (hasta 100 caracteres); cursor con `limit` 20 por defecto y máximo 100. `listPlans` y `listPlanCompanies` siguen devolviendo todo sin paginar (aceptable mientras sean pocos).
15. Fase 5 (encontrada en la tarea 9): alcances y endpoints del revendedor (`reseller` no recibe alcances en `RoleScopes`) y de `/plataforma/revendedores`; mientras no existan, esas pantallas no se ofrecen.

## 17. Backlog del frontend (fase 5)

Cada tarea es un PR con pruebas.

1. Crear el workspace con la última versión estable, librerías `core`, `ui`, `api`, ESLint con fronteras, presupuestos de *bundle* y CI.
2. `api`: script de generación y verificación de diferencias contra el OpenAPI del Api local.
3. `core`: configuración en tiempo de ejecución, sesión, interceptores, guards, manejo de errores, *toasts*.
4. `ui`: tokens, tema claro/oscuro, botón, campo, tabla, diálogo, paginador, estado vacío/carga/error.
5. `feature-auth`: login, refresh, selección de empresa, cierre por inactividad.
6. `feature-companies` y `feature-production-wizard`.
7. `feature-masters` (clientes y productos).
8. `feature-documents`: listado, detalle con seguimiento de estado, emisión con cálculo en servidor, vista previa.
9. `feature-api-keys`, `feature-plans`, `feature-notifications`, `feature-platform`.
10. E2E de los 5 flujos críticos, pruebas de accesibilidad y Lighthouse CI.

**Puerta de salida de la fase 5:** un flujo completo de alta de empresa y emisión de factura sin usar el panel PHP, con E2E en verde, sin violaciones graves de accesibilidad y presupuestos de *bundle* cumplidos.

## 18. Reglas de desarrollo del panel (carpeta `web/`)

- Angular *standalone*, *signals*, `OnPush` y control flow nativo. Sin NgModules ni NgRx clásico; el estado compartido va en NgRx SignalStore (ADR-16).
- Listas con paginación, orden y filtros en el servidor.
- Cada *feature* es una librería; un *feature* no importa a otro (el lint lo impide).
- El cliente de API es generado: no se edita `projects/api`; se regenera con `npm run api:generate`.
- Sin cálculos fiscales ni de CUFE en el cliente; se usan los endpoints del Api.
- Los tokens viven solo en memoria, nunca en `localStorage`.
- Todo texto visible pasa por i18n (es-CO). Todo componente tiene estados de carga, vacío y error.
- Comandos: `npm run lint`, `npm test`, `npm run e2e` y `npm run build` (falla si se superan los presupuestos).
