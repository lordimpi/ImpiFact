import { expect, test } from '@playwright/test';
import { seriousViolations } from './support/axe';
import { signInAt } from './support/sign-in';

// Plataforma (feature-platform) contra la Api simulada: un administrador sin empresa activa trae `platform:*`.

const PLANS = [
  { id: 1, name: 'Pyme 300', period: 1, qty_docs_invoice: 300, state: true },
  { id: 2, name: 'Anual 5000', period: 2, qty_docs_invoice: 5000, state: true },
];

const asPlatformAdmin = { companies: [], roles: ['platform_admin'], scopes: ['platform:*'] };

const COMPANIES = [
  {
    id: 'c-1',
    name: 'Acme SAS',
    nit: '900123456',
    dv: '7',
    email: 'facturas@acme.test',
    is_active: true,
    plan_id: 1,
    plan_name: 'Pyme 300',
  },
  {
    id: 'c-2',
    name: 'Beta Ltda',
    nit: '800111222',
    dv: '3',
    email: 'beta@beta.test',
    is_active: true,
    plan_id: null,
    plan_name: null,
  },
];

test('assigns a plan to a company picked from the searchable list', async ({ page }) => {
  await signInAt(page, '/plataforma/empresas', asPlatformAdmin);
  const assigned: { url: string; body: unknown }[] = [];
  const searches: (string | null)[] = [];
  await page.route('**/api/v1/plans', (route) =>
    route.fulfill({ json: { success: true, data: PLANS } }),
  );
  await page.route(/\/api\/v1\/platform\/companies(\?|$)/, (route) => {
    const search = new URL(route.request().url()).searchParams.get('search');
    searches.push(search);
    const items = COMPANIES.filter(
      (company) => !search || company.name.toLowerCase().includes(search.toLowerCase()),
    );
    return route.fulfill({ json: { items, next_cursor: null } });
  });
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
    page.getByRole('heading', { level: 1, name: 'Empresas de la plataforma' }),
  ).toBeVisible();
  const table = page.getByRole('table', { name: 'Empresas de la plataforma' });
  await expect(table.getByText('Pyme 300')).toBeVisible();
  expect(await seriousViolations(page)).toEqual([]);

  await page.getByRole('searchbox', { name: 'Buscar empresa' }).fill('beta');
  await expect(table.getByText('Acme SAS')).toHaveCount(0);
  expect(searches.at(-1)).toBe('beta');

  await page.getByRole('button', { name: 'Asignar plan a Beta Ltda' }).click();
  const dialog = page.getByRole('dialog', { name: 'Asignar plan a Beta Ltda' });
  const plan = dialog.getByRole('combobox', { name: 'Plan' });
  await expect(plan).toHaveText('Elija un plan');
  expect(await seriousViolations(page)).toEqual([]);
  // The ui-select list opens over the page, outside the dialog; axe checks it open too.
  await plan.click();
  const option = page.getByRole('option', { name: 'Anual 5000', exact: true });
  await expect(option).toBeVisible();
  expect(await seriousViolations(page)).toEqual([]);
  await option.click();
  await expect(plan).toHaveText('Anual 5000');
  await dialog.getByRole('button', { name: 'Asignar plan' }).click();

  await expect(
    page.getByText(/Se asignó el plan Anual 5000 a Beta Ltda \(NIT 800111222\)/),
  ).toBeVisible();
  await expect(table.getByText('Anual 5000')).toBeVisible();
  expect(assigned).toHaveLength(1);
  expect(assigned[0]!.url).toContain('/api/v1/companies/800111222/plan');
  expect(assigned[0]!.body).toEqual({ plan_id: 2 });
});

test('cancels a new plan without showing required errors', async ({ page }) => {
  await signInAt(page, '/plataforma/planes', asPlatformAdmin);
  await page.route('**/api/v1/plans', (route) =>
    route.fulfill({ json: { success: true, data: PLANS } }),
  );
  await page.reload();

  await page.getByRole('button', { name: 'Nuevo plan' }).click();
  const dialog = page.getByRole('dialog', { name: 'Nuevo plan' });
  await expect(dialog.getByLabel('Nombre')).toBeFocused();

  // Leaving the focused empty field to press Cancelar must not validate it: the dialog just closes. A person holds
  // the button for a moment, so the press and the release are apart.
  const cancel = await dialog.getByRole('button', { name: 'Cancelar' }).boundingBox();
  await page.mouse.move(cancel!.x + cancel!.width / 2, cancel!.y + cancel!.height / 2);
  await page.mouse.down();
  await page.waitForTimeout(150);
  await page.mouse.up();

  await expect(dialog).toBeHidden();
  await expect(page.getByText('Este campo es obligatorio.')).toHaveCount(0);
});

test('lists the plans with the platform menu', async ({ page }) => {
  const createdPlans: unknown[] = [];
  await signInAt(page, '/plataforma/planes', asPlatformAdmin);
  await page.route('**/api/v1/plans', (route) => {
    if (route.request().method() === 'POST') {
      createdPlans.push(route.request().postDataJSON());
      return route.fulfill({
        status: 201,
        json: { success: true, plan: { id: 3, name: 'Micro 50' } },
      });
    }
    return route.fulfill({ json: { success: true, data: PLANS } });
  });
  await page.reload();

  const nav = page.getByRole('navigation', { name: 'Navegación principal' });
  await expect(nav.getByRole('link', { name: 'Planes', exact: true })).toBeVisible();
  await expect(page.getByRole('table', { name: 'Planes' }).getByText('Anual 5000')).toBeVisible();
  expect(await seriousViolations(page)).toEqual([]);

  await page.getByRole('button', { name: 'Nuevo plan' }).click();
  const dialog = page.getByRole('dialog', { name: 'Nuevo plan' });
  // On open the focus goes to the first field, not to its help button: no tooltip opens by itself.
  await expect(dialog.getByLabel('Nombre')).toBeFocused();
  await expect(dialog.locator('.ui-help__tooltip--open')).toHaveCount(0);
  // El id lo asigna el Api: no se pide al crear. Cada campo trae un ejemplo y su explicación.
  await expect(dialog.getByLabel(/Id del plan/)).toHaveCount(0);
  await expect(dialog.getByLabel('Nombre')).toHaveAttribute(
    'placeholder',
    'Por ejemplo, Mensual 100',
  );
  await dialog.getByRole('button', { name: 'Ayuda sobre Documentos por periodo' }).hover();
  await expect(
    dialog.getByRole('tooltip').filter({ hasText: 'en cada periodo del plan' }),
  ).toBeVisible();
  expect(await seriousViolations(page)).toEqual([]);

  await dialog.getByLabel('Nombre').fill('Micro 50');
  await dialog.getByLabel('Facturas y notas').pressSequentially('50a');
  await expect(dialog.getByLabel('Facturas y notas')).toHaveValue('50');
  await dialog.getByRole('button', { name: 'Guardar' }).click();
  await expect(dialog).toBeHidden();
  expect(createdPlans).toHaveLength(1);
  expect(createdPlans[0]).not.toHaveProperty('id');
  expect(createdPlans[0]).toMatchObject({ name: 'Micro 50', qty_docs_invoice: 50 });
});
