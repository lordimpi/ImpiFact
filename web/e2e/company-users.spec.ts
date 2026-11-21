import { Page, Route, expect, test } from '@playwright/test';
import { seriousViolationsInBothThemes } from './support/axe';
import { COMPANIES } from './support/mock-api';
import { openMenu, pickMenuItem } from './support/menu';
import { pickOption } from './support/select';
import { signInAt } from './support/sign-in';

// Users of the active company (`/empresa/usuarios`, feature-companies) against the mocked API: only the owner sees
// the entry and the page. The signed-in user of the mock is `user-1` (the `sub` of its token).

const NIT = '900123456';
const usersPath = (nit: string) => `**/api/v1/companies/${nit}/users`;
const USERS_PATH = usersPath(NIT);

interface MockUser {
  id: string;
  email: string;
  name: string;
  phone: string | null;
  role: string;
  is_active: boolean;
}

/** The users of the company in the mock, changed by the PATCH and POST calls. */
function users(): MockUser[] {
  return [
    {
      id: 'user-1',
      email: 'ana@example.test',
      name: 'Ana Gómez',
      phone: '3001234567',
      role: 'owner',
      is_active: true,
    },
    {
      id: 'user-2',
      email: 'carlos@example.test',
      name: 'Carlos Ruiz',
      phone: null,
      role: 'seller',
      is_active: true,
    },
    {
      id: 'user-3',
      email: 'diana@example.test',
      name: 'Diana Peña',
      phone: '3109876543',
      role: 'seller',
      is_active: false,
    },
  ];
}

/** Mocks list, change and create for the company `nit`; returns the bodies the panel sent. */
async function mockUsers(page: Page, nit = NIT) {
  const state = users();
  const sent: { patch: unknown[]; create: unknown[] } = { patch: [], create: [] };
  await page.route(usersPath(nit), (route) => route.fulfill({ json: { items: state } }));
  await page.route(`${usersPath(nit)}/*`, async (route: Route) => {
    const id = new URL(route.request().url()).pathname.split('/').pop();
    const body = route.request().postDataJSON() as Partial<MockUser>;
    sent.patch.push(body);
    const index = state.findIndex((user) => user.id === id);
    state[index] = { ...state[index]!, ...body };
    return route.fulfill({ json: state[index] });
  });
  await page.route('**/api/v1/users', (route) => {
    const body = route.request().postDataJSON() as MockUser & { company_id: string };
    sent.create.push(body);
    const created = { ...body, id: `user-${state.length + 1}`, is_active: true };
    state.push({
      id: created.id,
      email: body.email,
      name: body.name,
      phone: body.phone,
      role: body.role,
      is_active: true,
    });
    return route.fulfill({
      status: 201,
      json: {
        id: created.id,
        company_id: body.company_id,
        email: body.email,
        name: body.name,
        role: body.role,
      },
    });
  });
  return sent;
}

const mainNav = (page: Page) => page.getByRole('navigation', { name: 'Navegación principal' });
const usersTable = (page: Page) => page.getByRole('table', { name: 'Usuarios de la empresa' });
const userRow = (page: Page, email: string) =>
  usersTable(page)
    .getByRole('row')
    .filter({ has: page.getByText(email, { exact: true }) });

test('the owner creates a seller, changes a role and deactivates a user after confirming', async ({
  page,
}) => {
  const sent = await mockUsers(page);
  await signInAt(page, '/');

  await mainNav(page).getByRole('link', { name: 'Usuarios' }).click();

  await expect(page).toHaveURL('/empresa/usuarios');
  await expect(page.getByRole('heading', { level: 1, name: 'Usuarios' })).toBeVisible();
  await expect(mainNav(page).getByRole('link', { name: 'Usuarios' })).toHaveAttribute(
    'aria-current',
    'page',
  );
  const me = userRow(page, 'ana@example.test');
  await expect(me).toContainText('(usted)');
  await expect(me).toContainText('Es su cuenta: no puede cambiar su rol ni desactivarla.');
  await expect(me.getByRole('button')).toHaveCount(0);
  await expect(userRow(page, 'diana@example.test')).toContainText('Inactivo');

  // Create a seller.
  await page.getByRole('button', { name: 'Nuevo usuario' }).click();
  const dialog = page.getByRole('dialog', { name: 'Nuevo usuario' });
  await dialog.getByLabel('Correo electrónico').fill('eva@example.test');
  await dialog.getByLabel('Nombre').fill('Eva Torres');
  await dialog.getByLabel('Contraseña').fill('clave-segura-123');
  await pickOption(page, dialog.getByRole('combobox', { name: 'Rol' }), 'Vendedor');
  await dialog.getByRole('button', { name: 'Crear usuario' }).click();

  await expect(dialog).toBeHidden();
  await expect(
    page.getByText('Se creó el usuario eva@example.test como vendedor; ya puede iniciar sesión.'),
  ).toBeVisible();
  await expect(userRow(page, 'eva@example.test')).toContainText('Vendedor');
  expect(sent.create).toEqual([
    {
      company_id: COMPANIES[0],
      email: 'eva@example.test',
      name: 'Eva Torres',
      phone: null,
      password: 'clave-segura-123',
      role: 'seller',
    },
  ]);

  // Change a role.
  await userRow(page, 'carlos@example.test')
    .getByRole('button', { name: 'Cambiar a dueño: Carlos Ruiz' })
    .click();
  await expect(userRow(page, 'carlos@example.test')).toContainText('Dueño');
  await expect(page.getByText('Carlos Ruiz ahora es dueño.')).toBeVisible();

  // Deactivate, after the confirmation.
  await userRow(page, 'carlos@example.test')
    .getByRole('button', { name: 'Desactivar a Carlos Ruiz' })
    .click();
  const confirm = page.getByRole('dialog', { name: 'Desactivar a Carlos Ruiz' });
  await expect(confirm).toContainText(
    'perderá el acceso a esta empresa y se cerrarán sus sesiones',
  );
  await confirm.getByRole('button', { name: 'Desactivar' }).click();

  await expect(confirm).toBeHidden();
  await expect(userRow(page, 'carlos@example.test')).toContainText('Inactivo');
  await expect(
    page.getByText('Carlos Ruiz quedó inactivo y se cerraron sus sesiones.'),
  ).toBeVisible();
  expect(sent.patch).toEqual([{ role: 'owner' }, { is_active: false }]);
});

test('explains a conflict with another change in a toast and refreshes the list by itself', async ({
  page,
}) => {
  await mockUsers(page);
  await page.route(`${USERS_PATH}/*`, (route) =>
    route.fulfill({
      status: 409,
      contentType: 'application/problem+json',
      body: JSON.stringify({
        status: 409,
        code: 'concurrency.conflict',
        message: 'Conflicto.',
        correlation_id: 'e2e',
      }),
    }),
  );
  await signInAt(page, '/empresa/usuarios');

  // After the conflict the page asks for the list again by itself (no «Recargar» button).
  let listsAfterConflict = 0;
  page.on('request', (request) => {
    if (
      request.method() === 'GET' &&
      new URL(request.url()).pathname.endsWith(`/api/v1/companies/${NIT}/users`)
    ) {
      listsAfterConflict++;
    }
  });
  await userRow(page, 'carlos@example.test')
    .getByRole('button', { name: 'Cambiar a dueño: Carlos Ruiz' })
    .click();

  await expect(
    page.getByText('Otra persona cambió estos datos al mismo tiempo; la lista se actualizó.'),
  ).toBeVisible();
  await expect.poll(() => listsAfterConflict).toBeGreaterThan(0);
  await expect(page.getByRole('button', { name: 'Recargar' })).toHaveCount(0);
  await expect(userRow(page, 'carlos@example.test')).toContainText('Vendedor');
});

test('has no serious accessibility violations with the create dialog open, in both themes', async ({
  page,
}) => {
  await mockUsers(page);
  await signInAt(page, '/empresa/usuarios');
  await expect(userRow(page, 'diana@example.test')).toContainText('Inactivo');

  expect(await seriousViolationsInBothThemes(page)).toEqual({ light: [], dark: [] });

  await page.getByRole('button', { name: 'Nuevo usuario' }).click();
  await expect(page.getByRole('dialog', { name: 'Nuevo usuario' })).toBeVisible();

  expect(await seriousViolationsInBothThemes(page)).toEqual({ light: [], dark: [] });
});

test('a seller does not see the entry and cannot open the page', async ({ page }) => {
  await signInAt(page, '/', { roles: ['seller'], scopes: ['documents:read', 'documents:write'] });

  await expect(mainNav(page).getByRole('link', { name: 'Documentos' })).toBeVisible();
  await expect(mainNav(page).getByRole('link', { name: 'Usuarios' })).toHaveCount(0);

  await page.goto('/empresa/usuarios');

  await expect(page).toHaveURL('/');
  await expect(page.getByRole('heading', { level: 1, name: 'Usuarios' })).toHaveCount(0);
});

// The platform admin (no active company, `platform:*`) manages the users of any company from «Empresas de la
// plataforma», on the same page (`/plataforma/empresas/:nit/usuarios`).
const asPlatformAdmin = { companies: [], roles: ['platform_admin'], scopes: ['platform:*'] };
const BETA = {
  id: 'c-2',
  name: 'Beta Ltda',
  nit: '800111222',
  dv: '3',
  email: 'beta@beta.test',
  is_active: true,
  plan_id: null,
  plan_name: null,
};

/** Signs in as the platform admin on the list of companies, with Beta in the platform. */
async function signInAsPlatformAdmin(page: Page) {
  await signInAt(page, '/plataforma/empresas', asPlatformAdmin);
  await page.route('**/api/v1/plans', (route) =>
    route.fulfill({ json: { success: true, data: [] } }),
  );
  await page.route(/\/api\/v1\/platform\/companies(\?|$)/, (route) => {
    const search = new URL(route.request().url()).searchParams.get('search') ?? '';
    const items = [BETA].filter((company) => company.nit.includes(search));
    return route.fulfill({ json: { items, next_cursor: null } });
  });
  const sent = await mockUsers(page, BETA.nit);
  await page.reload();
  return sent;
}

test('the platform admin opens the users of a company, creates a seller and deactivates a user', async ({
  page,
}) => {
  const sent = await signInAsPlatformAdmin(page);

  await pickMenuItem(page, 'Acciones de Beta Ltda', 'Usuarios');

  await expect(page).toHaveURL(`/plataforma/empresas/${BETA.nit}/usuarios`);
  await expect(
    page.getByRole('heading', { level: 1, name: 'Usuarios de Beta Ltda' }),
  ).toBeVisible();
  const trail = page.getByRole('navigation', { name: 'Ruta de navegación' });
  await expect(trail.getByRole('link', { name: 'Empresas de la plataforma' })).toBeVisible();
  // The admin is not a member: no row is theirs and every row has its actions.
  await expect(usersTable(page)).not.toContainText('(usted)');
  await expect(
    userRow(page, 'ana@example.test').getByRole('button', { name: 'Desactivar a Ana Gómez' }),
  ).toBeVisible();

  // Create a seller of Beta.
  await page.getByRole('button', { name: 'Nuevo usuario' }).click();
  const dialog = page.getByRole('dialog', { name: 'Nuevo usuario' });
  await dialog.getByLabel('Correo electrónico').fill('luis@beta.test');
  await dialog.getByLabel('Nombre').fill('Luis Vendedor');
  await dialog.getByLabel('Contraseña').fill('clave-segura-123');
  await pickOption(page, dialog.getByRole('combobox', { name: 'Rol' }), 'Vendedor');
  await dialog.getByRole('button', { name: 'Crear usuario' }).click();

  await expect(dialog).toBeHidden();
  await expect(userRow(page, 'luis@beta.test')).toContainText('Vendedor');
  expect(sent.create).toEqual([
    {
      company_id: BETA.id,
      email: 'luis@beta.test',
      name: 'Luis Vendedor',
      phone: null,
      password: 'clave-segura-123',
      role: 'seller',
    },
  ]);

  // Deactivate, after the confirmation.
  await userRow(page, 'carlos@example.test')
    .getByRole('button', { name: 'Desactivar a Carlos Ruiz' })
    .click();
  const confirm = page.getByRole('dialog', { name: 'Desactivar a Carlos Ruiz' });
  await confirm.getByRole('button', { name: 'Desactivar' }).click();

  await expect(confirm).toBeHidden();
  await expect(userRow(page, 'carlos@example.test')).toContainText('Inactivo');
  expect(sent.patch).toEqual([{ is_active: false }]);

  // Back to the list from the breadcrumb.
  await trail.getByRole('link', { name: 'Empresas de la plataforma' }).click();
  await expect(page).toHaveURL('/plataforma/empresas');
});

test('the platform users page has no serious accessibility violations, in both themes', async ({
  page,
}) => {
  // Four axe runs in both themes, one with a row menu open.
  test.slow();
  await signInAsPlatformAdmin(page);
  const button = page.getByRole('button', { name: 'Acciones de Beta Ltda', exact: true });
  await expect(button).toBeVisible();
  await expect(button).toHaveAttribute('aria-haspopup', 'menu');
  await expect(button).toHaveAttribute('aria-expanded', 'false');

  expect(await seriousViolationsInBothThemes(page)).toEqual({ light: [], dark: [] });

  // The row menu is styled by its Optimus theme: a card with a background and a shadow.
  const menu = await openMenu(page, 'Acciones de Beta Ltda');
  await expect(button).toHaveAttribute('aria-expanded', 'true');
  const popup = await menu.evaluate((list) => {
    const panel = (list.closest('.p-menu') ?? list) as HTMLElement;
    const style = getComputedStyle(panel);
    return { background: style.backgroundColor, shadow: style.boxShadow };
  });
  expect(popup.background).not.toBe('rgba(0, 0, 0, 0)');
  expect(popup.shadow).not.toBe('none');
  expect(await seriousViolationsInBothThemes(page)).toEqual({ light: [], dark: [] });

  // Same keyboard as the Exportar menu: Escape closes it back on the button; Enter opens it, the arrows move and
  // Enter picks «Usuarios».
  await page.keyboard.press('Escape');
  await expect(menu).toBeHidden();
  await expect(button).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(menu).toBeVisible();
  await page.keyboard.press('End');
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(`/plataforma/empresas/${BETA.nit}/usuarios`);

  await expect(userRow(page, 'diana@example.test')).toContainText('Inactivo');

  expect(await seriousViolationsInBothThemes(page)).toEqual({ light: [], dark: [] });

  await page.goto('/plataforma/empresas/123/usuarios');
  await expect(page.getByText('No encontramos esa empresa')).toBeVisible();
  await expect(
    page.getByRole('link', { name: 'Volver a Empresas de la plataforma' }),
  ).toBeVisible();

  expect(await seriousViolationsInBothThemes(page)).toEqual({ light: [], dark: [] });
});

test('an owner cannot open the platform users page', async ({ page }) => {
  await signInAt(page, '/');

  await page.goto(`/plataforma/empresas/${BETA.nit}/usuarios`);

  await expect(page).toHaveURL('/');
  await expect(page.getByRole('heading', { level: 1, name: /^Usuarios de/ })).toHaveCount(0);
});
