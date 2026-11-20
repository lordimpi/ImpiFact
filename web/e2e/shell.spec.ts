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
  await expect(page.locator('.shell-body')).toHaveJSProperty('inert', false);
  await expect(page.getByRole('banner').getByText('Acme SAS')).toBeVisible();
  expect(await seriousViolations(page)).toEqual([]);

  await nav.getByRole('link', { name: 'Avisos' }).click();

  await expect(page).toHaveURL('/notificaciones');
  await expect(nav.getByRole('link', { name: 'Avisos' })).toHaveAttribute('aria-current', 'page');
  await expect(home).not.toHaveAttribute('aria-current', 'page');
});

test('keeps showing who is signed in after a page reload', async ({ page }) => {
  await mockApi(page);
  await signInAt(page, '/');
  const account = page.getByRole('banner').getByRole('button', { name: /Mi cuenta/ });
  await expect(account).toContainText('ana@example.test');

  // A reload restores the session from the refresh cookie; the JWT carries no email, the refresh answer does.
  await page.reload();

  await expect(account).toContainText('ana@example.test');
});

test('offers the user manual from the top bar in a new tab', async ({ page }) => {
  await mockApi(page);
  await signInAt(page, '/');

  const help = page.getByRole('banner').getByRole('link', { name: /Ayuda/ });

  await expect(help).toHaveAttribute('href', '/ayuda/manual-de-uso.pdf');
  await expect(help).toHaveAttribute('target', '_blank');
  await expect(help).toHaveAttribute('rel', /noopener/);
  await expect(help).toHaveAccessibleName(/manual de uso/i);
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

// --if-color-page of each theme (tokens.css).
const LIGHT_PAGE = 'rgb(244, 246, 250)';
const DARK_PAGE = 'rgb(34, 42, 55)';
const bodyBackground = (page: Page) =>
  page.evaluate(() => getComputedStyle(document.body).backgroundColor);

test.describe('theme', () => {
  test('is light by default even when the system prefers dark', async ({ page }) => {
    await page.emulateMedia({ colorScheme: 'dark' });
    await signInAt(page, '/');

    await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
    expect(await bodyBackground(page)).toBe(LIGHT_PAGE);
    await expect(
      page.getByRole('banner').getByRole('button', { name: 'Activar modo oscuro' }),
    ).toBeVisible();
  });

  test('switches to dark from the top bar and keeps it after a reload and on the login', async ({
    page,
  }) => {
    await signInAt(page, '/');
    const banner = page.getByRole('banner');

    await banner.getByRole('button', { name: 'Activar modo oscuro' }).click();

    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
    await expect.poll(() => bodyBackground(page)).toBe(DARK_PAGE);
    const toLight = banner.getByRole('button', { name: 'Activar modo claro' });
    await expect(toLight).toBeVisible();
    expect(await seriousViolations(page)).toEqual([]);

    await page.reload();
    await expect(toLight).toBeVisible();
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');

    // The access pages have no top bar but follow the stored theme too.
    await page.getByRole('button', { name: /Mi cuenta/ }).click();
    await page.getByRole('button', { name: 'Cerrar sesión' }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Iniciar sesión' })).toBeVisible();
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
    expect(await bodyBackground(page)).toBe(DARK_PAGE);
  });

  test('goes back to light and keeps it after a reload', async ({ page }) => {
    await page.addInitScript(() => {
      if (!sessionStorage.getItem('seeded')) {
        sessionStorage.setItem('seeded', '1');
        localStorage.setItem('impifact.theme', 'dark');
      }
    });
    await signInAt(page, '/');
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');

    await page.getByRole('banner').getByRole('button', { name: 'Activar modo claro' }).click();
    await page.reload();

    await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
    expect(await bodyBackground(page)).toBe(LIGHT_PAGE);
  });
});

test('hides and shows the sidebar on a desktop and remembers it', async ({ page }) => {
  await signInAt(page, '/');
  const nav = mainNav(page);
  const hide = page.getByRole('banner').getByRole('button', { name: 'Ocultar el menú' });
  const contentLeft = () =>
    page.locator('#main-content').evaluate((main) => main.getBoundingClientRect().left);
  await expect(hide).toHaveAttribute('aria-expanded', 'true');
  await expect(hide).toHaveAttribute('aria-controls', 'app-sidebar');
  await expect(nav).toBeVisible();

  await hide.click();

  const show = page.getByRole('banner').getByRole('button', { name: 'Mostrar el menú' });
  await expect(show).toHaveAttribute('aria-expanded', 'false');
  await expect(nav).toBeHidden();
  // The content takes the whole width and stays usable: a hidden sidebar is not a modal drawer.
  await expect.poll(contentLeft).toBe(0);
  await expect(page.locator('.shell-body')).toHaveJSProperty('inert', false);
  await expect(show).toBeFocused();

  await page.reload();
  await expect(show).toBeVisible();
  await expect(nav).toBeHidden();

  await show.click();
  await expect(nav).toBeVisible();
  await expect.poll(contentLeft).toBeGreaterThan(200);
  await page.reload();
  await expect(nav).toBeVisible();
});

test.describe('on a phone', () => {
  test.use({ viewport: { width: 390, height: 844 } });

  test('keeps the drawer when the sidebar was hidden on a desktop', async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem('impifact.sidebar', 'hidden'));
    await signInAt(page, '/');
    const nav = mainNav(page);

    await expect(page.getByRole('button', { name: 'Mostrar el menú' })).toBeHidden();
    await page.getByRole('button', { name: 'Menú de navegación' }).click();

    await expect(nav).toBeVisible();
    await expect(page.locator('.shell-body')).toHaveJSProperty('inert', true);
  });

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

  test('keeps the focus inside the open drawer and makes the page behind inert', async ({
    page,
  }) => {
    await signInAt(page, '/');
    const toggle = page.getByRole('button', { name: 'Menú de navegación' });
    const nav = mainNav(page);
    const focusInDrawer = () =>
      page.evaluate(() => !!document.activeElement?.closest('#app-sidebar'));
    // Programmatic focus is refused by inert content, so it tells whether the main content can take the focus.
    const mainTakesFocus = () =>
      page.evaluate(() => {
        const main = document.getElementById('main-content');
        main?.focus();
        return document.activeElement === main;
      });
    await expect.poll(mainTakesFocus).toBe(true);

    await toggle.click();
    await expect(nav.getByRole('link', { name: 'Inicio' })).toBeFocused();
    const stops = await page
      .locator('#app-sidebar')
      .locator('a[href], button')
      .evaluateAll((elements) => elements.length);

    // Forward and backward, past the last and the first element of the drawer.
    for (let index = 0; index < stops + 2; index++) {
      await page.keyboard.press('Tab');
      expect(await focusInDrawer()).toBe(true);
    }
    for (let index = 0; index < stops + 2; index++) {
      await page.keyboard.press('Shift+Tab');
      expect(await focusInDrawer()).toBe(true);
    }
    expect(await mainTakesFocus()).toBe(false);
    await expect(page.locator('#main-content')).toHaveJSProperty('inert', false);
    await expect(page.locator('.shell-body')).toHaveJSProperty('inert', true);
    await expect(page.getByRole('link', { name: 'Saltar al contenido' })).toHaveJSProperty(
      'inert',
      true,
    );

    await page.keyboard.press('Escape');

    await expect(nav).toBeHidden();
    await expect(toggle).toBeFocused();
    await expect(page.locator('.shell-body')).toHaveJSProperty('inert', false);
    expect(await mainTakesFocus()).toBe(true);
  });

  test('releases the page when the window widens past the drawer', async ({ page }) => {
    await signInAt(page, '/');
    await page.getByRole('button', { name: 'Menú de navegación' }).click();
    await expect(page.locator('.shell-body')).toHaveJSProperty('inert', true);

    await page.setViewportSize({ width: 1280, height: 800 });

    await expect(page.locator('.shell-body')).toHaveJSProperty('inert', false);
    await expect(mainNav(page)).toBeVisible();
    await expect(page.locator('#app-sidebar')).not.toHaveClass(/is-open/);
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
