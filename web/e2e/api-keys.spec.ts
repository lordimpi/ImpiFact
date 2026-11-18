import { expect, test } from '@playwright/test';
import { seriousViolations } from './support/axe';
import { signInAt } from './support/sign-in';

// Llaves de API (feature-api-keys) contra la Api simulada.

const SECRET = 'ifk_live_e2e_0123456789abcdef0123456789abcdef';

test('creates a key, shows it once and forgets it after closing the dialog', async ({ page }) => {
  await signInAt(page, '/llaves-api');
  const bodies: unknown[] = [];
  await page.route('**/api/v1/api-keys', (route) => {
    bodies.push(route.request().postDataJSON());
    return route.fulfill({
      status: 201,
      json: {
        id: 'k-1',
        company_id: 'c-1',
        name: 'Tienda en línea',
        key: SECRET,
        key_prefix: 'ifk_live_e2e',
        scopes: ['documents:read'],
        expires_at: null,
      },
    });
  });

  await expect(page.getByRole('heading', { level: 1, name: 'Llaves de API' })).toBeVisible();
  expect(await seriousViolations(page)).toEqual([]);

  await page.getByLabel(/Nombre de la llave/).fill('Tienda en línea');
  await page.getByRole('checkbox', { name: /Consultar documentos/ }).check();
  await page.getByRole('button', { name: 'Crear llave' }).click();

  const dialog = page.getByRole('dialog', { name: 'Copie su llave ahora' });
  await expect(dialog.getByText(SECRET)).toBeVisible();
  expect(bodies).toEqual([
    { name: 'Tienda en línea', scopes: ['documents:read'], expires_at: null },
  ]);
  expect(await seriousViolations(page)).toEqual([]);

  await dialog.getByRole('button', { name: 'Ya la copié' }).click();
  await expect(dialog).toBeHidden();
  await expect(page.getByText(SECRET)).toHaveCount(0);
  expect(await page.content()).not.toContain(SECRET);
  await expect(
    page.getByRole('table', { name: 'Llaves creadas en esta sesión' }).getByText('ifk_live_e2e…'),
  ).toBeVisible();
});
