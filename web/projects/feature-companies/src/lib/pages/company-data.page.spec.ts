import { TestBed } from '@angular/core/testing';
import { ToastStore } from '@impifact/core';
import { fireEvent, screen, waitFor, within } from '@testing-library/angular';
import { CompanySetupStore } from '../state/company-setup.store';
import { catalogRequests } from '../testing/catalog-fixtures';
import {
  COMPANY_DATA,
  COMPANY_PATH,
  EMPTY_READINESS,
  expectRequest,
  problem,
  renderCompaniesRoute,
  respond,
} from '../testing/companies-testing';

const field = (label: RegExp) => screen.getByLabelText(label) as HTMLInputElement;
const type = (label: RegExp, value: string) => fireEvent.input(field(label), { target: { value } });
const choose = (label: RegExp, value: string) => {
  const select = screen.getByLabelText(label) as HTMLSelectElement;
  fireEvent.change(select, { target: { value } });
  fireEvent.input(select, { target: { value } });
};
const optionTexts = (label: RegExp) =>
  Array.from((screen.getByLabelText(label) as HTMLSelectElement).options).map((option) =>
    option.textContent?.trim(),
  );
const save = () => fireEvent.click(screen.getByRole('button', { name: 'Guardar' }));
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

/** Empresa recién creada: solo la razón social y el tipo de identificación. */
const NEW_COMPANY = {
  id: 'company-1',
  business_name: 'Acme SAS',
  type_document_identification_id: 6,
  type_liability_ids: [],
  environments: [],
};

async function open(company: object = NEW_COMPANY) {
  const rendered = await renderCompaniesRoute('/empresas/company-1/datos');
  await respond(rendered.http, COMPANY_PATH, company);
  await screen.findByRole('heading', { level: 2, name: 'Datos de la empresa' });
  // Las listas salen de los catálogos del Api.
  await screen.findByRole('checkbox', { name: 'O-13 Gran contribuyente' });
  expect(screen.getByRole('combobox', { name: /^Departamento/ })).toBeTruthy();
  return rendered;
}

async function fillAll() {
  choose(/^Tipo de organización/, '1');
  choose(/^Régimen/, '1');
  fireEvent.click(screen.getByRole('checkbox', { name: 'O-13 Gran contribuyente' }));
  await pickDepartment('Bogotá');
  await pickMunicipality('bogo', 'Bogotá, D.C.');
  type(/^Dirección/, 'Calle 1 # 2-3');
  type(/^Teléfono/, '6011234567');
  type(/^Correo electrónico/, 'facturas@acme.test');
  type(/^Matrícula mercantil/, '12345-67');
}

/** Elige el departamento en su combobox: escribe parte del nombre (sin tildes) y hace clic en la opción. */
async function pickDepartment(name: string) {
  const input = screen.getByRole('combobox', { name: /^Departamento/ });
  fireEvent.input(input, {
    target: { value: name.slice(0, 4).normalize('NFD').replace(/[̀-ͯ]/g, '') },
  });
  fireEvent.click(await screen.findByRole('option', { name }));
}

describe('CompanyDataPage', () => {
  it('fills the lists from the DIAN catalogs and searches the municipalities of the chosen department', async () => {
    await open();
    expect(optionTexts(/^Régimen/)).toContain('Responsable de IVA');
    expect(screen.getByRole('checkbox', { name: 'R-99-PN No aplica – Otros' })).toBeTruthy();
    expect(screen.getByText('Seleccione primero el departamento.')).toBeTruthy();

    await pickDepartment('Antioquia');
    await waitFor(() => expect(catalogRequests.at(-1)?.params.get('department_id')).toBe('2'));
    fireEvent.input(municipality(), { target: { value: 'medel' } });
    await waitFor(() => expect(catalogRequests.at(-1)?.params.get('q')).toBe('medel'));
    await waitFor(() => expect(municipalityOptions()).toEqual(['Medellín']));
    expect(screen.queryByRole('button', { name: /Cargar más/ })).toBeNull();
  });

  it('accepts only digits in the phone and explains every field in a tooltip', async () => {
    await open();

    type(/^Teléfono/, '(601) 123-4567');

    expect(field(/^Teléfono/).value).toBe('6011234567');
    expect(field(/^Teléfono/).getAttribute('inputmode')).toBe('numeric');
    const business = field(/^Razón social/);
    const help = document.getElementById(
      (business.getAttribute('aria-describedby') ?? '').split(' ')[0]!,
    );
    expect(help?.textContent).toContain('(por ejemplo, ACUÑA OBANDO SANTIAGO)');
    expect(screen.getAllByRole('tooltip', { hidden: true }).length).toBe(11);
    expect(screen.getByText(/registro mercantil de la Cámara de Comercio/)).toBeTruthy();
  });

  it('fills the form with the data the API has saved', async () => {
    await open(COMPANY_DATA);

    expect(field(/^Razón social/).value).toBe('Acme SAS');
    expect(field(/^Tipo de identificación/).value).toBe('6');
    expect(field(/^Tipo de organización/).value).toBe('1');
    expect(field(/^Régimen/).value).toBe('1');
    // El departamento sale del municipio guardado.
    await waitFor(() => expect(field(/^Departamento/).value).toBe('Bogotá'));
    await waitFor(() => expect(municipality().value).toBe('Bogotá, D.C.'));
    expect(field(/^Dirección/).value).toBe('Calle 1 # 2-3');
    expect(field(/^Teléfono/).value).toBe('6011234567');
    expect(field(/^Correo electrónico/).value).toBe('facturas@acme.test');
    expect(field(/^Matrícula mercantil/).value).toBe('12345-67');
    expect(
      (screen.getByRole('checkbox', { name: 'O-13 Gran contribuyente' }) as HTMLInputElement)
        .checked,
    ).toBe(true);
    expect(screen.queryByText(/no permite consultar/)).toBeNull();
  });

  it('saves the data it read without changes and refreshes the setup status', async () => {
    const { http } = await open(COMPANY_DATA);

    save();
    const request = await expectRequest(http, `${COMPANY_PATH}/7`);
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
    request.flush({ success: true, company: COMPANY_DATA });

    await respond(http, `${COMPANY_PATH}/setup-status`, EMPTY_READINESS);
    await waitFor(() =>
      expect(TestBed.inject(CompanySetupStore).readiness()).toEqual(EMPTY_READINESS),
    );
  });

  it('explains when the data could not be read and retries', async () => {
    const { http } = await renderCompaniesRoute('/empresas/company-1/datos');
    const error = problem(503, 'unavailable', 'No disponible.');
    (
      await expectRequest(
        http,
        (request) => request.method === 'GET' && request.url === COMPANY_PATH,
      )
    ).flush(error.body, error.options);

    expect(
      await screen.findByText('No se pudieron consultar los datos de la empresa.'),
    ).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Guardar' })).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: 'Reintentar' }));
    await respond(http, COMPANY_PATH, COMPANY_DATA);

    await waitFor(() => expect(field(/^Dirección/).value).toBe('Calle 1 # 2-3'));
  });

  it('marks the required fields and does not call the API', async () => {
    const { http } = await open();

    save();

    expect((await screen.findAllByText('Este campo es obligatorio.')).length).toBeGreaterThan(3);
    expect(screen.getByText('Marque al menos una responsabilidad.')).toBeTruthy();
    http.expectNone(() => true);
  });

  it('shows a server field error next to its field and then saves', async () => {
    const { http, directory } = await open();
    await fillAll();

    save();
    const rejected = await expectRequest(http, `${COMPANY_PATH}/7`);
    expect(rejected.request.method).toBe('PUT');
    expect(rejected.request.body).toEqual({
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
    const error = problem(422, 'validation.failed', 'Los datos no son válidos.', {
      email: ['El email ya está en uso por otra empresa.'],
    });
    rejected.flush(error.body, error.options);

    expect(await screen.findByText('El email ya está en uso por otra empresa.')).toBeTruthy();
    expect(field(/^Correo electrónico/).getAttribute('aria-invalid')).toBe('true');

    type(/^Correo electrónico/, 'otra@acme.test');
    save();
    const accepted = await expectRequest(http, `${COMPANY_PATH}/7`);
    accepted.flush({
      success: true,
      company: { id: 'company-1', business_name: 'Acme SAS', environments: [] },
    });

    await waitFor(() =>
      expect(
        TestBed.inject(ToastStore)
          .toasts()
          .map((toast) => toast.text),
      ).toContain('Los cambios se guardaron.'),
    );
    expect(TestBed.inject(CompanySetupStore).companyData()?.business_name).toBe('Acme SAS');
    expect(directory.reload).toHaveBeenCalled();
  });

  it('asks before leaving with unsaved changes', async () => {
    const { router, http, confirmLeave } = await renderCompaniesRoute('/empresas/company-1/datos', {
      confirmLeave: false,
    });
    await respond(http, COMPANY_PATH, COMPANY_DATA);
    await screen.findByLabelText(/^Dirección/);
    type(/^Dirección/, 'Calle 9');

    await router.navigateByUrl('/empresas/company-1/ambientes');

    expect(confirmLeave).toHaveBeenCalled();
    expect(router.url).toBe('/empresas/company-1/datos');
  });
});
