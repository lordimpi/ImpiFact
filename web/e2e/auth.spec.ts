import { Page, expect, test } from '@playwright/test';
import { seriousViolations, seriousViolationsInBothThemes } from './support/axe';
import { CRITICAL } from './support/critical';
import { COMPANIES, USER, mockApi } from './support/mock-api';

// Inicio de sesión, selección de empresa, cierre de sesión e inactividad (feature-auth) contra la Api simulada.

async function logIn(page: Page, password = USER.password) {
  await page.getByLabel('Correo electrónico').fill(USER.email);
  await page.getByLabel('Contraseña').fill(password);
  await page.getByRole('button', { name: 'Ingresar' }).click();
}

// El propietario (rol por defecto de la Api simulada) entra en `/` al tablero.
const homeHeading = (page: Page) => page.getByRole('heading', { level: 1, name: 'Tablero' });

// Flujo crítico 1 de 5 (docs/Arquitectura frontend.md, sección 12): login, selección de empresa y tablero.
test('logs in, chooses a company and lands on the home page', CRITICAL, async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/login');
  await expect(page.getByRole('heading', { level: 1, name: 'Iniciar sesión' })).toBeVisible();
  expect(await seriousViolationsInBothThemes(page)).toEqual({ light: [], dark: [] });

  await logIn(page);

  await expect(page).toHaveURL('/empresas/seleccionar');
  await expect(page.getByRole('heading', { level: 1, name: 'Seleccionar empresa' })).toBeVisible();
  expect(await seriousViolationsInBothThemes(page)).toEqual({ light: [], dark: [] });
  await page.getByRole('button', { name: 'Trabajar con Beta Ltda' }).click();

  await expect(page).toHaveURL('/');
  await expect(homeHeading(page)).toBeVisible();
  expect(await seriousViolationsInBothThemes(page)).toEqual({ light: [], dark: [] });
  expect(api.calls.login).toEqual([
    { email: USER.email, password: USER.password, company_id: null },
  ]);
  expect(api.calls.refresh).toContainEqual({ company_id: COMPANIES[1], refresh_token: null });
  await expect(page.getByRole('button', { name: /Mi cuenta/ })).toContainText(USER.email);
});

test('shows an error for a wrong password and stays on the login', async ({ page }) => {
  await mockApi(page);
  await page.goto('/login');

  await logIn(page, 'equivocada');

  await expect(page.getByRole('alert')).toHaveText('El correo o la contraseña no son correctos.');
  await expect(page).toHaveURL('/login');
  await expect(page.getByRole('button', { name: /Mi cuenta/ })).toHaveCount(0);
});

test('a protected route sends to the login and comes back after it', async ({ page }) => {
  await mockApi(page, { companies: [COMPANIES[0]!] });
  await page.goto('/');

  await expect(page).toHaveURL('/login?returnUrl=%2F');
  await logIn(page);

  await expect(page).toHaveURL('/');
  await expect(homeHeading(page)).toBeVisible();
});

test('logging out clears the session, also after a reload', async ({ page }) => {
  const api = await mockApi(page, { companies: [COMPANIES[0]!] });
  await page.goto('/login');
  await logIn(page);
  await expect(homeHeading(page)).toBeVisible();

  await page.getByRole('button', { name: /Mi cuenta/ }).click();
  await page.getByRole('button', { name: 'Cerrar sesión' }).click();

  await expect(page).toHaveURL('/login');
  await expect(page.getByRole('button', { name: /Mi cuenta/ })).toHaveCount(0);
  expect(api.calls.logout).toBe(1);
  expect(api.hasSession()).toBe(false);
  await page.goto('/');
  await expect(page).toHaveURL('/login?returnUrl=%2F');
});

test('warns about inactivity, keeps the session on demand and closes it at the limit', async ({
  page,
}) => {
  await page.clock.install();
  await mockApi(page, { companies: [COMPANIES[0]!] });
  await page.goto('/login');
  await logIn(page);
  await expect(homeHeading(page)).toBeVisible();

  await page.clock.fastForward('25:01');
  const dialog = page.getByRole('dialog', { name: 'Su sesión se cerrará por inactividad' });
  await expect(dialog).toBeVisible();
  expect(await seriousViolations(page)).toEqual([]);
  await dialog.getByRole('button', { name: 'Seguir conectado', exact: true }).click();
  await expect(dialog).toBeHidden();

  await page.clock.fastForward('30:01');

  await expect(page).toHaveURL('/login?reason=inactivity');
  await expect(page.getByRole('status')).toContainText('Su sesión se cerró por inactividad');
});

for (const theme of ['light', 'dark'] as const) {
  test(`the login and company selection pages have no serious accessibility violations (${theme})`, async ({
    page,
  }) => {
    await page.emulateMedia({ colorScheme: theme });
    await mockApi(page);
    await page.goto('/login');
    await expect(page.getByRole('heading', { level: 1, name: 'Iniciar sesión' })).toBeVisible();
    expect(await seriousViolations(page)).toEqual([]);

    // Con los errores de los campos visibles.
    await page.getByRole('button', { name: 'Ingresar' }).click();
    await expect(page.getByText('Escriba su correo electrónico.')).toBeVisible();
    expect(await seriousViolations(page)).toEqual([]);

    await logIn(page);
    await expect(
      page.getByRole('heading', { level: 1, name: 'Seleccionar empresa' }),
    ).toBeVisible();
    expect(await seriousViolations(page)).toEqual([]);
  });
}
