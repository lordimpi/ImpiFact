import AxeBuilder from '@axe-core/playwright';
import { Page, expect, test } from '@playwright/test';
import { seriousViolationsInBothThemes } from './support/axe';
import { pickOption } from './support/select';

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

// Light by default: the page background is --if-color-page (tokens.css) whatever the system prefers.
const PAGE_BACKGROUND = 'rgb(244, 246, 250)';
const DARK_PAGE_BACKGROUND = 'rgb(34, 42, 55)';

test('renders every component in the dark theme without serious accessibility violations', async ({
  page,
}) => {
  await page.addInitScript(() => localStorage.setItem('impifact.theme', 'dark'));
  await openShowcase(page);

  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  expect(await bodyBackground(page)).toBe(DARK_PAGE_BACKGROUND);
  expect(await seriousViolations(page)).toEqual([]);
});

const bodyBackground = (page: Page) =>
  page.evaluate(() => getComputedStyle(document.body).backgroundColor);

for (const colorScheme of ['light', 'dark'] as const) {
  test(`renders the light theme without serious accessibility violations under a ${colorScheme} system preference`, async ({
    page,
  }) => {
    await page.emulateMedia({ colorScheme });
    await openShowcase(page);

    await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
    expect(await bodyBackground(page)).toBe(PAGE_BACKGROUND);
    expect(await seriousViolations(page)).toEqual([]);
  });
}

test('the page title shows its subtitle, breadcrumb and action', async ({ page }) => {
  await openShowcase(page);

  await expect(
    page.getByText('Página solo para desarrollo: cada componente de ui en cada estado.'),
  ).toBeVisible();
  const breadcrumb = page.getByRole('navigation', { name: 'Ruta de navegación' });
  await expect(breadcrumb.getByRole('link', { name: 'Inicio' })).toBeVisible();
  await expect(breadcrumb.getByText('Vitrina')).toHaveAttribute('aria-current', 'page');
  await page.getByRole('button', { name: 'Nueva factura' }).click();
  await expect(page.getByTestId('last-event')).toHaveText('pressed page action');
});

test('the field help is a small Optimus info icon with a 24px target and a tooltip', async ({
  page,
}) => {
  await openShowcase(page);
  const help = page.getByRole('button', { name: 'Ayuda sobre NIT' });

  const target = await help.boundingBox();
  expect(target?.width).toBeGreaterThanOrEqual(24);
  expect(target?.height).toBeGreaterThanOrEqual(24);
  const icon = await help.locator('svg[data-p-icon="info-circle"]').boundingBox();
  expect(icon?.width).toBeLessThanOrEqual(17);

  await help.hover();
  await expect(page.getByRole('tooltip')).toBeVisible();
  await help.focus();
  await page.keyboard.press('Escape');
  await expect(page.getByRole('tooltip')).toBeHidden();
});

test('the stats pair each label with its value', async ({ page }) => {
  await openShowcase(page);

  const stat = page.locator('ui-stat').filter({ hasText: 'Facturas emitidas' });
  await expect(stat.getByRole('term')).toHaveText('Facturas emitidas');
  await expect(stat.getByRole('definition').first()).toHaveText('1.284');
});

test('the routed tabs mark the active section with aria-current', async ({ page }) => {
  await openShowcase(page);
  const tabs = page.getByRole('navigation', { name: 'Secciones de la empresa' });

  await tabs.getByRole('link', { name: 'Resoluciones' }).click();

  await expect(page).toHaveURL(/\/dev\/ui\?tab=resolutions$/);
  await expect(tabs.getByRole('link', { name: 'Resoluciones' })).toHaveAttribute(
    'aria-current',
    'page',
  );
  await expect(tabs.getByRole('link', { name: 'Datos' })).not.toHaveAttribute('aria-current');
  await page.keyboard.press('Tab');
  await expect(tabs.getByRole('link', { name: 'Certificado' })).toBeFocused();
});

test('the toasts appear, are announced and can be dismissed', async ({ page }) => {
  await openShowcase(page);

  await page.getByRole('button', { name: 'Mostrar avisos' }).click();
  const region = page.getByRole('region', { name: 'Avisos' });
  await expect(region.getByText('La factura SETP990000004 se envió a la DIAN.')).toBeVisible();
  await expect(region.getByText('7f3c9a2e-41b0')).toBeVisible();
  expect(await seriousViolations(page)).toEqual([]);

  await region.getByRole('button', { name: 'Cerrar' }).first().click();
  await expect(region.getByText('La factura SETP990000004 se envió a la DIAN.')).toBeHidden();
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

test('the Optimus select, the locked combobox, the date picker and the file chooser work and pass axe', async ({
  page,
}) => {
  await openShowcase(page);
  const lastEvent = page.getByTestId('last-event');
  const department = page.getByRole('combobox', { name: 'Departamento' });
  const municipality = page.getByRole('combobox', { name: 'Municipio' });

  // S9: the municipality stays locked (out of the tab order) until a department is chosen.
  await expect(municipality).toHaveAttribute('aria-disabled', 'true');
  await expect(municipality).toHaveAttribute('tabindex', '-1');
  await expect(page.getByText('Seleccione primero el departamento.')).toBeVisible();

  // The select works with the keyboard: Enter opens, the arrows move and Enter chooses.
  await department.focus();
  await page.keyboard.press('Enter');
  await expect(page.getByRole('listbox', { name: 'Departamento' })).toBeVisible();
  await page.keyboard.press('ArrowDown');
  await page.keyboard.press('ArrowDown');
  await page.keyboard.press('Enter');
  await expect(department).toHaveText('Antioquia');
  await expect(lastEvent).toHaveText('department "5"');

  await expect(municipality).toHaveAttribute('aria-disabled', 'false');
  await pickOption(page, municipality, 'Medellín', 'mede');
  await expect(lastEvent).toHaveText('municipality "5001"');

  // The date picker takes a typed date and gives the ISO date; its calendar is in Spanish.
  const expiresAt = page.getByRole('combobox', { name: 'Vence el' });
  await expiresAt.pressSequentially('15/12/2026');
  await expect(lastEvent).toHaveText('expiresAt "2026-12-15"');
  await page.getByRole('button', { name: 'Elegir la fecha' }).click();
  await expect(page.getByText('diciembre', { exact: true })).toBeVisible();
  expect(await seriousViolationsInBothThemes(page)).toEqual({ light: [], dark: [] });
  await page.keyboard.press('Escape');

  // The file chooser names the chosen file and rejects other types in Spanish.
  const choose = page.getByRole('button', { name: 'Certificado digital Seleccionar archivo' });
  await expect(choose).toBeVisible();
  const chooser = page.waitForEvent('filechooser');
  await choose.click();
  await (
    await chooser
  ).setFiles({ name: 'notas.txt', mimeType: 'text/plain', buffer: Buffer.from('x') });
  await expect(page.locator('ui-file-upload').getByRole('alert')).toHaveText(
    '«notas.txt» no es de un tipo permitido. Use un archivo .p12, .pfx.',
  );
  await page.locator('ui-file-upload input[type="file"]').setInputFiles({
    name: 'empresa.p12',
    mimeType: 'application/x-pkcs12',
    buffer: Buffer.from('x'),
  });
  await expect(page.getByText('empresa.p12', { exact: true })).toBeVisible();
  await expect(lastEvent).toHaveText('file "empresa.p12"');

  // Open list over the page, in both themes.
  await department.click();
  await expect(page.getByRole('listbox', { name: 'Departamento' })).toBeVisible();
  expect(await seriousViolationsInBothThemes(page)).toEqual({ light: [], dark: [] });
});
