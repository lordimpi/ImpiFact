import { expect, test } from '@playwright/test';
import { seriousViolations } from './support/axe';
import { signInAt } from './support/sign-in';

// Plataforma (feature-platform) contra la Api simulada: un administrador sin empresa activa trae `platform:*`.

const PLANS = [
  { id: 1, name: 'Pyme 300', period: 1, qty_docs_invoice: 300, state: true },
  { id: 2, name: 'Anual 5000', period: 2, qty_docs_invoice: 5000, state: true },
];

const asPlatformAdmin = { companies: [], roles: ['platform_admin'], scopes: ['platform:*'] };

test('assigns a plan to a company by its NIT', async ({ page }) => {
  await signInAt(page, '/plataforma/empresas', asPlatformAdmin);
  const assigned: { url: string; body: unknown }[] = [];
  await page.route('**/api/v1/plans', (route) =>
    route.fulfill({ json: { success: true, data: PLANS } }),
  );
  await page.route('**/api/v1/companies/*/plan', (route) => {
    assigned.push({ url: route.request().url(), body: route.request().postDataJSON() });
    return route.fulfill({
      json: {
        success: true,
        plan: PLANS[1],
        start_plan_date: '2026-11-17T20:00:00Z',
      },
    });
  });
  await page.reload();

  await expect(
    page.getByRole('heading', { level: 1, name: 'Asignar plan a una empresa' }),
  ).toBeVisible();
  await expect(page.getByRole('option', { name: 'Anual 5000' })).toBeAttached();
  expect(await seriousViolations(page)).toEqual([]);

  await page.getByLabel(/NIT de la empresa/).fill('900123456');
  await page.getByRole('combobox', { name: 'Plan' }).selectOption('2');
  await page.getByRole('button', { name: 'Asignar plan' }).click();

  await expect(page.getByText(/Se asignó el plan Anual 5000 a la empresa 900123456/)).toBeVisible();
  expect(assigned).toHaveLength(1);
  expect(assigned[0]!.url).toContain('/api/v1/companies/900123456/plan');
  expect(assigned[0]!.body).toEqual({ plan_id: 2 });
});

test('lists the plans with the platform menu', async ({ page }) => {
  await signInAt(page, '/plataforma/planes', asPlatformAdmin);
  await page.route('**/api/v1/plans', (route) =>
    route.fulfill({ json: { success: true, data: PLANS } }),
  );
  await page.reload();

  const nav = page.getByRole('navigation', { name: 'Navegación principal' });
  await expect(nav.getByRole('link', { name: 'Planes', exact: true })).toBeVisible();
  await expect(page.getByRole('table', { name: 'Planes' }).getByText('Anual 5000')).toBeVisible();
  expect(await seriousViolations(page)).toEqual([]);

  await page.getByRole('button', { name: 'Nuevo plan' }).click();
  const dialog = page.getByRole('dialog', { name: 'Nuevo plan' });
  await expect(dialog.getByLabel(/Id del plan/)).toHaveValue('3');
  expect(await seriousViolations(page)).toEqual([]);
});
