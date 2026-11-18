import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { fireEvent, screen, waitFor, within } from '@testing-library/angular';
import { catalogRequests } from '../testing/catalog-fixtures';
import { expectRequest, problem, renderPlatformRoute } from '../testing/platform-testing';

const SECRET = 'ifk_live_0a1b2c3d4e5f60718293a4b5c6d7e8f9';
const CREATE_URL = '/api/v1/companies/900123456/8';

const field = (label: RegExp) => screen.getByLabelText(label) as HTMLInputElement;
const type = (label: RegExp, value: string) => fireEvent.input(field(label), { target: { value } });
const choose = (label: RegExp, value: string) => {
  // Signal Forms lee el evento `input` del <select>; el navegador manda también `change`.
  const select = screen.getByLabelText(label) as HTMLSelectElement;
  fireEvent.change(select, { target: { value } });
  fireEvent.input(select, { target: { value } });
};
const optionTexts = (label: RegExp) =>
  Array.from((screen.getByLabelText(label) as HTMLSelectElement).options).map((option) =>
    option.textContent?.trim(),
  );
const create = () => fireEvent.click(screen.getByRole('button', { name: 'Crear empresa' }));
const municipality = () => screen.getByRole('combobox', { name: /^Municipio/ }) as HTMLInputElement;
const municipalityOptions = () =>
  within(screen.getByRole('listbox'))
    .queryAllByRole('option')
    .map((option) => option.textContent?.trim());

/** Elige el municipio en el combobox: escribe para buscar y hace clic en la opción. */
async function pickMunicipality(search: string, name: string) {
  fireEvent.input(municipality(), { target: { value: search } });
  fireEvent.click(await screen.findByRole('option', { name }));
}

async function open() {
  const rendered = await renderPlatformRoute('/plataforma/empresas/nueva');
  await screen.findByRole('heading', { level: 1, name: 'Nueva empresa' });
  // Las listas salen de los catálogos del Api.
  await screen.findByRole('checkbox', { name: 'O-13 Gran contribuyente' });
  await waitFor(() => expect(optionTexts(/^Departamento/)).toContain('Bogotá'));
  return rendered;
}

async function fillAll() {
  type(/^NIT/, '900123456');
  type(/^Razón social/, 'Acme SAS');
  choose(/^Tipo de organización/, '1');
  choose(/^Régimen/, '1');
  fireEvent.click(screen.getByRole('checkbox', { name: 'O-13 Gran contribuyente' }));
  choose(/^Departamento/, '4');
  await pickMunicipality('bogo', 'Bogotá, D.C.');
  type(/^Dirección/, 'Calle 1 # 2-3');
  type(/^Teléfono/, '6011234567');
  type(/^Correo electrónico/, 'facturas@acme.test');
  type(/^Matrícula mercantil/, '12345-67');
}

const CREATED = {
  success: true,
  created: true,
  message: 'Empresa creada.',
  token: SECRET,
  company: {
    id: 'c-9',
    identification_number: '900123456',
    dv: '8',
    business_name: 'Acme SAS',
    environments: [],
  },
};

describe('NewCompanyPage', () => {
  it('fills the lists from the DIAN catalogs and searches the municipalities of the chosen department', async () => {
    await open();
    expect(optionTexts(/^Tipo de identificación/)).toContain('NIT');
    expect((screen.getByLabelText(/^Tipo de identificación/) as HTMLSelectElement).value).toBe('6');
    expect(optionTexts(/^Régimen/)).toContain('Responsable de IVA');
    expect(screen.getByText('Seleccione primero el departamento.')).toBeTruthy();
    expect(catalogRequests.some((request) => request.url.endsWith('/municipalities'))).toBe(false);

    choose(/^Departamento/, '2');
    await waitFor(() => expect(catalogRequests.at(-1)?.params.get('department_id')).toBe('2'));
    fireEvent.input(municipality(), { target: { value: 'medel' } });
    await waitFor(() => expect(catalogRequests.at(-1)?.params.get('q')).toBe('medel'));
    expect(catalogRequests.at(-1)?.params.get('department_id')).toBe('2');
    await waitFor(() => expect(municipalityOptions()).toEqual(['Medellín']));
    expect(screen.queryByRole('button', { name: /Cargar más/ })).toBeNull();
  });

  it('accepts only digits in the NIT and the phone and computes the check digit', async () => {
    await open();
    const dv = field(/^Dígito de verificación/);
    expect(dv.readOnly).toBe(true);
    expect(field(/^NIT/).getAttribute('inputmode')).toBe('numeric');

    type(/^NIT/, '900.123.456abc');
    type(/^Teléfono/, '(601) 123-4567');

    expect(field(/^NIT/).value).toBe('900123456');
    expect(dv.value).toBe('8');
    expect(field(/^Teléfono/).value).toBe('6011234567');
    type(/^NIT/, '800197268');
    expect(dv.value).toBe('4');
  });

  it('explains every field in a tooltip', async () => {
    await open();

    const business = field(/^Razón social/);
    const help = document.getElementById(
      (business.getAttribute('aria-describedby') ?? '').split(' ')[0]!,
    );
    expect(help?.getAttribute('role')).toBe('tooltip');
    expect(help?.textContent).toContain(
      'tal como aparece en el RUT (por ejemplo, ACUÑA OBANDO SANTIAGO)',
    );
    expect(screen.getAllByRole('tooltip', { hidden: true }).length).toBeGreaterThanOrEqual(13);
    expect(screen.getByText(/Cámara de Comercio/)).toBeTruthy();
  });

  it('marks the required fields and does not call the API', async () => {
    const { http } = await open();

    create();

    expect((await screen.findAllByText('Este campo es obligatorio.')).length).toBeGreaterThan(5);
    expect(screen.getByText('Marque al menos una responsabilidad.')).toBeTruthy();
    http.expectNone((request) => request.method === 'POST');
  });

  it('creates the company, shows its API key once and forgets it on close', async () => {
    const { http } = await open();
    await fillAll();

    create();
    const request = await expectRequest(http, CREATE_URL);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      business_name: 'Acme SAS',
      type_document_identification_id: 6,
      type_organization_id: 1,
      type_regime_id: 1,
      type_liability_ids: [7],
      municipality_id: 149,
      address: 'Calle 1 # 2-3',
      phone: '6011234567',
      email: 'facturas@acme.test',
      merchant_registration: '12345-67',
    });
    request.flush(CREATED, { status: 201, statusText: 'Created' });

    const dialog = await screen.findByRole('dialog', { name: 'Copie la llave de la empresa' });
    expect(within(dialog).getByText(SECRET)).toBeTruthy();
    const writeText = vi.fn(async () => undefined);
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Copiar llave' }));
    await waitFor(() => expect(writeText).toHaveBeenCalledWith(SECRET));
    expect(await within(dialog).findByText('Llave copiada.')).toBeTruthy();

    fireEvent.click(within(dialog).getByRole('button', { name: 'Ya la copié' }));
    await waitFor(() =>
      expect(screen.queryByRole('dialog', { name: 'Copie la llave de la empresa' })).toBeNull(),
    );
    expect(document.body.textContent).not.toContain(SECRET);
    expect(screen.getByText(/Se creó la empresa Acme SAS \(NIT 900123456-8\)/)).toBeTruthy();
    // Enseguida se ofrece crear el dueño de la empresa nueva.
    expect(await screen.findByRole('dialog', { name: 'Crear usuario de Acme SAS' })).toBeTruthy();
  });

  it('creates the first owner of the new company', async () => {
    const { http } = await open();
    await fillAll();
    create();
    (await expectRequest(http, CREATE_URL)).flush(CREATED, { status: 201, statusText: 'Created' });
    const secret = await screen.findByRole('dialog', { name: 'Copie la llave de la empresa' });
    fireEvent.click(within(secret).getByRole('button', { name: 'Ya la copié' }));

    const dialog = await screen.findByRole('dialog', { name: 'Crear usuario de Acme SAS' });
    fireEvent.input(within(dialog).getByLabelText(/^Correo electrónico/), {
      target: { value: 'dueno@acme.test' },
    });
    fireEvent.input(within(dialog).getByLabelText(/^Nombre/), { target: { value: 'Ana Dueña' } });
    fireEvent.input(within(dialog).getByLabelText(/^Contraseña/), {
      target: { value: 'una-clave-larga-1' },
    });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Crear usuario' }));

    const request = await expectRequest(http, '/api/v1/users');
    expect(request.request.body).toEqual({
      company_id: 'c-9',
      email: 'dueno@acme.test',
      name: 'Ana Dueña',
      phone: null,
      password: 'una-clave-larga-1',
      role: 'owner',
    });
    request.flush(
      { id: 'u-1', email: 'dueno@acme.test', name: 'Ana Dueña', role: 'owner', company_id: 'c-9' },
      { status: 201, statusText: 'Created' },
    );

    await waitFor(() =>
      expect(screen.queryByRole('dialog', { name: 'Crear usuario de Acme SAS' })).toBeNull(),
    );
    expect(
      await screen.findByText(
        'Se creó el usuario dueno@acme.test como dueño de Acme SAS; ya puede iniciar sesión.',
      ),
    ).toBeTruthy();
  });

  it('shows the field errors of the API next to their fields', async () => {
    const { http } = await open();
    await fillAll();

    create();
    const error = problem(422, 'validation.failed', 'Los datos no son válidos.', {
      dv: ['El dígito de verificación no corresponde al NIT.'],
    });
    (await expectRequest(http, CREATE_URL)).flush(error.body, error.options);

    expect(
      await screen.findByText('El dígito de verificación no corresponde al NIT.'),
    ).toBeTruthy();
    // El DV se calcula del NIT: el error se muestra en el NIT, que es lo que hay que corregir.
    expect(field(/^NIT/).getAttribute('aria-invalid')).toBe('true');
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('shows a repeated email next to the email field', async () => {
    const { http } = await open();
    await fillAll();

    create();
    const error = problem(409, 'company.email_taken', 'El correo ya existe.');
    (await expectRequest(http, CREATE_URL)).flush(error.body, error.options);

    expect(await screen.findByText('El correo ya existe.')).toBeTruthy();
    expect(field(/^Correo electrónico/).getAttribute('aria-invalid')).toBe('true');
  });

  it('explains when the NIT already existed and its data was updated, without a key', async () => {
    const { http } = await open();
    await fillAll();

    create();
    (await expectRequest(http, CREATE_URL)).flush({
      ...CREATED,
      created: false,
      token: null,
    });

    expect(
      await screen.findByText(
        /La empresa con NIT 900123456-8 ya existía: se actualizaron sus datos y no se generó una llave nueva/,
      ),
    ).toBeTruthy();
    expect(screen.queryByRole('dialog', { name: 'Copie la llave de la empresa' })).toBeNull();
    expect(screen.getByRole('button', { name: 'Crear usuario de Acme SAS' })).toBeTruthy();
  });

  it('goes back to the list of companies', async () => {
    await open();

    fireEvent.click(screen.getByRole('link', { name: 'Volver a las empresas' }));

    await waitFor(() => expect(TestBed.inject(Router).url).toBe('/plataforma/empresas'));
  });
});
