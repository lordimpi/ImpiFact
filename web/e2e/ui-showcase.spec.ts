import AxeBuilder from '@axe-core/playwright';
import { Page, expect, test } from '@playwright/test';

// Vitrina de ui (/dev/ui, solo en desarrollo): accesibilidad con axe y eventos de los componentes.
// WCAG = pautas de accesibilidad web; se revisan los niveles A y AA hasta la versión 2.2.
const WCAG_TAGS = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];

async function seriousViolations(page: Page) {
  const results = await new AxeBuilder({ page }).withTags(WCAG_TAGS).analyze();
  return results.violations
    .filter((violation) => violation.impact === 'serious' || violation.impact === 'critical')
    .map((violation) => ({
      id: violation.id,
      impact: violation.impact,
      targets: violation.nodes.map((node) => node.target.join(' ')),
    }));
}

async function openShowcase(page: Page) {
  await page.goto('/dev/ui');
  await expect(
    page.getByRole('heading', { level: 1, name: 'Vitrina de componentes' }),
  ).toBeVisible();
}

// Fondo esperado de cada tema (--if-color-bg en tokens.css): prueba que el tema realmente se aplicó.
const BACKGROUNDS = { light: 'rgb(255, 255, 255)', dark: 'rgb(18, 22, 29)' } as const;

const bodyBackground = (page: Page) =>
  page.evaluate(() => getComputedStyle(document.body).backgroundColor);

for (const theme of ['light', 'dark'] as const) {
  test(`has no serious accessibility violations in the ${theme} theme`, async ({ page }) => {
    await page.emulateMedia({ colorScheme: theme });
    await openShowcase(page);

    expect(await bodyBackground(page)).toBe(BACKGROUNDS[theme]);
    expect(await seriousViolations(page)).toEqual([]);
  });
}

test('the theme selector overrides the system preference', async ({ page }) => {
  await page.emulateMedia({ colorScheme: 'light' });
  await openShowcase(page);
  expect(await bodyBackground(page)).toBe(BACKGROUNDS.light);

  await page.getByRole('radio', { name: 'Oscuro' }).check();

  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  expect(await bodyBackground(page)).toBe(BACKGROUNDS.dark);
  expect(await seriousViolations(page)).toEqual([]);
});

test('the open dialog traps focus, closes with Escape and has no violations', async ({ page }) => {
  await openShowcase(page);

  await page.getByRole('button', { name: 'Abrir diálogo' }).click();
  const dialog = page.getByRole('dialog', { name: 'Anular factura' });
  await expect(dialog).toBeVisible();
  await expect(dialog.getByRole('button', { name: 'Cerrar' })).toBeVisible();
  expect(await seriousViolations(page)).toEqual([]);

  for (let i = 0; i < 5; i++) await page.keyboard.press('Tab');
  expect(await dialog.evaluate((element) => element.contains(document.activeElement))).toBe(true);

  await page.keyboard.press('Escape');
  await expect(dialog).toBeHidden();
  await expect(page.getByTestId('last-event')).toHaveText('closed');
});

test('the table emits server-side sort, filter and page requests', async ({ page }) => {
  await openShowcase(page);
  const table = page.getByRole('table', { name: 'Documentos emitidos', exact: true });
  const lastEvent = page.getByTestId('last-event');
  const firstNumbers = () => table.locator('tbody tr td:first-child').allTextContents();
  const before = await firstNumbers();

  await table.getByRole('button', { name: /Número/ }).click();
  await expect(lastEvent).toHaveText('sortChange {"key":"number","direction":"asc"}');
  await expect(table.getByRole('columnheader', { name: /Número/ })).toHaveAttribute(
    'aria-sort',
    'ascending',
  );
  // La tabla no reordena en el navegador: el orden lo aplica el Api.
  expect(await firstNumbers()).toEqual(before);

  await table.getByRole('textbox', { name: 'Filtrar por cliente' }).fill('Alfa');
  await table.getByRole('textbox', { name: 'Filtrar por cliente' }).press('Enter');
  await expect(lastEvent).toHaveText('filterChange {"customer":"Alfa"}');

  await page
    .getByRole('navigation', { name: 'Paginación', exact: true })
    .getByRole('button', { name: 'Página 2' })
    .click();
  await expect(lastEvent).toHaveText('pageChange {"kind":"pages","page":2}');
  await expect(page.getByRole('button', { name: 'Página 2' })).toHaveAttribute(
    'aria-current',
    'page',
  );
});

test('the cursor paginator emits the opaque next cursor', async ({ page }) => {
  await openShowcase(page);
  const nav = page.getByRole('navigation', { name: 'Paginación con cursor' });

  await expect(nav.getByRole('button', { name: 'Anterior' })).toBeDisabled();
  await nav.getByRole('button', { name: 'Siguiente' }).click();

  await expect(page.getByTestId('last-event')).toHaveText(
    'pageChange {"kind":"cursor","cursor":"eyJpZCI6M30","direction":"next"}',
  );
});

test('the error state retries', async ({ page }) => {
  await openShowcase(page);

  await page
    .getByRole('table', { name: 'Documentos emitidos, con error' })
    .getByRole('button', { name: 'Reintentar' })
    .click();

  await expect(page.getByTestId('last-event')).toHaveText('retry table');
});
