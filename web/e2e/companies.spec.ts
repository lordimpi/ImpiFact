import { Page, Request, expect, test } from '@playwright/test';
import { seriousViolations } from './support/axe';
import { COMPANIES, USER, mockApi } from './support/mock-api';

// Configuración de la empresa y asistente de paso a producción (feature-companies) contra la Api simulada. Con una
// sola empresa, queda activa al entrar. Nada de esto llama a la DIAN: todas las respuestas son simuladas.

const COMPANY = COMPANIES[0]!;
const NIT = '900123456';
const SECRET = 'clave-secreta-del-p12';
const CERTIFICATE = {
  id: 'cert-1',
  name: 'acme.p12',
  subject: 'CN=ACME SAS',
  issuer: 'CN=GSE CA',
  expiration_date: '2027-06-30',
  certificate_days_left: 225,
  is_active: true,
};

function problem(status: number, code: string, message: string, errors?: object) {
  return {
    status,
    contentType: 'application/problem+json',
    body: JSON.stringify({ status, code, message, correlation_id: 'e2e', errors }),
  };
}

async function signInAt(page: Page, path: string, certificates: unknown[] = [CERTIFICATE]) {
  await mockApi(page, { companies: [COMPANY] });
  await page.route(`**/api/v1/companies/${NIT}/certificates`, (route) =>
    route.fulfill({ json: { success: true, certificates } }),
  );
  await page.goto(`/login?returnUrl=${encodeURIComponent(path)}`);
  await page.getByLabel('Correo electrónico').fill(USER.email);
  await page.getByLabel('Contraseña').fill(USER.password);
  await page.getByRole('button', { name: 'Ingresar' }).click();
}

test('the company data form shows a server field error and then saves', async ({ page }) => {
  const bodies: unknown[] = [];
  await signInAt(page, '/empresa');
  await expect(page).toHaveURL(`/empresas/${COMPANY}/datos`);
  await page.route(`**/api/v1/companies/${NIT}/7`, (route) => {
    bodies.push(route.request().postDataJSON());
    return bodies.length === 1
      ? route.fulfill(
          problem(422, 'validation.failed', 'Los datos no son válidos.', {
            email: ['El email ya está en uso por otra empresa.'],
          }),
        )
      : route.fulfill({ json: { success: true, company: { business_name: 'Acme SAS' } } });
  });

  await expect(page.getByRole('heading', { level: 2, name: 'Datos de la empresa' })).toBeVisible();
  await expect(page.getByLabel('Razón social')).toHaveValue('Acme SAS');
  expect(await seriousViolations(page)).toEqual([]);

  await page.getByLabel('Tipo de organización').selectOption('1');
  await page.getByRole('combobox', { name: 'Régimen' }).selectOption('1');
  await page.getByRole('checkbox', { name: 'O-13 Gran contribuyente' }).check();
  await page.getByLabel('Municipio').selectOption('149');
  await page.getByLabel('Dirección').fill('Calle 1 # 2-3');
  await page.getByLabel('Teléfono').fill('6011234567');
  await page.getByLabel('Correo electrónico').fill('facturas@acme.test');
  await page.getByLabel('Matrícula mercantil').fill('12345-67');
  await page.getByRole('button', { name: 'Guardar' }).click();

  await expect(page.getByText('El email ya está en uso por otra empresa.')).toBeVisible();
  await expect(page.getByLabel('Correo electrónico')).toHaveAttribute('aria-invalid', 'true');
  expect(bodies[0]).toMatchObject({ type_liability_ids: [7], municipality_id: 149 });

  await page.getByLabel('Correo electrónico').fill('otra@acme.test');
  await page.getByRole('button', { name: 'Guardar' }).click();
  await expect(page.getByText('Los cambios se guardaron.')).toBeVisible();
});

test('the certificate upload sends the password once and never shows it', async ({ page }) => {
  const requests: Request[] = [];
  const logs: string[] = [];
  page.on('request', (request) => requests.push(request));
  page.on('console', (message) => logs.push(message.text()));
  await signInAt(page, `/empresas/${COMPANY}/certificado`);
  await page.route(`**/api/v1/companies/${NIT}/certificate`, (route) =>
    route.fulfill({
      json: {
        success: true,
        certificate_days_left: 365,
        certificate: { ...CERTIFICATE, id: 'cert-2', certificate_days_left: 365 },
      },
    }),
  );

  await expect(page.getByText('225 días restantes')).toBeVisible();
  expect(await seriousViolations(page)).toEqual([]);
  const password = page.getByLabel('Contraseña del certificado');
  await expect(password).toHaveAttribute('type', 'password');
  await page.getByLabel('Archivo del certificado').setInputFiles({
    name: 'acme.p12',
    mimeType: 'application/x-pkcs12',
    buffer: Buffer.from('PFX-BYTES'),
  });
  await password.fill(SECRET);
  await page.getByRole('button', { name: 'Cargar certificado' }).click();

  await expect(page.getByText('El certificado se cargó. Vence en 365 días.')).toBeVisible();
  await expect(password).toHaveValue('');
  expect(await page.content()).not.toContain(SECRET);
  expect(logs.join('\n')).not.toContain(SECRET);
  const withSecret = requests.filter((request) => request.postData()?.includes(SECRET));
  expect(
    withSecret.map((request) => `${request.method()} ${new URL(request.url()).pathname}`),
  ).toEqual([`PUT /api/v1/companies/${NIT}/certificate`]);
  expect(withSecret[0]!.postDataJSON()).toEqual({
    certificate: Buffer.from('PFX-BYTES').toString('base64'),
    password: SECRET,
  });
});

test('the production wizard steps follow what the API reports', async ({ page }) => {
  await signInAt(page, '/empresa/produccion', []);
  await expect(page).toHaveURL(`/empresas/${COMPANY}/produccion`);

  const steps = page.getByRole('list', { name: 'Pasos del paso a producción' });
  const step = (title: string) =>
    steps.getByRole('listitem').filter({ has: page.getByRole('heading', { name: title }) });
  await expect(step('Certificado digital').getByText('Pendiente', { exact: true })).toBeVisible();
  await expect(step('Set de pruebas de la DIAN').getByText('Bloqueado')).toBeVisible();
  await expect(step('Producción').getByText('Bloqueado')).toBeVisible();
  expect(await seriousViolations(page)).toEqual([]);

  await page.unroute(`**/api/v1/companies/${NIT}/certificates`);
  await page.route(`**/api/v1/companies/${NIT}/certificates`, (route) =>
    route.fulfill({ json: { success: true, certificates: [CERTIFICATE] } }),
  );
  await page.getByRole('button', { name: 'Volver a revisar' }).click();

  await expect(step('Certificado digital').getByText('Completo')).toBeVisible();
  await expect(
    step('Set de pruebas de la DIAN').getByRole('button', { name: 'Enviar factura de prueba' }),
  ).toBeDisabled();
});

test('switching to production requires an explicit confirmation', async ({ page }) => {
  const bodies: unknown[] = [];
  await signInAt(page, `/empresas/${COMPANY}/produccion`);
  await page.route(`**/api/v1/companies/${NIT}/environment`, (route) => {
    bodies.push(route.request().postDataJSON());
    return route.fulfill({
      json: { success: true, environments: [{ document_kind: 1, type_environment_id: 1 }] },
    });
  });

  await page
    .getByRole('button', { name: 'Pasar a producción: Factura electrónica y notas' })
    .click();
  const dialog = page.getByRole('dialog', { name: 'Confirmar el paso a producción' });
  await expect(dialog.getByText(/efecto fiscal real ante la DIAN/)).toBeVisible();
  expect(await seriousViolations(page)).toEqual([]);
  const confirm = dialog.getByRole('button', { name: 'Pasar a producción' });
  await expect(confirm).toBeDisabled();

  await dialog.getByRole('button', { name: 'Cancelar' }).click();
  await expect(dialog).toBeHidden();
  expect(bodies).toEqual([]);

  await page
    .getByRole('button', { name: 'Pasar a producción: Factura electrónica y notas' })
    .click();
  await dialog.getByRole('checkbox').check();
  await confirm.click();

  await expect(page.getByText('En producción', { exact: true })).toBeVisible();
  expect(bodies).toEqual([{ type_environment_id: 1 }]);
});
