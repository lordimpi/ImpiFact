import { expect, test } from '@playwright/test';
import { seriousViolations, seriousViolationsInBothThemes } from './support/axe';
import { openMenu, pickMenuItem } from './support/menu';
import { pressLikeAPerson } from './support/press';
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

  // One «Acciones» menu per row; axe checks it open too.
  const menu = await openMenu(page, 'Acciones de Beta Ltda');
  await expect(menu.getByRole('menuitem')).toHaveText([
    'Asignar plan',
    'Crear usuario',
    'Usuarios',
    'Desactivar',
  ]);
  expect(await seriousViolations(page)).toEqual([]);
  await page.keyboard.press('Escape');
  await expect(menu).toBeHidden();

  await pickMenuItem(page, 'Acciones de Beta Ltda', 'Asignar plan');
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

test('deactivates and reactivates a company after the confirmation, and the row follows by itself', async ({
  page,
}) => {
  await signInAt(page, '/plataforma/empresas', asPlatformAdmin);
  const calls: string[] = [];
  let listed = 0;
  let active = true;
  await page.route('**/api/v1/plans', (route) =>
    route.fulfill({ json: { success: true, data: PLANS } }),
  );
  await page.route(/\/api\/v1\/platform\/companies(\?|$)/, (route) => {
    listed += 1;
    return route.fulfill({ json: { items: COMPANIES, next_cursor: null } });
  });
  await page.route(/\/api\/v1\/platform\/companies\/\d+\/(deactivate|reactivate)$/, (route) => {
    const url = new URL(route.request().url()).pathname;
    calls.push(`${route.request().method()} ${url}`);
    active = url.endsWith('/reactivate');
    return route.fulfill({
      json: {
        ...COMPANIES[0],
        is_active: active,
        changed: true,
        revoked_api_keys: active ? 0 : 2,
        signed_out_users: active ? 0 : 1,
      },
    });
  });
  await page.reload();

  const table = page.getByRole('table', { name: 'Empresas de la plataforma' });
  const acme = table.getByRole('row').filter({ hasText: 'Acme SAS' });
  await expect(acme.getByText('Activa', { exact: true })).toBeVisible();

  await pickMenuItem(page, 'Acciones de Acme SAS', 'Desactivar');
  const confirm = page.getByRole('alertdialog', { name: '¿Desactivar Acme SAS?' });
  await expect(confirm).toBeVisible();
  await expect(confirm).toContainText('Sus API keys se revocan para siempre');
  await expect(confirm).toContainText('Las facturas y demás documentos se conservan');
  // The safe choice has the focus, and Escape only closes the question.
  await expect(confirm.getByRole('button', { name: 'Cancelar' })).toBeFocused();
  expect(await seriousViolationsInBothThemes(page)).toEqual({ light: [], dark: [] });
  await page.keyboard.press('Escape');
  await expect(confirm).toBeHidden();
  expect(calls).toEqual([]);

  await pickMenuItem(page, 'Acciones de Acme SAS', 'Desactivar');
  await confirm.getByRole('button', { name: 'Desactivar empresa' }).click();
  await expect(
    page.getByText(
      'Se desactivó Acme SAS (NIT 900123456). API keys revocadas: 2. Usuarios con la sesión cerrada: 1.',
    ),
  ).toBeVisible();
  await expect(acme.getByText('Inactiva', { exact: true })).toBeVisible();

  const menu = await openMenu(page, 'Acciones de Acme SAS');
  await expect(menu.getByRole('menuitem', { name: 'Reactivar', exact: true })).toBeVisible();
  await expect(menu.getByRole('menuitem', { name: 'Desactivar', exact: true })).toHaveCount(0);
  await page.keyboard.press('Escape');
  await expect(menu).toBeHidden();

  await pickMenuItem(page, 'Acciones de Acme SAS', 'Reactivar');
  await page
    .getByRole('alertdialog', { name: '¿Reactivar Acme SAS?' })
    .getByRole('button', { name: 'Reactivar empresa' })
    .click();
  await expect(page.getByText(/Se reactivó Acme SAS \(NIT 900123456\)/)).toBeVisible();
  await expect(acme.getByText('Activa', { exact: true })).toBeVisible();
  expect(calls).toEqual([
    'POST /api/v1/platform/companies/900123456/deactivate',
    'POST /api/v1/platform/companies/900123456/reactivate',
  ]);
  expect(listed).toBe(1);
});

test('fits every company on one line at 1366×768, with its NIT and its actions menu', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1366, height: 768 });
  await signInAt(page, '/plataforma/empresas', asPlatformAdmin);
  const companies = [
    ['Café La Montaña SAS', '901458321', '4', 'facturacion@cafelamontana.example', 'Pyme 300'],
    ['Distribuidora El Roble Ltda', '900874512', '4', 'contabilidad@elroble.example', 'Anual 5000'],
    ['Ferretería El Tornillo SAS', '901236547', '9', 'ventas@eltornillo.example', null],
  ].map(([name, nit, dv, email, plan], index) => ({
    id: `c-${index}`,
    name,
    nit,
    dv,
    email,
    is_active: true,
    plan_id: plan ? 1 : null,
    plan_name: plan,
  }));
  await page.route('**/api/v1/plans', (route) =>
    route.fulfill({ json: { success: true, data: PLANS } }),
  );
  await page.route(/\/api\/v1\/platform\/companies(\?|$)/, (route) =>
    route.fulfill({ json: { items: companies, next_cursor: null } }),
  );
  await page.reload();

  const table = page.getByRole('table', { name: 'Empresas de la plataforma' });
  await expect(table.getByRole('button', { name: /^Acciones de/ })).toHaveCount(3);
  // A cell is on one line when every box of its text starts at about the same height (a second line is ~20 px lower).
  const oneLine = await table.evaluate((element) =>
    Array.from(element.querySelectorAll('tbody tr'), (row) =>
      Array.from(row.querySelectorAll('td'), (cell) => {
        const tops: number[] = [];
        const walker = document.createTreeWalker(cell, NodeFilter.SHOW_TEXT);
        for (let node = walker.nextNode(); node; node = walker.nextNode()) {
          if (node.textContent?.trim()) {
            const range = document.createRange();
            range.selectNodeContents(node);
            tops.push(...Array.from(range.getClientRects(), (rect) => rect.top));
          }
        }
        return Math.max(...tops) - Math.min(...tops) < 10;
      }),
    ),
  );
  expect(oneLine).toEqual([
    [true, true, true, true, true, true],
    [true, true, true, true, true, true],
    [true, true, true, true, true, true],
  ]);
  await expect(table.getByRole('cell', { name: '901458321-4', exact: true })).toHaveCSS(
    'white-space',
    'nowrap',
  );
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

  // Leaving the focused empty field to press Cancelar must not validate it: the dialog just closes.
  await pressLikeAPerson(page, dialog.getByRole('button', { name: 'Cancelar' }));

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
