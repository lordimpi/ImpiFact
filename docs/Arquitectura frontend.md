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
│  ├─ feature-companies/
│  ├─ feature-production-wizard/
│  ├─ feature-documents/
│  ├─ feature-masters/           clientes y productos
│  ├─ feature-api-keys/
│  ├─ feature-plans/
│  ├─ feature-platform/          reseller y administrador de plataforma
│  └─ feature-notifications/
├─ e2e/                          Playwright
├─ eslint.config.js  tsconfig.base.json  package.json
```

Reglas de dependencia (se hacen cumplir con `eslint-plugin-boundaries` o `no-restricted-imports`):

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

- **Estado de servidor** (listas, detalles): servicios del feature con `signal`/`computed` y llamadas al cliente generado. Patrón: `state.loading()`, `state.error()`, `state.data()`; refresco explícito tras mutaciones.
- **Estado de UI** local: signals dentro del componente.
- **Estado global mínimo** en `core`: sesión (usuario, roles), empresa activa, preferencias, notificaciones.
- No se usa NgRx al inicio (ADR-16): el estado es mayormente de servidor y local. Se reabre si aparece estado compartido complejo (por ejemplo, un POS Enterprise con carrito offline).
- Persistencia en el navegador: solo preferencias no sensibles (tema, densidad, última empresa) en `localStorage`. Nada de tokens ni datos fiscales.

## 5. Cliente de API

- Se **genera** desde el OpenAPI que publica el Api con un generador que produzca servicios de Angular sobre `HttpClient` (`ng-openapi-gen` u OpenAPI Generator `typescript-angular`), para que pasen por los interceptores. Se descarta `openapi-fetch` por saltarse `HttpClient`.
- Script `npm run api:generate` descarga `openapi.json`, regenera `projects/api` y falla en CI si hay diferencias sin commitear (el contrato no puede divergir en silencio).
- Nombres con typos heredados del contrato JSON (`integral_salarary`, `seze`) se conservan en los tipos generados; los formularios los mapean a nombres limpios en `forms/`.
- Manejo de errores centralizado: un interceptor convierte `ProblemDetails` en un `AppError` con `code`, `message`, `fieldErrors` y `correlationId`; los formularios muestran `fieldErrors` junto al campo y el resto sale en un *toast* con el `correlationId` copiable.

## 6. Autenticación y seguridad en el cliente

| Tema | Decisión |
| --- | --- |
| Token de acceso | JWT de 15 min, **solo en memoria** (un signal en `core`), nunca en `localStorage` |
| Refresh | Cookie `HttpOnly; Secure; SameSite=Strict` emitida por el Api; el cliente llama a `/auth/refresh` con `withCredentials` al arrancar y ante un 401 |
| Interceptores | `authInterceptor` (adjunta Bearer), `refreshInterceptor` (un solo refresh en vuelo; reintenta una vez), `errorInterceptor`, `correlationInterceptor` (`X-Correlation-Id`) |
| Guards | `authGuard` (sesión), `roleGuard(roles)`, `companyGuard` (empresa activa elegida), `unsavedChangesGuard` |
| Permisos en UI | Se leen de los claims y ocultan o deshabilitan acciones; **el backend es la autoridad**, nunca se confía en el cliente |
| XSS | Sin `innerHTML` ni `bypassSecurityTrust*` salvo en un wrapper revisado (`SafeHtml` solo para el PDF/XML renderizado); plantillas Angular escapan por defecto |
| CSP | Cabeceras en el servidor estático: `default-src 'self'`, sin `unsafe-inline` (estilos con *nonce* o hash), `frame-ancestors 'none'`, `connect-src` solo el Api |
| Secretos | Ninguno en el bundle. API keys se muestran una vez con botón de copiar y se borran del estado al cerrar el diálogo |
| Certificado `.pfx` | Se sube por `multipart` directo al Api; la clave se escribe en un campo `type=password`, no se guarda en estado ni en logs del cliente |
| Cierre de sesión | Llama a `/auth/logout` (revoca el refresh), limpia estado y redirige; sincroniza entre pestañas con `BroadcastChannel` |
| Inactividad | Aviso a los 25 min y cierre a los 30 (configurable) |

Cambio requerido en el backend: el endpoint de refresh debe aceptar y emitir la cookie `HttpOnly`, y CORS debe permitir credenciales para el origen del panel. Se agrega a las tareas de la fase 2.

## 7. Mapa de rutas y roles

| Ruta | Feature | Roles |
| --- | --- | --- |
| `/login`, `/recuperar` | `feature-auth` | público |
| `/empresas/seleccionar` | `feature-auth` | todos los autenticados |
| `/` (tablero) | `app` | owner, seller |
| `/empresas/:id/{datos,ambientes,software,certificado,resoluciones,correo,logo,plantilla}` | `feature-companies` | owner |
| `/empresas/:id/produccion` | `feature-production-wizard` | owner |
| `/documentos`, `/documentos/nuevo`, `/documentos/:id` | `feature-documents` | owner, seller (`nuevo`: con alcance de emisión) |
| `/clientes`, `/productos` | `feature-masters` | owner, seller |
| `/llaves-api` | `feature-api-keys` | owner |
| `/plan` | `feature-plans` | owner |
| `/plataforma/{empresas,planes,revendedores}` | `feature-platform` | reseller, platform\_admin |
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

### 8.2 Asistente de paso a producción

Pasos con estado persistido en el servidor (empresa → certificado → resolución → set de pruebas → producción por producto). Cada paso valida con el Api antes de habilitar el siguiente; un paso completado puede reabrirse. Mensajes de error de la DIAN se muestran con su explicación en español.

### 8.3 Listado de documentos

Paginación y orden en el servidor (por clave `issued_at, id`), filtros por fecha, tipo, estado y cliente guardados en la URL (compartibles), exportación CSV desde el Api, acciones por fila: ver, XML, PDF, reenviar correo, regenerar PDF, enviar evento.

## 9. Estado en tiempo real

- Detalle de documento: *polling* con retroceso (2 s, 4 s, 8 s hasta 30 s) mientras el estado sea `Queued`, `Sent` o `Unconfirmed`; se detiene con un estado final o al salir de la pantalla.
- Notificaciones (campana): *polling* cada 60 s. Si se requiere inmediatez se agrega SSE en el Api (`/api/v1/events`) sin cambiar los componentes, porque ambos alimentan el mismo servicio de `core`.
- Todo *polling* se cancela con `takeUntilDestroyed` y se pausa si la pestaña está oculta.

## 10. Sistema de diseño y UX

- Librería de componentes: **Optimus UI (@openng/optimus-ui, fork comunitario de PrimeNG)** (ADR-17), por que PrimeNG 22 (Angular 22 en adelante) dejó la licencia MIT y exige licencia comunitaria con requisitos o comercial por desarrollador, mientras Optimus UI conserva la línea abierta y el catálogo (tabla, árbol, calendario, carga de archivos). Es un proyecto joven: antes de comprometerse se verifica licencia, ritmo de mantenimiento, soporte de la versión de Angular elegida y accesibilidad (spike en la tarea 4 del backlog). Los componentes propios de `ui` envuelven a Optimus UI para poder cambiarlo si el proyecto se estanca.
- *Design tokens* (colores, tipografía, espaciado, radios, densidad) como variables CSS en un solo archivo; modo claro y oscuro.
- Datos: tablas con cabecera fija y paginación servidor, estados vacíos, de carga (esqueletos) y de error con acción de reintento en todas las listas.
- Accesibilidad WCAG 2.2 AA: foco visible, navegación por teclado, etiquetas en todos los campos, contraste verificado, anuncios con `aria-live` para toasts y cambios de estado.
- Idioma y formato: español de Colombia (`es-CO`) como base; moneda COP con 2 decimales y separadores locales; fechas `YYYY-MM-DD` en entrada hacia la DIAN y formato local en pantalla. Textos con `@angular/localize` o Transloco (ADR-18) desde el primer día, sin cadenas fijas en las plantillas.
- Responsive: diseño *mobile-first*; el panel completo es usable en tablet, y las pantallas de consulta (listado, detalle, descarga) en móvil.

## 11. Rendimiento

- Rutas perezosas por feature; `@defer` para visor de PDF y gráficos; precarga de rutas frecuentes (`withPreloading`).
- Presupuestos de *bundle* en `angular.json`: inicial ≤ 500 kB y falla el *build* al superarlos; cualquier feature perezoso ≤ 250 kB.
- Imágenes con `NgOptimizedImage`; PDFs y XML descargados como blob, nunca incrustados en el estado.
- Metas: LCP ≤ 2,5 s y INP ≤ 200 ms en 4G simulado; medidas con Lighthouse CI en el pipeline.

## 12. Pruebas

| Nivel | Herramienta | Qué cubre |
| --- | --- | --- |
| Unitarias | Ejecutor por defecto de la CLI vigente + *Testing Library* | Servicios de estado, validadores, *pipes*, componentes de presentación |
| Componentes | Testing Library con selectores por rol y etiqueta | Interacción de formularios y tablas |
| E2E | Playwright | Login, selección de empresa, alta de empresa, emisión de factura (contra Api con DIAN simulada), descarga de PDF, revocar API key |
| Contrato | Regeneración del cliente en CI | Falla si el OpenAPI cambia sin actualizar `projects/api` |
| Accesibilidad | `axe-core` en Playwright | Sin violaciones graves en las pantallas principales |
| Visual (opcional) | Capturas de Playwright | Páginas de `ui` |

Cobertura mínima: `core` y servicios de estado 80 %. El E2E corre contra el docker-compose con un servidor DIAN simulado (`WireMock` o un `IDianClient` de prueba).

## 13. Construcción y despliegue

- `Dockerfile.web`: etapa de compilación con Node LTS y etapa final Nginx sin privilegios de superusuario.
- **Configuración en tiempo de ejecución**: `/assets/config.json` (URL del Api, entorno, indicadores de *features*) montado por contenedor; el mismo artefacto sirve para desarrollo, staging y producción. No se hornean URLs en el *build*.
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
| ADR-16 | Gestión de estado | Signals y servicios por feature; sin NgRx al inicio |
| ADR-17 | Biblioteca de UI | Optimus UI (fork de PrimeNG), envuelta en `ui`; se confirma con el spike de la tarea 4 del backlog |
| ADR-18 | i18n | Transloco o `@angular/localize`; decidir al crear el proyecto (preferencia: Transloco por carga en tiempo de ejecución) |
| ADR-19 | Cliente de API | Generado con servicios Angular sobre `HttpClient` |
| ADR-20 | Sesión | JWT en memoria y refresh en cookie `HttpOnly` |
| ADR-21 | Cálculo de totales | En el backend (`/documents/calculate`); el cliente no calcula |

## 16. Dependencias con el backend

Tareas que el frontend necesita y que se suman al backlog del Api:

1. Fase 2: refresh por cookie `HttpOnly`, `/auth/logout`, CORS con credenciales para el origen del panel.
2. Fase 2: `openapi.json` estable y versionado, con `operationId` legibles y *tags* por módulo.
3. Fase 3: `POST /api/v1/documents/calculate` (totales e impuestos con `DianRounding`).
4. Fase 4: `GET /api/v1/notifications` y, opcional, SSE.
5. Fase 4: URLs firmadas de descarga para XML y PDF (el panel las abre en nueva pestaña).
6. Fase 5: endpoints de lectura para el tablero (documentos por estado y mes, consumo del plan, certificados por vencer).

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

- Angular *standalone*, *signals*, `OnPush` y control flow nativo. Sin NgModules ni NgRx.
- Cada *feature* es una librería; un *feature* no importa a otro (el lint lo impide).
- El cliente de API es generado: no se edita `projects/api`; se regenera con `npm run api:generate`.
- Sin cálculos fiscales ni de CUFE en el cliente; se usan los endpoints del Api.
- Los tokens viven solo en memoria, nunca en `localStorage`.
- Todo texto visible pasa por i18n (es-CO). Todo componente tiene estados de carga, vacío y error.
- Comandos: `npm run lint`, `npm test`, `npm run e2e` y `npm run build` (falla si se superan los presupuestos).
