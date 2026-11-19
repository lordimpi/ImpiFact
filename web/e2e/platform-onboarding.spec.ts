import { Route, expect, test } from '@playwright/test';
import { seriousViolations } from './support/axe';
import { sessionTokens } from './support/mock-api';
import { signInAt } from './support/sign-in';

/*
 * Alta de una empresa desde la plataforma (feature-platform) contra la Api simulada, sin el panel PHP: el
 * administrador de la plataforma crea la empresa, ve su llave de API una sola vez, crea su primer dueño y ese dueño
 * inicia sesión en el panel. La Api simulada guarda lo que recibe: la lista de empresas muestra la nueva y el login
 * solo acepta al dueño con la contraseña con la que se creó.
 */

const asPlatformAdmin = { companies: [], roles: ['platform_admin'], scopes: ['platform:*'] };

const COMPANY_ID = '3f2a9c1e-0000-4000-8000-0000000000aa';
const TOKEN = 'ifk_live_5e4d3c2b1a0f9e8d7c6b5a4f3e2d1c0b';
const OWNER = { email: 'dueno@nueva.test', password: 'clave-del-dueno-123' };
const OWNER_SCOPES = ['documents:read', 'documents:write', 'config:write'];

interface StoredUser {
  company_id: string;
  email: string;
  name: string;
  phone: string | null;
  password: string;
  role: string;
}

test('creates a company, shows its key once, creates its owner and the owner signs in', async ({
  page,
}) => {
  await signInAt(page, '/plataforma/empresas', asPlatformAdmin);
  await page.context().grantPermissions(['clipboard-read', 'clipboard-write']);

  const companies: Record<string, unknown>[] = [];
  const created: { url: string; body: Record<string, unknown> }[] = [];
  const users: StoredUser[] = [];
  let ownerSignedIn = false;

  await page.route('**/api/v1/plans', (route) =>
    route.fulfill({ json: { success: true, data: [] } }),
  );
  await page.route(/\/api\/v1\/platform\/companies(\?|$)/, (route) =>
    route.fulfill({ json: { items: companies, next_cursor: null } }),
  );
  await page.route(/\/api\/v1\/companies\/\d+\/\d$/, (route) => {
    if (route.request().method() !== 'POST') {
      return route.fallback();
    }
    const body = route.request().postDataJSON() as Record<string, unknown>;
    created.push({ url: route.request().url(), body });
    companies.push({
      id: COMPANY_ID,
      name: body['business_name'],
      nit: '901234567',
      dv: '7',
      email: body['email'],
      is_active: true,
      plan_id: null,
      plan_name: null,
    });
    return route.fulfill({
      status: 201,
      json: {
        success: true,
        created: true,
        message: 'Empresa creada.',
        token: TOKEN,
        company: {
          id: COMPANY_ID,
          identification_number: '901234567',
          dv: '7',
          business_name: body['business_name'],
          environments: [],
        },
      },
    });
  });
  await page.route('**/api/v1/users', (route) => {
    const body = route.request().postDataJSON() as StoredUser;
    users.push(body);
    return route.fulfill({
      status: 201,
      json: {
        id: 'u-owner',
        email: body.email,
        name: body.name,
        role: body.role,
        company_id: body.company_id,
      },
    });
  });
  await page.reload();

  // 1. Alta de la empresa.
  await expect(
    page.getByRole('heading', { level: 1, name: 'Empresas de la plataforma' }),
  ).toBeVisible();
  await expect(page.getByText('La plataforma aún no tiene empresas')).toBeVisible();
  await page.getByRole('button', { name: 'Nueva empresa' }).click();
  await expect(page).toHaveURL('/plataforma/empresas/nueva');
  await expect(page.getByRole('heading', { level: 1, name: 'Nueva empresa' })).toBeVisible();
  await expect(page.getByRole('checkbox', { name: 'O-13 Gran contribuyente' })).toBeVisible();
  expect(await seriousViolations(page)).toEqual([]);

  // El NIT solo admite dígitos y el DV se calcula solo.
  await page.getByRole('textbox', { name: /^NIT/ }).pressSequentially('901.234.567');
  await expect(page.getByRole('textbox', { name: /^NIT/ })).toHaveValue('901234567');
  await expect(page.getByLabel('Dígito de verificación')).toHaveValue('7');
  await page.getByLabel('Razón social').fill('Nueva Empresa SAS');
  await page.getByLabel('Tipo de organización').selectOption('1');
  await page.getByRole('combobox', { name: 'Régimen' }).selectOption('1');
  await page.getByRole('checkbox', { name: 'O-13 Gran contribuyente' }).check();
  // Primero el departamento; el municipio se busca entre los suyos y se elige en la lista.
  // El departamento también se busca escribiendo, sin importar tildes ni mayúsculas.
  const department = page.getByRole('combobox', { name: 'Departamento', exact: true });
  await department.fill('cauca');
  await expect(page.getByRole('listbox').getByRole('option')).toHaveText([
    'Cauca',
    'Valle del Cauca',
  ]);
  await page.getByRole('option', { name: 'Cauca', exact: true }).click();
  await expect(department).toHaveValue('Cauca');
  const municipality = page.getByRole('combobox', { name: 'Municipio', exact: true });
  await municipality.fill('popa');
  await page.getByRole('option', { name: 'Popayán' }).click();
  await expect(municipality).toHaveValue('Popayán');
  await page.getByLabel('Dirección').fill('Calle 10 # 20-30');
  await page.getByLabel('Teléfono').fill('6017654321');
  await page.getByLabel('Correo electrónico').fill('facturas@nueva.test');
  await page.getByLabel('Matrícula mercantil').fill('0000000-00');
  await page.getByRole('button', { name: 'Crear empresa' }).click();

  // 2. La llave de la empresa se muestra una sola vez.
  const keyDialog = page.getByRole('dialog', { name: 'Copie la llave de la empresa' });
  await expect(keyDialog.getByText(TOKEN)).toBeVisible();
  expect(created).toHaveLength(1);
  expect(created[0]!.url).toContain('/api/v1/companies/901234567/7');
  expect(created[0]!.body).toMatchObject({
    business_name: 'Nueva Empresa SAS',
    type_document_identification_id: 6,
    type_liability_ids: [7],
    municipality_id: 362,
  });
  expect(await seriousViolations(page)).toEqual([]);
  await keyDialog.getByRole('button', { name: 'Copiar llave' }).click();
  await expect(keyDialog.getByText('Llave copiada.')).toBeVisible();
  expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(TOKEN);
  await keyDialog.getByRole('button', { name: 'Ya la copié' }).click();
  await expect(keyDialog).toHaveCount(0);
  await expect(page.locator('body')).not.toContainText(TOKEN);
  await expect(
    page.getByText(/Se creó la empresa Nueva Empresa SAS \(NIT 901234567-7\)/),
  ).toBeVisible();

  // 3. Enseguida se crea el dueño.
  const userDialog = page.getByRole('dialog', { name: 'Crear usuario de Nueva Empresa SAS' });
  await expect(userDialog).toBeVisible();
  expect(await seriousViolations(page)).toEqual([]);
  await userDialog.getByLabel('Correo electrónico').fill(OWNER.email);
  await userDialog.getByLabel('Nombre').fill('Ana Dueña');
  await userDialog.getByLabel('Contraseña').fill(OWNER.password);
  await expect(userDialog.getByRole('combobox', { name: 'Rol' })).toHaveValue('owner');
  await userDialog.getByRole('button', { name: 'Crear usuario' }).click();
  await expect(userDialog).toHaveCount(0);
  await expect(
    page.getByText(
      'Se creó el usuario dueno@nueva.test como dueño de Nueva Empresa SAS; ya puede iniciar sesión.',
    ),
  ).toBeVisible();
  expect(users).toEqual([
    {
      company_id: COMPANY_ID,
      email: OWNER.email,
      name: 'Ana Dueña',
      phone: null,
      password: OWNER.password,
      role: 'owner',
    },
  ]);

  // 4. La lista de empresas ya trae la nueva.
  await page.getByRole('link', { name: 'Volver a las empresas' }).click();
  const table = page.getByRole('table', { name: 'Empresas de la plataforma' });
  await expect(table.getByText('Nueva Empresa SAS')).toBeVisible();
  await expect(table.getByText('901234567-7')).toBeVisible();

  // 5. El dueño inicia sesión con lo que se creó (la Api simulada solo conoce a ese usuario).
  const isOwner = (route: Route) => {
    const body = route.request().postDataJSON() as { email?: string; password?: string } | null;
    return users.some((user) => user.email === body?.email && user.password === body?.password);
  };
  const ownerTokens = () => sessionTokens(COMPANY_ID, [COMPANY_ID], ['owner'], OWNER_SCOPES);
  await page.route('**/api/v1/auth/login', (route) => {
    if (!isOwner(route)) {
      return route.fallback();
    }
    ownerSignedIn = true;
    return route.fulfill({ json: ownerTokens() });
  });
  await page.route('**/api/v1/auth/refresh', (route) =>
    ownerSignedIn ? route.fulfill({ json: ownerTokens() }) : route.fallback(),
  );
  await page.route('**/api/v1/me/companies', (route) =>
    ownerSignedIn
      ? route.fulfill({
          json: {
            items: [
              {
                id: COMPANY_ID,
                name: 'Nueva Empresa SAS',
                nit: '901234567',
                dv: '7',
                roles: ['owner'],
                is_active: true,
              },
            ],
          },
        })
      : route.fallback(),
  );

  await page.getByRole('button', { name: /Mi cuenta/ }).click();
  await page.getByRole('button', { name: 'Cerrar sesión' }).click();
  await expect(page).toHaveURL('/login');
  await page.getByLabel('Correo electrónico').fill(OWNER.email);
  await page.getByLabel('Contraseña').fill(OWNER.password);
  await page.getByRole('button', { name: 'Ingresar' }).click();

  await expect(page).toHaveURL('/');
  await expect(page.getByRole('heading', { level: 1, name: 'Tablero' })).toBeVisible();
  await expect(page.getByRole('button', { name: /Mi cuenta/ })).toContainText(OWNER.email);
  expect(ownerSignedIn).toBe(true);
});
