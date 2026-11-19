/*
 * Punto de entrada perezoso del feature: `masters.routes.ts` lo carga con `import('./feature-masters')`, así todo lo
 * de aquí queda en un solo chunk llamado `feature-masters` (el nombre que usa su presupuesto en `angular.json`).
 */
export { CustomerFormPage } from './pages/customer-form.page';
export { CustomerListPage } from './pages/customer-list.page';
export { ProductFormPage } from './pages/product-form.page';
export { ProductListPage } from './pages/product-list.page';
