/*
 * Punto de entrada perezoso del feature: `auth.routes.ts` lo carga con `import('./feature-auth')`, así todo lo de
 * aquí queda en un solo chunk llamado `feature-auth` (el nombre que usa su presupuesto en `angular.json`).
 */
export { InactivityWarningComponent } from './components/inactivity-warning.component';
export { CompanySelectionPage } from './pages/company-selection.page';
export { LoginPage } from './pages/login.page';
export { PasswordRecoveryPage } from './pages/password-recovery.page';
