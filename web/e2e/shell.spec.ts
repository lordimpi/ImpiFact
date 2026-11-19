import { expect, test } from '@playwright/test';
import { mockApi } from './support/mock-api';

// Prueba de humo E2E (de extremo a extremo): abre el shell y comprueba que los textos salen de i18n en es-CO.
// Sin sesión, el inicio manda al login.
test('opens the shell with Spanish texts', async ({ page }) => {
  await mockApi(page);
  await page.goto('/');

  await expect(page).toHaveTitle('ImpiFact · Panel');
  await expect(page.locator('html')).toHaveAttribute('lang', 'es-CO');
  await expect(page.getByRole('heading', { level: 1, name: 'Iniciar sesión' })).toBeVisible();
  const nav = page.getByRole('navigation', { name: 'Navegación principal' });
  await expect(nav.getByRole('link', { name: 'Inicio' })).toBeVisible();
});
