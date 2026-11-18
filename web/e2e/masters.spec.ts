import { Page, expect, test } from '@playwright/test';
import { seriousViolations } from './support/axe';
import { CUSTOMER, PRODUCT, mockMasters, problem } from './support/mock-masters';
import { COMPANIES, USER, mockApi } from './support/mock-api';

// Clientes y productos (feature-masters) contra la Api simulada: con una sola empresa, queda activa al entrar.

async function signInAt(page: Page, path: string) {
  await mockApi(page, { companies: [COMPANIES[0]!] });
  const masters = await mockMasters(page);
  await page.goto(`/login?returnUrl=${encodeURIComponent(path)}`);
  await page.getByLabel('Correo electrónico').fill(USER.email);
  await page.getByLabel('Contraseña').fill(USER.password);
  await page.getByRole('button', { name: 'Ingresar' }).click();
  await expect(page).toHaveURL(path);
  return masters;
}

test('the customer list pages, sorts and searches on the server', async ({ page }) => {
  const masters = await signInAt(page, '/clientes');
  masters.respond('GET', '/api/v1/customers', (request, route) =>
    route.fulfill({
      json: {
        items: [{ ...CUSTOMER, name: request.params['cursor'] ? 'Página dos SAS' : CUSTOMER.name }],
        next_cursor: request.params['cursor'] ? null : 'cursor-2',
      },
    }),
  );
  await page.reload();

  const table = page.getByRole('table', { name: 'Clientes' });
  await expect(table.getByText('Ana Comercial SAS')).toBeVisible();
  expect(masters.calls('GET', '/api/v1/customers').at(-1)!.params).toEqual({
    status: 'active',
    sort: 'name',
    limit: '20',
  });

  await page.getByRole('button', { name: 'Siguiente' }).click();
  await expect(table.getByText('Página dos SAS')).toBeVisible();
  expect(masters.calls('GET', '/api/v1/customers').at(-1)!.params).toEqual({
    status: 'active',
    sort: 'name',
    cursor: 'cursor-2',
    limit: '20',
  });

  await page.getByRole('button', { name: 'Identificación' }).click();
  await expect
    .poll(() => masters.calls('GET', '/api/v1/customers').at(-1)!.params['sort'])
    .toBe('identification_number');

  await page
    .getByRole('searchbox', { name: 'Buscar por nombre o número de identificación' })
    .fill('acme');
  await expect(page).toHaveURL('/clientes?q=acme');
  await expect
    .poll(() => masters.calls('GET', '/api/v1/customers').at(-1)!.params)
    .toEqual({ search: 'acme', status: 'active', sort: 'identification_number', limit: '20' });

  await page.getByRole('combobox', { name: 'Estado' }).selectOption('all');
  await expect(page).toHaveURL('/clientes?q=acme&estado=all');
  expect(await seriousViolations(page)).toEqual([]);
});

test('a shared link opens the list with its search and status', async ({ page }) => {
  const masters = await signInAt(page, '/clientes?q=ana&estado=inactive');

  await expect(page.getByRole('table', { name: 'Clientes' })).toBeVisible();
  await expect
    .poll(() => masters.calls('GET', '/api/v1/customers').at(-1)?.params)
    .toEqual({ search: 'ana', status: 'inactive', sort: 'name', limit: '20' });
  await expect(
    page.getByRole('searchbox', { name: 'Buscar por nombre o número de identificación' }),
  ).toHaveValue('ana');
});

test('creates a customer after fixing the check digit the server rejected', async ({ page }) => {
  const masters = await signInAt(page, '/clientes/nuevo');
  let attempts = 0;
  masters.respond('POST', '/api/v1/customers', (request, route) => {
    attempts++;
    if ((request.body as { dv: string }).dv !== '8') {
      return problem(route, 422, 'validation.failed', 'Los datos no son válidos.', {
        dv: ['Error, el digito de verificacion no es valido para este NIT.'],
      });
    }
    return route.fulfill({ status: 201, json: { ...CUSTOMER } });
  });

  await expect(page.getByRole('heading', { level: 1, name: 'Nuevo cliente' })).toBeVisible();
  await page.getByRole('combobox', { name: 'Tipo de identificación' }).selectOption('6');
  await page.getByRole('textbox', { name: 'Número de identificación' }).fill('900123456');
  await page.getByRole('textbox', { name: 'Dígito de verificación (DV)' }).fill('1');
  await page.getByRole('textbox', { name: 'Nombre o razón social' }).fill('Ana Comercial SAS');
  await page
    .getByRole('textbox', { name: 'Correo para recibir facturas' })
    .fill('facturas@ana.test');
  await page.getByRole('button', { name: 'Guardar' }).click();

  await expect(
    page.getByText('Error, el digito de verificacion no es valido para este NIT.'),
  ).toBeVisible();
  await expect(page.getByRole('textbox', { name: 'Dígito de verificación (DV)' })).toHaveAttribute(
    'aria-invalid',
    'true',
  );
  expect(await seriousViolations(page)).toEqual([]);

  await page.getByRole('textbox', { name: 'Dígito de verificación (DV)' }).fill('8');
  await page.getByRole('button', { name: 'Guardar' }).click();

  await expect(page).toHaveURL('/clientes');
  await expect(page.getByText('Cliente guardado.')).toBeVisible();
  expect(attempts).toBe(2);
  expect(masters.calls('POST', '/api/v1/customers').at(-1)!.body).toMatchObject({
    type_document_identification_id: 6,
    identification_number: '900123456',
    dv: '8',
    name: 'Ana Comercial SAS',
    email: 'facturas@ana.test',
  });
});

test('prefills the customer from the DIAN acquirer lookup', async ({ page }) => {
  const masters = await signInAt(page, '/clientes/nuevo');
  masters.respond('GET', '/api/v1/customers/lookup', (_, route) =>
    route.fulfill({ json: { found: true, name: 'ANA COMERCIAL SAS', email: 'fe@ana.test' } }),
  );

  await page.getByRole('combobox', { name: 'Tipo de identificación' }).selectOption('6');
  await page.getByRole('textbox', { name: 'Número de identificación' }).fill('900123456');
  await page.getByRole('button', { name: 'Buscar en la DIAN' }).click();

  await expect(page.getByRole('status')).toContainText(
    'La DIAN tiene registrado a ANA COMERCIAL SAS.',
  );
  await expect(page.getByRole('textbox', { name: 'Nombre o razón social' })).toHaveValue(
    'ANA COMERCIAL SAS',
  );
  await expect(page.getByRole('textbox', { name: 'Correo para recibir facturas' })).toHaveValue(
    'fe@ana.test',
  );
  expect(masters.calls('GET', '/api/v1/customers/lookup').at(-1)!.params).toEqual({
    type: '6',
    number: '900123456',
  });
  expect(await seriousViolations(page)).toEqual([]);
});

test('deactivates a customer only after confirming', async ({ page }) => {
  const masters = await signInAt(page, '/clientes');
  masters.respond('POST', `/api/v1/customers/${CUSTOMER.id}/deactivate`, (_, route) =>
    route.fulfill({ status: 204 }),
  );

  await page.getByRole('button', { name: 'Desactivar a Ana Comercial SAS' }).click();
  const dialog = page.getByRole('dialog', { name: 'Desactivar cliente' });
  await expect(dialog).toContainText('Ana Comercial SAS dejará de aparecer al emitir.');
  expect(await seriousViolations(page)).toEqual([]);
  expect(masters.calls('POST', `/api/v1/customers/${CUSTOMER.id}/deactivate`)).toHaveLength(0);

  await dialog.getByRole('button', { name: 'Desactivar', exact: true }).click();

  await expect(dialog).toBeHidden();
  await expect(page.getByText('Cliente desactivado.')).toBeVisible();
  expect(masters.calls('POST', `/api/v1/customers/${CUSTOMER.id}/deactivate`)).toHaveLength(1);
});

test('the product unit measure comes from the DIAN catalog, page by page', async ({ page }) => {
  await signInAt(page, '/productos/nuevo');

  const unit = page.getByRole('combobox', { name: 'Unidad de medida', exact: true });
  await expect(unit).toHaveValue('70');
  await expect(unit.locator('option', { hasText: 'kilogramo (KGM)' })).toHaveCount(1);
  await expect(unit.locator('option', { hasText: 'litro (LTR)' })).toHaveCount(0);
  await expect(page.getByRole('combobox', { name: 'Impuesto' })).not.toContainText('ReteIVA');
  expect(await seriousViolations(page)).toEqual([]);

  await page.getByRole('button', { name: 'Cargar más unidades' }).click();
  await expect(unit.locator('option', { hasText: 'litro (LTR)' })).toHaveCount(1);
  await expect(page.getByRole('button', { name: 'Cargar más unidades' })).toHaveCount(0);
  await unit.selectOption('821');
  await expect(unit).toHaveValue('821');
});

for (const theme of ['light', 'dark'] as const) {
  test(`the product list and form have no serious accessibility violations (${theme})`, async ({
    page,
  }) => {
    await page.emulateMedia({ colorScheme: theme });
    await signInAt(page, '/productos');
    await expect(
      page.getByRole('table', { name: 'Productos y servicios' }).getByText(PRODUCT.name),
    ).toBeVisible();
    expect(await seriousViolations(page)).toEqual([]);

    await page.getByRole('link', { name: `Editar ${PRODUCT.name}` }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Editar producto' })).toBeVisible();
    await expect(page.getByRole('textbox', { name: 'Tarifa (%)' })).toHaveValue('19');
    expect(await seriousViolations(page)).toEqual([]);

    await page.getByRole('link', { name: 'Volver al listado' }).click();
    await page.getByRole('link', { name: 'Nuevo producto' }).click();
    await page.getByRole('button', { name: 'Guardar' }).click();
    await expect(page.getByText('Este campo es obligatorio.').first()).toBeVisible();
    expect(await seriousViolations(page)).toEqual([]);
  });
}
