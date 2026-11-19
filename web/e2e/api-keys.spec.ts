import { expect, test } from '@playwright/test';
import { seriousViolations } from './support/axe';
import { signInAt } from './support/sign-in';

// Llaves de API (feature-api-keys) contra la Api simulada.

const SECRET = 'ifk_live_e2e_0123456789abcdef0123456789abcdef';

const ERP_KEY = {
  id: 'k-0',
  name: 'ERP contable',
  key_prefix: 'ifk_live_erp',
  scopes: ['documents:read'],
  created_at: '2026-10-01T15:00:00Z',
  expires_at: null,
  last_used_at: null,
  is_revoked: false,
  revoked_at: null,
};

test('creates a key, shows it once and forgets it after closing the dialog', async ({ page }) => {
  await signInAt(page, '/llaves-api');
  const bodies: unknown[] = [];
  const listed: object[] = [ERP_KEY];
  await page.route(/\/api\/v1\/api-keys(\?|$)/, (route) => {
    if (route.request().method() === 'GET') {
      return route.fulfill({ json: { items: listed, next_cursor: null } });
    }
    bodies.push(route.request().postDataJSON());
    listed.unshift({ ...ERP_KEY, id: 'k-1', name: 'Tienda en línea', key_prefix: 'ifk_live_e2e' });
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
  await page.reload();

  await expect(page.getByRole('heading', { level: 1, name: 'Llaves de API' })).toBeVisible();
  const table = page.getByRole('table', { name: 'Llaves de la empresa' });
  await expect(table.getByText('ERP contable')).toBeVisible();
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
  await expect(table.getByText('ifk_live_e2e…')).toBeVisible();
  await expect(table.getByText('ERP contable')).toBeVisible();
});
