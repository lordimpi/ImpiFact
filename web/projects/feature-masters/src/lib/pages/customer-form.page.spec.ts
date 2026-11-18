import { TestBed } from '@angular/core/testing';
import { ToastStore } from '@impifact/core';
import { fireEvent, screen, waitFor } from '@testing-library/angular';
import { catalogRequests } from '../testing/catalog-fixtures';
import { expectRequest, problem, renderMastersRoute } from '../testing/masters-testing';

const field = (label: RegExp) => screen.getByLabelText(label) as HTMLInputElement;
const type = (label: RegExp, value: string) => fireEvent.input(field(label), { target: { value } });
const choose = (label: RegExp, value: string) => {
  const select = screen.getByLabelText(label) as HTMLSelectElement;
  fireEvent.change(select, { target: { value } });
  fireEvent.input(select, { target: { value } });
};
/** Textos de las opciones de una lista (más rápido que buscar cada `option` por su rol). */
const optionTexts = (label: RegExp) =>
  Array.from((screen.getByLabelText(label) as HTMLSelectElement).options).map((option) =>
    option.textContent?.trim(),
  );
const save = () => fireEvent.click(screen.getByRole('button', { name: 'Guardar' }));
const toastTexts = () =>
  TestBed.inject(ToastStore)
    .toasts()
    .map((toast) => toast.text);

async function openNew() {
  const rendered = await renderMastersRoute('/clientes/nuevo');
  await screen.findByRole('heading', { level: 1, name: 'Nuevo cliente' });
  // Las listas salen de los catálogos del Api.
  await waitFor(() => expect(optionTexts(/^Tipo de identificación/)).toContain('NIT'));
  await waitFor(() => expect(optionTexts(/^Municipio/)).toContain('Bogotá, D.C. (Bogotá)'));
  return rendered;
}

function fillNit() {
  choose(/^Tipo de identificación/, '6');
  type(/^Número de identificación/, '900123456');
}

describe('CustomerFormPage', () => {
  it('fills the lists from the DIAN catalogs and searches municipalities on the server', async () => {
    await openNew();
    const names = catalogRequests.map((request) => request.url.replace('/api/v1/catalogs/', ''));
    expect(names).toEqual(
      expect.arrayContaining([
        'identification-types',
        'organization-types',
        'regimes',
        'liabilities',
        'departments',
        'municipalities',
      ]),
    );
    expect(optionTexts(/^Responsabilidad fiscal/)).toContain('O-13 Gran contribuyente');
    expect(optionTexts(/^Municipio/)).toContain('Medellín (Antioquia)');

    fireEvent.input(screen.getByLabelText(/^Buscar municipio/), { target: { value: 'bogo' } });
    await waitFor(() => expect(catalogRequests.at(-1)?.params.get('q')).toBe('bogo'));
    await waitFor(() =>
      expect(optionTexts(/^Municipio/)).toEqual(['Sin seleccionar', 'Bogotá, D.C. (Bogotá)']),
    );
  });

  it('shows the check digit only for a NIT', async () => {
    await openNew();

    expect(screen.queryByLabelText(/Dígito de verificación/)).toBeNull();
    choose(/^Tipo de identificación/, '6');
    expect(await screen.findByLabelText(/Dígito de verificación/)).toBeTruthy();
    choose(/^Tipo de identificación/, '3');
    await waitFor(() => expect(screen.queryByLabelText(/Dígito de verificación/)).toBeNull());
  });

  it('marks the required fields and does not call the API', async () => {
    const { http } = await openNew();

    save();

    expect(await screen.findAllByText('Este campo es obligatorio.')).toHaveLength(3);
    expect(field(/^Nombre o razón social/).getAttribute('aria-invalid')).toBe('true');
    http.expectNone('/api/v1/customers');
  });

  it('shows the server field errors next to each field and then saves', async () => {
    const { http, router } = await openNew();
    fillNit();
    type(/Dígito de verificación/, '1');
    type(/^Nombre o razón social/, 'Ana Comercial SAS');
    type(/Correo para recibir facturas/, 'facturas@ana.test');
    choose(/^Municipio/, '149');

    save();
    const rejected = await expectRequest(http, '/api/v1/customers');
    expect(rejected.request.method).toBe('POST');
    expect(rejected.request.body).toEqual({
      type_document_identification_id: 6,
      identification_number: '900123456',
      dv: '1',
      name: 'Ana Comercial SAS',
      phone: null,
      address: null,
      email: 'facturas@ana.test',
      municipality_id: 149,
      type_organization_id: null,
      type_regime_id: null,
      type_liability_id: null,
      merchant_registration: null,
    });
    const error = problem(422, 'validation.failed', 'Los datos no son válidos.', {
      dv: ['Error, el digito de verificacion no es valido para este NIT.'],
    });
    rejected.flush(error.body, error.options);

    expect(
      await screen.findByText('Error, el digito de verificacion no es valido para este NIT.'),
    ).toBeTruthy();
    expect(field(/Dígito de verificación/).getAttribute('aria-invalid')).toBe('true');
    expect(router.url).toBe('/clientes/nuevo');
    expect(toastTexts()).toEqual([]);

    type(/Dígito de verificación/, '8');
    save();
    const accepted = await expectRequest(http, '/api/v1/customers');
    expect((accepted.request.body as { dv: string }).dv).toBe('8');
    accepted.flush({ id: 'c-9', name: 'Ana Comercial SAS', is_active: true });

    await waitFor(() => expect(router.url).toBe('/clientes'));
    expect(toastTexts()).toContain('Cliente guardado.');
  });

  it('explains a duplicate customer in the form', async () => {
    const { http } = await openNew();
    choose(/^Tipo de identificación/, '3');
    type(/^Número de identificación/, '1020304050');
    type(/^Nombre o razón social/, 'Ana Pérez');

    save();
    const error = problem(
      409,
      'customer.duplicate',
      'Ya existe un cliente con esa identificación.',
    );
    (await expectRequest(http, '/api/v1/customers')).flush(error.body, error.options);

    expect((await screen.findByRole('alert')).textContent).toContain(
      'Ya existe otro cliente con ese tipo y número de identificación.',
    );
  });

  it('prefills the name and the email from the DIAN', async () => {
    const { http } = await openNew();
    fillNit();

    fireEvent.click(screen.getByRole('button', { name: 'Buscar en la DIAN' }));
    const lookup = await expectRequest(
      http,
      (r) => r.url === '/api/v1/customers/lookup' && r.params.get('type') === '6',
    );
    expect(lookup.request.params.get('number')).toBe('900123456');
    expect(
      screen.getByRole('button', { name: 'Buscar en la DIAN' }).getAttribute('aria-busy'),
    ).toBe('true');
    lookup.flush({ found: true, name: 'ANA COMERCIAL SAS', email: 'fe@ana.test' });

    expect((await screen.findByRole('status')).textContent).toContain(
      'La DIAN tiene registrado a ANA COMERCIAL SAS.',
    );
    expect(field(/^Nombre o razón social/).value).toBe('ANA COMERCIAL SAS');
    expect(field(/Correo para recibir facturas/).value).toBe('fe@ana.test');
  });

  it('says when the DIAN does not know the buyer', async () => {
    const { http } = await openNew();
    fillNit();

    fireEvent.click(screen.getByRole('button', { name: 'Buscar en la DIAN' }));
    (await expectRequest(http, (r) => r.url === '/api/v1/customers/lookup')).flush({
      found: false,
      name: null,
      email: null,
    });

    expect((await screen.findByRole('status')).textContent).toContain(
      'La DIAN no tiene registrado este adquiriente.',
    );
    expect(field(/^Nombre o razón social/).value).toBe('');
  });

  it.each([
    [503, 'dian.unavailable', 'La DIAN no respondió. Intente de nuevo en unos minutos.'],
    [422, 'dian.rejected', 'La DIAN no aceptó la consulta: Documento no válido.'],
    [404, 'certificate.not_found', 'La empresa no tiene un certificado activo.'],
  ])('explains a %s (%s) from the DIAN lookup', async (status, code, text) => {
    const { http } = await openNew();
    fillNit();

    fireEvent.click(screen.getByRole('button', { name: 'Buscar en la DIAN' }));
    const error = problem(status, code, 'Documento no válido.');
    (await expectRequest(http, (r) => r.url === '/api/v1/customers/lookup')).flush(
      error.body,
      error.options,
    );

    expect((await screen.findByRole('alert')).textContent).toContain(text);
    expect(toastTexts()).toEqual([]);
  });

  it('asks for the identification before looking up the DIAN', async () => {
    const { http } = await openNew();

    fireEvent.click(screen.getByRole('button', { name: 'Buscar en la DIAN' }));

    expect((await screen.findByRole('alert')).textContent).toContain(
      'Escriba el tipo y el número de identificación para consultar en la DIAN.',
    );
    http.expectNone((r) => r.url === '/api/v1/customers/lookup');
  });

  it('loads a customer, edits it and keeps its status', async () => {
    const { http, router } = await renderMastersRoute('/clientes/c-1');

    const load = await expectRequest(http, '/api/v1/customers/c-1');
    load.flush({
      id: 'c-1',
      type_document_identification_id: 3,
      identification_number: '1020304050',
      dv: null,
      name: 'Ana Pérez',
      email: null,
      phone: '3001234567',
      address: null,
      municipality_id: 1,
      type_organization_id: 2,
      type_regime_id: 2,
      type_liability_id: 117,
      merchant_registration: null,
      is_active: false,
    });
    expect(await screen.findByRole('heading', { level: 1, name: 'Editar cliente' })).toBeTruthy();
    await waitFor(() => expect(field(/^Nombre o razón social/).value).toBe('Ana Pérez'));
    expect((screen.getByLabelText(/^Municipio/) as HTMLSelectElement).value).toBe('1');

    type(/^Nombre o razón social/, 'Ana María Pérez');
    save();

    const update = await expectRequest(http, '/api/v1/customers/c-1');
    expect(update.request.method).toBe('PUT');
    expect(update.request.body).toEqual({
      type_document_identification_id: 3,
      identification_number: '1020304050',
      dv: null,
      name: 'Ana María Pérez',
      phone: '3001234567',
      address: null,
      email: null,
      municipality_id: 1,
      type_organization_id: 2,
      type_regime_id: 2,
      type_liability_id: 117,
      merchant_registration: null,
      is_active: false,
    });
    update.flush({ id: 'c-1' });
    await waitFor(() => expect(router.url).toBe('/clientes'));
  });

  it('shows an error with retry when the customer cannot be loaded', async () => {
    const { http } = await renderMastersRoute('/clientes/c-1');
    const error = problem(404, 'customer.not_found', 'No existe.');
    (await expectRequest(http, '/api/v1/customers/c-1')).flush(error.body, error.options);

    expect(await screen.findByText('No se pudieron cargar los datos')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Reintentar' }));
    expect(await expectRequest(http, '/api/v1/customers/c-1')).toBeTruthy();
  });

  it('asks before leaving with unsaved changes', async () => {
    const { router, confirmLeave } = await renderMastersRoute('/clientes/nuevo', {
      confirmLeave: false,
    });
    await screen.findByRole('heading', { level: 1, name: 'Nuevo cliente' });
    type(/^Nombre o razón social/, 'Ana');

    await router.navigateByUrl('/clientes');

    expect(confirmLeave).toHaveBeenCalledOnce();
    expect(router.url).toBe('/clientes/nuevo');
  });

  it('leaves without asking when nothing changed', async () => {
    const { router, confirmLeave } = await renderMastersRoute('/clientes/nuevo');
    await screen.findByRole('heading', { level: 1, name: 'Nuevo cliente' });

    await router.navigateByUrl('/clientes');

    expect(confirmLeave).not.toHaveBeenCalled();
    expect(router.url).toBe('/clientes');
  });
});
