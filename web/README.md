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
npm run api:generate   # generación del cliente de API (pendiente, tarea 2 del backlog)
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

## Reglas

- Fronteras entre librerías (sección 2 de la arquitectura), verificadas por `npm run lint` con `eslint-plugin-boundaries`: `app` importa todo; un `feature-*` importa `core`, `ui` y `api`, nunca otro `feature-*`; `core` solo importa `api`; `ui` y `api` no importan nada del workspace.
- Componentes _standalone_ con `OnPush`, _signals_ y control flow nativo; sin NgModules ni NgRx. La aplicación no usa Zone.js (_zoneless_, el valor por defecto de Angular 22).
- Todo texto visible sale de Transloco (`es-CO`); no se escriben textos fijos en las plantillas.
- Presupuestos de _bundle_ en `angular.json`: inicial ≤ 500 kB y cualquier archivo de script ≤ 250 kB; el _build_ falla al superarlos.

## Crear un feature nuevo

1. `npx ng generate library feature-<nombre> --prefix=feat`. Después:
   - borrar el bloque `compilerOptions.paths` que la CLI agrega en `tsconfig.json` (apunta a `dist/` y tapa los alias de `tsconfig.base.json`);
   - en los `tsconfig.*.json` del feature, extender `../../tsconfig.base.json`;
   - cambiar el nombre en su `package.json` a `@impifact/feature-<nombre>` y borrar el `README.md` generado.
2. Exportar sus rutas desde `src/public-api.ts` y registrarlas en `projects/app/src/app/app.routes.ts` con `loadChildren`.
3. El alias `@impifact/feature-*` y las reglas de fronteras ya lo cubren; no hay que tocar ESLint.
