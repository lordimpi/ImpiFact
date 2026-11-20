import { Page, expect, test } from '@playwright/test';
import { seriousViolations } from './support/axe';
import { mockApi } from './support/mock-api';
import { signInAt } from './support/sign-in';

// Panel shell: sidebar grouped by section and filtered by role, top bar and a menu drawer on narrow screens. The
// texts come from the es-CO translations.

const mainNav = (page: Page) => page.getByRole('navigation', { name: 'Navegación principal' });

// E2E smoke test: without a session the home sends to the login, which has no sidebar.
test('opens the shell with Spanish texts', async ({ page }) => {
  await mockApi(page);
  await page.goto('/');

  await expect(page).toHaveTitle('ImpiFact · Panel');
  await expect(page.locator('html')).toHaveAttribute('lang', 'es-CO');
  await expect(page.getByRole('heading', { level: 1, name: 'Iniciar sesión' })).toBeVisible();
  await expect(mainNav(page)).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Menú de navegación' })).toHaveCount(0);
});

test('shows the owner menu grouped by section and marks the current page', async ({ page }) => {
  await signInAt(page, '/');

  const nav = mainNav(page);
  await expect(nav).toBeVisible();
  const home = nav.getByRole('link', { name: 'Inicio' });
  await expect(home).toHaveAttribute('aria-current', 'page');
  await expect(nav.getByRole('list', { name: 'Emisión' }).getByRole('link')).toHaveText([
    'Nueva factura',
    'Nota crédito',
    'Nota débito',
  ]);
  await expect(nav.getByRole('list', { name: 'Consulta' }).getByRole('link')).toHaveText([
    'Documentos',
    'Clientes',
    'Productos',
  ]);
  await expect(nav.getByRole('list', { name: 'Ajustes' }).getByRole('link')).toHaveText([
    'Empresa',
    'Paso a producción',
    'Llaves de API',
    'Mi plan',
    'Avisos',
  ]);
  await expect(nav.getByRole('list', { name: 'Plataforma' })).toHaveCount(0);
  // On a desktop the sidebar is fixed: there is no menu button.
  await expect(page.getByRole('button', { name: 'Menú de navegación' })).toBeHidden();
  await expect(page.getByRole('banner').getByText('Acme SAS')).toBeVisible();
  expect(await seriousViolations(page)).toEqual([]);

  await nav.getByRole('link', { name: 'Avisos' }).click();

  await expect(page).toHaveURL('/notificaciones');
  await expect(nav.getByRole('link', { name: 'Avisos' })).toHaveAttribute('aria-current', 'page');
  await expect(home).not.toHaveAttribute('aria-current', 'page');
});

test('hides the issuing entries from a seller without the issuing scope', async ({ page }) => {
  await signInAt(page, '/', { roles: ['seller'], scopes: ['documents:read'] });

  const nav = mainNav(page);
  await expect(nav.getByRole('link', { name: 'Documentos' })).toBeVisible();
  await expect(nav.getByRole('list', { name: 'Emisión' })).toHaveCount(0);
  await expect(nav.getByRole('link', { name: 'Llaves de API' })).toHaveCount(0);
});

test('shows only the home and the notices to a customer', async ({ page }) => {
  await signInAt(page, '/', { roles: ['customer'], scopes: ['documents:read'] });

  await expect(page.getByRole('heading', { level: 1, name: 'Panel de ImpiFact' })).toBeVisible();
  await expect(mainNav(page).getByRole('link')).toHaveText(['Inicio', 'Avisos']);
});

test('shows only the platform entries to a platform administrator', async ({ page }) => {
  await signInAt(page, '/plataforma/planes', {
    companies: [],
    roles: ['platform_admin'],
    scopes: ['platform:*'],
  });

  const nav = mainNav(page);
  await expect(nav.getByRole('link')).toHaveText(['Inicio', 'Planes', 'Empresas']);
  await expect(nav.getByRole('link', { name: 'Planes' })).toHaveAttribute('aria-current', 'page');
  await expect(page.getByRole('banner')).toContainText('Administración de la plataforma');
});

test.describe('on a phone', () => {
  test.use({ viewport: { width: 390, height: 844 } });

  test('opens the menu drawer with the keyboard and closes it', async ({ page }) => {
    await signInAt(page, '/');
    const toggle = page.getByRole('button', { name: 'Menú de navegación' });
    const nav = mainNav(page);
    await expect(toggle).toHaveAttribute('aria-expanded', 'false');
    await expect(nav).toBeHidden();

    await toggle.focus();
    await page.keyboard.press('Enter');

    await expect(toggle).toHaveAttribute('aria-expanded', 'true');
    await expect(nav).toBeVisible();
    await expect(nav.getByRole('link', { name: 'Inicio' })).toBeFocused();
    expect(await seriousViolations(page)).toEqual([]);

    await page.keyboard.press('Escape');

    await expect(toggle).toHaveAttribute('aria-expanded', 'false');
    await expect(nav).toBeHidden();
    await expect(toggle).toBeFocused();
  });

  test('closes the menu drawer after choosing a link or tapping outside', async ({ page }) => {
    await signInAt(page, '/');
    const toggle = page.getByRole('button', { name: 'Menú de navegación' });
    const nav = mainNav(page);

    await toggle.click();
    await nav.getByRole('link', { name: 'Avisos' }).click();

    await expect(page).toHaveURL('/notificaciones');
    await expect(nav).toBeHidden();
    await expect(toggle).toHaveAttribute('aria-expanded', 'false');

    await toggle.click();
    await expect(nav).toBeVisible();
    await page.mouse.click(370, 400);

    await expect(nav).toBeHidden();
    await expect(toggle).toBeFocused();
  });
});
