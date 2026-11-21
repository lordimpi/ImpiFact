/*
 * Lazy entry point of the company users page: `companies.routes.ts` loads it with
 * `import('./feature-company-users')`, so the page gets its own chunk named `feature-company-users` and the owner
 * does not download the company configuration screens to manage users.
 */
export { CompanyUsersPage } from './pages/company-users.page';
