import { Page, Route, expect, test } from '@playwright/test';
import { seriousViolationsInBothThemes } from './support/axe';
import { CRITICAL } from './support/critical';
import { COMPANIES, USER, mockApi } from './support/mock-api';
import { pickOption } from './support/select';

/*
 * Flujo crítico 2 de 5 (docs/Arquitectura frontend.md, secciones 8.2 y 12): alta de una empresa nueva desde el panel,
 * sin el panel PHP. Datos, certificado, software, resolución y el asistente de paso a producción hasta que los seis
 * pasos quedan completos: set de pruebas aceptado y facturación en producción.
 *
 * La Api simulada guarda lo que recibe y lo devuelve en las lecturas (`getCompany`, `getCompanyEnvironments`,
 * `getCompanySoftware`, `listCompanyResolutions`, `getCompanySetupStatus`): la prueba no depende de si una pantalla
 * recuerda lo guardado en la sesión o lo vuelve a leer del Api. Nada de esto llama a la DIAN.
 */

const COMPANY = COMPANIES[0]!;
const NIT = '900123456';
const DV = '7';
const TEST_SET_ID = '9f1c2d3e-0000-4000-8000-00000000c0de';
const INVOICE_KIND = 1;
const PRODUCTION = 1;
const HABILITATION = 2;

const CALCULATION = {
  allowance_charges: [],
  lines: [
    {
      allowance_charges: [],
      line_extension_amount: '10000.00',
      tax_subtotals: [
        { tax_id: 1, percent: '19.00', tax_amount: '1900.00', taxable_amount: '10000.00' },
      ],
    },
  ],
  tax_totals: [
    {
      tax_id: 1,
      tax_amount: '1900.00',
      tax_subtotals: [
        { tax_id: 1, percent: '19.00', tax_amount: '1900.00', taxable_amount: '10000.00' },
      ],
    },
  ],
  legal_monetary_totals: {
    line_extension_amount: '10000.00',
    tax_exclusive_amount: '10000.00',
    tax_inclusive_amount: '11900.00',
    allowance_total_amount: '0.00',
    charge_total_amount: '0.00',
    pre_paid_amount: '0.00',
    payable_amount: '11900.00',
  },
};

interface CompanyState {
  company: Record<string, unknown>;
  certificates: Record<string, unknown>[];
  environment: number;
  software: Record<string, unknown> | null;
  resolutions: Record<string, unknown>[];
  testSetStatuses: string[];
  testSetAccepted: boolean | null;
  writes: string[];
}

function json(route: Route, body: unknown, status = 200) {
  return route.fulfill({ status, json: body });
}

/** Configuración de la empresa en la Api simulada: empieza vacía y cada escritura se ve en las lecturas. */
async function mockCompanySetup(page: Page): Promise<CompanyState> {
  const state: CompanyState = {
    company: {
      id: COMPANY,
      identification_number: NIT,
      dv: DV,
      business_name: 'Acme SAS',
      is_active: true,
    },
    certificates: [],
    environment: HABILITATION,
    software: null,
    resolutions: [],
    // La factura de prueba pasa por En cola antes de que la DIAN la acepte.
    testSetStatuses: ['Queued', 'Accepted'],
    testSetAccepted: null,
    writes: [],
  };
  const environments = () => [
    { document_kind: INVOICE_KIND, type_environment_id: state.environment },
  ];
  const companyComplete = () => state.company['municipality_id'] !== undefined;
  const certificateValid = () => state.certificates.some((entry) => entry['is_active']);

  await page.route(`**/api/v1/companies/${NIT}/**`, async (route) => {
    const request = route.request();
    const method = request.method();
    const path = new URL(request.url()).pathname.replace(`/api/v1/companies/${NIT}`, '');
    if (method !== 'GET') {
      state.writes.push(`${method} ${path}`);
    }

    if (method === 'PUT' && path === `/${DV}`) {
      state.company = { ...state.company, ...request.postDataJSON(), environments: environments() };
      return json(route, { success: true, company: state.company });
    }
    if (method === 'PUT' && path === '/certificate') {
      const certificate = {
        id: 'cert-1',
        name: 'acme.p12',
        subject: 'CN=ACME SAS',
        issuer: 'CN=GSE CA',
        expiration_date: '2027-11-18',
        certificate_days_left: 365,
        is_active: true,
      };
      state.certificates = [certificate];
      return json(route, { success: true, certificate_days_left: 365, certificate });
    }
    if (method === 'GET' && path === '/certificates') {
      return json(route, { success: true, certificates: state.certificates });
    }
    if (method === 'PUT' && path === '/software') {
      const body = request.postDataJSON() as { id: string; test_set_id?: string };
      state.software = {
        document_kind: INVOICE_KIND,
        type_environment_id: state.environment,
        identifier: body.id,
        url: 'https://vpfe-hab.dian.gov.co/WcfDianCustomerServices.svc',
        test_set_id: body.test_set_id ?? null,
      };
      return json(route, { success: true, software: [state.software] });
    }
    if (method === 'GET' && path === '/software') {
      return json(route, {
        software: state.software ? [{ ...state.software, has_pin: true }] : [],
      });
    }
    if (method === 'PUT' && path === '/resolution') {
      const resolution = { id: 'res-1', ...request.postDataJSON() };
      state.resolutions = [resolution];
      return json(route, { success: true, resolution });
    }
    if (method === 'GET' && path === '/resolutions') {
      return json(route, {
        resolutions: state.resolutions.map((entry) => ({
          ...entry,
          kind: 'invoice',
          is_active: true,
          next_number: 990000000,
          remaining: 5000001,
          days_left: 1158,
          has_technical_key: true,
          technical_key: undefined,
        })),
      });
    }
    if (method === 'PUT' && path === '/environment') {
      const body = request.postDataJSON() as { type_environment_id?: number };
      state.environment = body.type_environment_id ?? state.environment;
      return json(route, { success: true, environments: environments() });
    }
    if (method === 'GET' && path === '/environments') {
      return json(route, { environments: environments() });
    }
    if (method === 'GET' && path === '/setup-status') {
      return json(route, {
        company_complete: companyComplete(),
        certificate_valid: certificateValid(),
        software_configured: state.software !== null,
        test_set_id_configured: !!state.software?.['test_set_id'],
        test_set_accepted: state.testSetAccepted,
        environments: environments(),
        resolutions: [
          { kind: 'invoice', type_document_id: 1, valid: state.resolutions.length > 0 },
          { kind: 'credit_note', type_document_id: 4, valid: false },
          { kind: 'debit_note', type_document_id: 5, valid: false },
        ],
      });
    }
    return route.fallback();
  });
  // `getCompany` no lleva barra al final: `**/companies/{nit}/**` no lo toma.
  await page.route(`**/api/v1/companies/${NIT}`, (route) =>
    json(route, { ...state.company, environments: environments() }),
  );

  await page.route('**/api/v1/documents/calculate', (route) => json(route, CALCULATION));
  await page.route(`**/api/v1/documents/invoices/test-set/${TEST_SET_ID}`, (route) => {
    state.writes.push(`POST /documents/invoices/test-set/${TEST_SET_ID}`);
    return json(
      route,
      {
        message: 'Documento recibido',
        QRStr: '',
        urlinvoiceattached: '',
        urlinvoicepdf: '',
        urlinvoicexml: '',
        document: { id: 'test-1', full_number: 'SETP990000000', status: 'Queued' },
      },
      201,
    );
  });
  await page.route('**/api/v1/documents/test-1', (route) => {
    const status = state.testSetStatuses.length > 1 ? state.testSetStatuses.shift()! : 'Accepted';
    if (status === 'Accepted') {
      state.testSetAccepted = true;
    }
    return json(route, {
      id: 'test-1',
      kind: 'invoice',
      full_number: 'SETP990000000',
      status,
      dian_status_code: status === 'Accepted' ? '00' : null,
      dian_status_description: status === 'Accepted' ? 'Procesado Correctamente.' : null,
    });
  });
  return state;
}

const noViolations = { light: [], dark: [] };

async function openSection(page: Page, name: string) {
  await page
    .getByRole('navigation', { name: 'Secciones de la configuración' })
    .getByRole('link', { name })
    .click();
}

test(
  'sets up a new company from scratch until the production wizard is ready',
  CRITICAL,
  async ({ page }) => {
    test.slow();
    await mockApi(page, {
      companies: [COMPANY],
      scopes: ['documents:read', 'documents:write', 'config:write'],
    });
    const api = await mockCompanySetup(page);
    await page.goto(`/login?returnUrl=${encodeURIComponent('/empresa')}`);
    await page.getByLabel('Correo electrónico').fill(USER.email);
    await page.getByLabel('Contraseña').fill(USER.password);
    await page.getByRole('button', { name: 'Ingresar' }).click();

    // 1. Datos de la empresa.
    await expect(page).toHaveURL(`/empresas/${COMPANY}/datos`);
    await expect(
      page.getByRole('heading', { level: 2, name: 'Datos de la empresa' }),
    ).toBeVisible();
    await expect(page.getByLabel('Razón social')).toHaveValue('Acme SAS');
    expect(await seriousViolationsInBothThemes(page)).toEqual(noViolations);
    await pickOption(
      page,
      page.getByRole('combobox', { name: 'Tipo de organización' }),
      'Persona Jurídica y asimiladas',
    );
    await pickOption(page, page.getByRole('combobox', { name: 'Régimen' }), 'Responsable de IVA');
    await page.getByRole('checkbox', { name: 'O-13 Gran contribuyente' }).check();
    // Primero el departamento y luego el municipio, los dos buscando por el nombre.
    const department = page.getByRole('combobox', { name: 'Departamento', exact: true });
    await pickOption(page, department, 'Bogotá', 'bogo');
    const municipality = page.getByRole('combobox', { name: 'Municipio', exact: true });
    await pickOption(page, municipality, 'Bogotá, D.C.', 'bogo');
    await page.getByLabel('Dirección').fill('Calle 1 # 2-3');
    await page.getByLabel('Teléfono').fill('6011234567');
    await page.getByLabel('Correo electrónico').fill('facturas@acme.test');
    await page.getByLabel('Matrícula mercantil').fill('12345-67');
    await page.getByRole('button', { name: 'Guardar' }).click();
    await expect(page.getByText('Los cambios se guardaron.')).toBeVisible();

    // 2. Certificado digital.
    await openSection(page, 'Certificado');
    await expect(page.getByText('La empresa todavía no tiene certificados.')).toBeVisible();
    expect(await seriousViolationsInBothThemes(page)).toEqual(noViolations);
    await page.locator('ui-file-upload input[type="file"]').setInputFiles({
      name: 'acme.p12',
      mimeType: 'application/x-pkcs12',
      buffer: Buffer.from('PFX-BYTES'),
    });
    await page.getByLabel('Contraseña del certificado').fill('clave-del-p12');
    await page.getByRole('button', { name: 'Cargar certificado' }).click();
    await expect(page.getByText('El certificado se cargó. Vence en 365 días.')).toBeVisible();

    // 3. Software con el set de pruebas.
    await openSection(page, 'Software');
    await expect(page.getByRole('heading', { level: 2, name: 'Software' })).toBeVisible();
    expect(await seriousViolationsInBothThemes(page)).toEqual(noViolations);
    await page.getByLabel('Identificador del software').fill('soft-acme-1');
    await page.getByLabel('PIN del software').fill('12345');
    await page.getByLabel('Identificador del set de pruebas (TestSetId)').fill(TEST_SET_ID);
    await page.getByRole('button', { name: 'Guardar' }).click();
    await expect(page.getByRole('table', { name: 'Software registrado' })).toContainText(
      'soft-acme-1',
    );

    // 4. Resolución de habilitación (SETP).
    await openSection(page, 'Resoluciones');
    await expect(
      page.getByRole('heading', { level: 2, name: 'Resoluciones de numeración' }),
    ).toBeVisible();
    expect(await seriousViolationsInBothThemes(page)).toEqual(noViolations);
    await page.getByRole('button', { name: 'Usar el ejemplo de habilitación (SETP)' }).click();
    await expect(page.getByLabel('Prefijo')).toHaveValue('SETP');
    // El ejemplo llena también las fechas de la resolución y de su vigencia.
    await expect(page.getByLabel('Fecha de la resolución')).toHaveValue('19/01/2019');
    await expect(page.getByLabel('Vigente desde')).toHaveValue('19/01/2019');
    await expect(page.getByLabel('Vigente hasta')).toHaveValue('19/01/2030');
    await page.getByRole('button', { name: 'Guardar resolución' }).click();
    await expect(page.getByRole('cell', { name: '18760000001' })).toBeVisible();

    // 5. Asistente: los cuatro primeros pasos completos, el set de pruebas por enviar.
    await openSection(page, 'Paso a producción');
    const steps = page.getByRole('list', { name: 'Pasos del paso a producción' });
    const step = (title: string) =>
      steps.getByRole('listitem').filter({ has: page.getByRole('heading', { name: title }) });
    for (const title of [
      'Datos de la empresa',
      'Certificado digital',
      'Resolución de numeración',
      'Software y set de pruebas',
    ]) {
      await expect(step(title).getByText('Completo', { exact: true })).toBeVisible();
    }
    await expect(
      step('Set de pruebas de la DIAN').getByText('Pendiente', { exact: true }),
    ).toBeVisible();
    expect(await seriousViolationsInBothThemes(page)).toEqual(noViolations);

    // 6. Factura de prueba al set de la DIAN, seguida hasta que la acepta.
    await step('Set de pruebas de la DIAN')
      .getByRole('button', { name: 'Enviar factura de prueba' })
      .click();
    await expect(page.getByText('Factura SETP990000000: Aceptada')).toBeVisible({
      timeout: 15_000,
    });
    await expect(
      step('Set de pruebas de la DIAN').getByText('Completo', { exact: true }),
    ).toBeVisible();

    // 7. Producción, con confirmación explícita.
    await page
      .getByRole('button', { name: 'Pasar a producción: Factura electrónica y notas' })
      .click();
    const dialog = page.getByRole('dialog', { name: 'Confirmar el paso a producción' });
    await expect(dialog).toBeVisible();
    expect(await seriousViolationsInBothThemes(page)).toEqual(noViolations);
    await dialog.getByRole('checkbox').check();
    await dialog.getByRole('button', { name: 'Pasar a producción' }).click();
    await expect(dialog).toBeHidden();

    // Listo: los seis pasos completos.
    await expect(step('Producción').getByText('Completo', { exact: true })).toBeVisible();
    await expect(steps.getByText('Completo', { exact: true })).toHaveCount(6);
    expect(await seriousViolationsInBothThemes(page)).toEqual(noViolations);

    expect(api.writes).toEqual([
      `PUT /${DV}`,
      'PUT /certificate',
      'PUT /software',
      'PUT /resolution',
      `POST /documents/invoices/test-set/${TEST_SET_ID}`,
      'PUT /environment',
    ]);
    expect(api.environment).toBe(PRODUCTION);
  },
);
