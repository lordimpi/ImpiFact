import { TestBed } from '@angular/core/testing';
import { ToastStore } from '@impifact/core';
import { fireEvent, screen, waitFor } from '@testing-library/angular';
import { CompanySetupStore } from '../state/company-setup.store';
import { catalogRequests } from '../testing/catalog-fixtures';
import { expectRequest, problem, renderCompaniesRoute } from '../testing/companies-testing';

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

async function open() {
  const rendered = await renderCompaniesRoute('/empresas/company-1/datos');
  await screen.findByRole('heading', { level: 2, name: 'Datos de la empresa' });
  // Las listas salen de los catálogos del Api.
  await screen.findByRole('checkbox', { name: 'O-13 Gran contribuyente' });
  await waitFor(() => expect(optionTexts(/^Municipio/)).toContain('Bogotá, D.C. (Bogotá)'));
  return rendered;
}

function fillAll() {
  choose(/^Tipo de organización/, '1');
  choose(/^Régimen/, '1');
  fireEvent.click(screen.getByRole('checkbox', { name: 'O-13 Gran contribuyente' }));
  choose(/^Municipio/, '149');
  type(/^Dirección/, 'Calle 1 # 2-3');
  type(/^Teléfono/, '6011234567');
  type(/^Correo electrónico/, 'facturas@acme.test');
  type(/^Matrícula mercantil/, '12345-67');
}

describe('CompanyDataPage', () => {
  it('fills the lists from the DIAN catalogs and searches municipalities on the server', async () => {
    await open();
    expect(optionTexts(/^Régimen/)).toContain('Responsable de IVA');
    expect(screen.getByRole('checkbox', { name: 'R-99-PN No aplica – Otros' })).toBeTruthy();

    fireEvent.input(screen.getByLabelText(/^Buscar municipio/), { target: { value: 'medel' } });
    await waitFor(() => expect(catalogRequests.at(-1)?.params.get('q')).toBe('medel'));
    await waitFor(() =>
      expect(optionTexts(/^Municipio/)).toEqual(['Seleccione una opción', 'Medellín (Antioquia)']),
    );
  });

  it('starts with the business name the API knows and explains that the rest is not readable', async () => {
    await open();

    expect(field(/^Razón social/).value).toBe('Acme SAS');
    expect(field(/^Tipo de identificación/).value).toBe('6');
    expect(screen.getByText(/el Api no permite consultar los datos guardados/)).toBeTruthy();
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
    fillAll();

    save();
    const rejected = await expectRequest(http, '/api/v1/companies/900123456/7');
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
    const accepted = await expectRequest(http, '/api/v1/companies/900123456/7');
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
    expect(TestBed.inject(CompanySetupStore).saved()?.business_name).toBe('Acme SAS');
    expect(directory.reload).toHaveBeenCalled();
  });

  it('asks before leaving with unsaved changes', async () => {
    const { router, confirmLeave } = await renderCompaniesRoute('/empresas/company-1/datos', {
      confirmLeave: false,
    });
    await screen.findByRole('heading', { level: 2, name: 'Datos de la empresa' });
    type(/^Dirección/, 'Calle 9');

    await router.navigateByUrl('/empresas/company-1/ambientes');

    expect(confirmLeave).toHaveBeenCalled();
    expect(router.url).toBe('/empresas/company-1/datos');
  });
});
