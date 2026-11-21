import { Page, Route, expect, test } from '@playwright/test';
import { seriousViolationsInBothThemes } from './support/axe';
import { COMPANIES } from './support/mock-api';
import { pickOption } from './support/select';
import { signInAt } from './support/sign-in';

// Users of the active company (`/empresa/usuarios`, feature-companies) against the mocked API: only the owner sees
// the entry and the page. The signed-in user of the mock is `user-1` (the `sub` of its token).

const NIT = '900123456';
const USERS_PATH = `**/api/v1/companies/${NIT}/users`;

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

/** Mocks list, change and create; returns the bodies the panel sent. */
async function mockUsers(page: Page) {
  const state = users();
  const sent: { patch: unknown[]; create: unknown[] } = { patch: [], create: [] };
  await page.route(USERS_PATH, (route) => route.fulfill({ json: { items: state } }));
  await page.route(`${USERS_PATH}/*`, async (route: Route) => {
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

test('explains a conflict with another change in a toast', async ({ page }) => {
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

  await userRow(page, 'carlos@example.test')
    .getByRole('button', { name: 'Cambiar a dueño: Carlos Ruiz' })
    .click();

  await expect(page.getByText('Otro usuario cambió estos datos; recargue la lista.')).toBeVisible();
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
