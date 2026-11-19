import { Page, expect, test } from '@playwright/test';
import { seriousViolations, seriousViolationsInBothThemes } from './support/axe';
import { CRITICAL } from './support/critical';
import { CUSTOMER, PRODUCT, mockMasters, problem } from './support/mock-masters';
import { COMPANIES, USER, mockApi } from './support/mock-api';

// Clientes y productos (feature-masters) contra la Api simulada: con una sola empresa, queda activa al entrar.

/** Totales que devuelve el Api simulado para la factura del cliente nuevo (el panel no calcula). */
const SUBTOTAL = {
  tax_id: 1,
  percent: '19.00',
  tax_amount: '285000.00',
  taxable_amount: '1500000.00',
};
const CALCULATION = {
  allowance_charges: [],
  lines: [
    { allowance_charges: [], line_extension_amount: '1500000.00', tax_subtotals: [SUBTOTAL] },
  ],
  tax_totals: [{ tax_id: 1, tax_amount: '285000.00', tax_subtotals: [SUBTOTAL] }],
  legal_monetary_totals: {
    line_extension_amount: '1500000.00',
    tax_exclusive_amount: '1500000.00',
    tax_inclusive_amount: '1785000.00',
    allowance_total_amount: '0.00',
    charge_total_amount: '0.00',
    pre_paid_amount: '0.00',
    payable_amount: '1785000.00',
  },
};

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

test('creates a customer after fixing the NIT whose check digit the server rejected', async ({
  page,
}) => {
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
  const number = page.getByRole('textbox', { name: 'Número de identificación' });
  const dv = page.getByRole('textbox', { name: 'Dígito de verificación (DV)' });
  // Con NIT el número solo admite dígitos y el DV se calcula solo.
  await number.pressSequentially('900.123.457');
  await expect(number).toHaveValue('900123457');
  await expect(dv).toHaveValue('5');
  await expect(dv).toHaveAttribute('readonly', '');
  await page.getByRole('textbox', { name: 'Nombre o razón social' }).fill('Ana Comercial SAS');
  await page
    .getByRole('textbox', { name: 'Correo para recibir facturas' })
    .fill('facturas@ana.test');
  await page.getByRole('button', { name: 'Guardar' }).click();

  await expect(
    page.getByText('Error, el digito de verificacion no es valido para este NIT.'),
  ).toBeVisible();
  await expect(number).toHaveAttribute('aria-invalid', 'true');
  expect(await seriousViolations(page)).toEqual([]);

  await number.fill('900123456');
  await expect(dv).toHaveValue('8');
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

// Flujo crítico 5 de 5: cliente nuevo con los datos de la DIAN y su uso en una factura.
test(
  'creates a customer from the DIAN lookup and issues an invoice to it',
  CRITICAL,
  async ({ page }) => {
    const created = {
      ...CUSTOMER,
      id: '6a1f0000-0000-4000-8000-000000000077',
      identification_number: '901555777',
      dv: '4',
      name: 'NUEVO CLIENTE DIAN SAS',
      email: 'fe@nuevo.test',
    };
    const customers: (typeof CUSTOMER)[] = [];
    const issued: unknown[] = [];
    const masters = await signInAt(page, '/clientes');
    masters.respond('GET', '/api/v1/customers', (_, route) =>
      route.fulfill({ json: { items: customers, next_cursor: null } }),
    );
    masters.respond('GET', '/api/v1/customers/lookup', (_, route) =>
      route.fulfill({ json: { found: true, name: created.name, email: created.email } }),
    );
    masters.respond('POST', '/api/v1/customers', (_, route) => {
      customers.push(created);
      return route.fulfill({ status: 201, json: created });
    });
    masters.respond('GET', `/api/v1/customers/${created.id}`, (_, route) =>
      route.fulfill({ json: created }),
    );
    await page.route('**/api/v1/documents/**', (route) => {
      const url = new URL(route.request().url());
      if (url.pathname === '/api/v1/documents/calculate') {
        return route.fulfill({ json: CALCULATION });
      }
      if (url.pathname === '/api/v1/documents/invoices') {
        issued.push(route.request().postDataJSON());
        return route.fulfill({
          status: 201,
          json: {
            message: 'Documento recibido',
            QRStr: '',
            urlinvoiceattached: '',
            urlinvoicepdf: '',
            urlinvoicexml: '',
            document: { id: 'doc-77', full_number: 'SETP990000077', status: 'Queued' },
          },
        });
      }
      return route.fulfill({
        json: {
          id: 'doc-77',
          kind: 'invoice',
          full_number: 'SETP990000077',
          issue_date: '2026-11-18',
          issue_time: '09:00:00',
          status: 'Accepted',
          email_status: 'Sent',
          cufe: 'cufe-77',
          customer: created,
          totals: CALCULATION.legal_monetary_totals,
          lines: [],
          taxes: [],
        },
      });
    });

    await expect(page.getByRole('heading', { level: 1, name: 'Clientes' })).toBeVisible();
    expect(await seriousViolationsInBothThemes(page)).toEqual({ light: [], dark: [] });
    await page.getByRole('link', { name: 'Nuevo cliente' }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Nuevo cliente' })).toBeVisible();
    await page.getByRole('combobox', { name: 'Tipo de identificación' }).selectOption('6');
    await page.getByRole('textbox', { name: 'Número de identificación' }).fill('901555777');
    await page.getByRole('button', { name: 'Buscar en la DIAN' }).click();
    await expect(page.getByRole('status')).toContainText(
      'La DIAN tiene registrado a NUEVO CLIENTE DIAN SAS.',
    );
    await expect(page.getByRole('textbox', { name: 'Dígito de verificación (DV)' })).toHaveValue(
      '4',
    );
    expect(await seriousViolationsInBothThemes(page)).toEqual({ light: [], dark: [] });
    await page.getByRole('button', { name: 'Guardar' }).click();

    await expect(page).toHaveURL('/clientes');
    await expect(page.getByText('Cliente guardado.')).toBeVisible();
    await expect(
      page.getByRole('table', { name: 'Clientes' }).getByText('NUEVO CLIENTE DIAN SAS'),
    ).toBeVisible();
    expect(masters.calls('POST', '/api/v1/customers').at(-1)!.body).toMatchObject({
      type_document_identification_id: 6,
      identification_number: '901555777',
      dv: '4',
      name: 'NUEVO CLIENTE DIAN SAS',
      email: 'fe@nuevo.test',
    });

    await page.getByRole('link', { name: 'Documentos' }).click();
    await page.getByRole('link', { name: 'Emitir factura' }).click();
    await page.getByLabel('Buscar cliente').fill('901555');
    await page.getByRole('button', { name: /Elegir NUEVO CLIENTE DIAN SAS/ }).click();
    await expect(page.getByText('Cliente elegido')).toBeVisible();
    await page.getByLabel('Agregar un producto').fill('café');
    await page.getByRole('button', { name: /Agregar Café molido 500 g/ }).click();
    await expect(page.getByTestId('payable')).toContainText('1.785.000,00');
    await page.getByRole('button', { name: 'Emitir factura' }).click();

    await expect(page).toHaveURL('/documentos/doc-77');
    await expect(
      page.getByRole('heading', { level: 1, name: 'Factura SETP990000077' }),
    ).toBeVisible();
    expect(issued).toHaveLength(1);
    expect(issued[0]).toMatchObject({
      customer: {
        identification_number: '901555777',
        dv: '4',
        name: 'NUEVO CLIENTE DIAN SAS',
        email: 'fe@nuevo.test',
      },
    });
  },
);

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
  await expect(unit).toHaveValue('unidad (94)');
  await expect(page.getByRole('combobox', { name: 'Impuesto' })).not.toContainText('ReteIVA');
  expect(await seriousViolations(page)).toEqual([]);

  // La simulación da dos filas por página: al abrir la lista, el final queda a la vista y la página siguiente
  // llega sola, sin botón «Cargar más».
  await unit.click();
  const list = page.getByRole('listbox', { name: 'Unidad de medida' });
  await expect(list.getByRole('option')).toHaveText([
    'unidad (94)',
    'kilogramo (KGM)',
    'litro (LTR)',
  ]);
  await expect(page.getByRole('button', { name: /Cargar más/ })).toHaveCount(0);
  await list.getByRole('option', { name: 'litro (LTR)' }).click();
  await expect(unit).toHaveValue('litro (LTR)');

  // También se busca escribiendo y se elige con el teclado.
  await unit.fill('kilo');
  await expect(list.getByRole('option')).toHaveText(['kilogramo (KGM)', 'litro (LTR)']);
  await unit.press('ArrowDown');
  await unit.press('Enter');
  await expect(unit).toHaveValue('kilogramo (KGM)');
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
