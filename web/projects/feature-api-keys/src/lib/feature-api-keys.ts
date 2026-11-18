/*
 * Punto de entrada perezoso del feature: `api-keys.routes.ts` lo carga con `import('./feature-api-keys')`, así todo
 * lo de aquí queda en un solo chunk llamado `feature-api-keys` (el nombre de su presupuesto en `angular.json`).
 */
export { ApiKeysPage } from './pages/api-keys.page';
