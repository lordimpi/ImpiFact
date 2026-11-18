import { TestRequest } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { CustomerResponse } from '@impifact/api';
import { ToastStore } from '@impifact/core';
import { fireEvent, screen, waitFor, within } from '@testing-library/angular';
import { expectRequest, problem, renderMastersRoute } from '../testing/masters-testing';

const ANA: CustomerResponse = {
  id: 'c-1',
  type_document_identification_id: 6,
  identification_number: '900123456',
  dv: '8',
  name: 'Ana Comercial SAS',
  email: 'facturas@ana.test',
  phone: '3001234567',
  address: 'Calle 1 # 2-3',
  municipality_id: 149,
  type_organization_id: 1,
  type_regime_id: 1,
  type_liability_id: 117,
  merchant_registration: null,
  is_active: true,
};

const isList = (request: { url: string; method: string }) =>
  request.method === 'GET' && request.url === '/api/v1/customers';

const toastTexts = () =>
  TestBed.inject(ToastStore)
    .toasts()
    .map((toast) => toast.text);

function params(request: TestRequest) {
  const query = request.request.params;
  return Object.fromEntries(query.keys().map((key) => [key, query.get(key)]));
}

describe('CustomerListPage', () => {
  it('asks the server for the first page with the default status and sort', async () => {
    const { http } = await renderMastersRoute('/clientes');

    const request = await expectRequest(http, (r) => isList(r));
    expect(params(request)).toEqual({ status: 'active', sort: 'name', limit: '20' });
    request.flush({ items: [ANA], next_cursor: null });

    expect(await screen.findByRole('heading', { level: 1, name: 'Clientes' })).toBeTruthy();
    const table = screen.getByRole('table', { name: 'Clientes' });
    expect(await within(table).findByText('Ana Comercial SAS')).toBeTruthy();
    expect(within(table).getByText('NIT 900123456-8')).toBeTruthy();
    expect(within(table).getByText('Activo')).toBeTruthy();
    expect(
      screen.getByRole('link', { name: 'Editar a Ana Comercial SAS' }).getAttribute('href'),
    ).toBe('/clientes/c-1');
    expect(screen.getByRole('link', { name: 'Nuevo cliente' }).getAttribute('href')).toBe(
      '/clientes/nuevo',
    );
  });

  it('reads the search and the status from the URL', async () => {
    const { http } = await renderMastersRoute('/clientes?q=ana&estado=all');

    const request = await expectRequest(http, (r) => isList(r));
    expect(params(request)).toEqual({ search: 'ana', status: 'all', sort: 'name', limit: '20' });
    request.flush({ items: [], next_cursor: null });
    const search = screen.getByRole('searchbox', {
      name: 'Buscar por nombre o número de identificación',
    }) as HTMLInputElement;
    expect(search.value).toBe('ana');
    expect((screen.getByLabelText('Estado') as HTMLSelectElement).value).toBe('all');
  });

  it('sends the search to the server after a pause and keeps it in the URL', async () => {
    const { http, router } = await renderMastersRoute('/clientes');
    (await expectRequest(http, (r) => isList(r))).flush({ items: [ANA], next_cursor: null });

    const search = await screen.findByRole('searchbox', {
      name: 'Buscar por nombre o número de identificación',
    });
    fireEvent.input(search, { target: { value: 'acm' } });
    fireEvent.input(search, { target: { value: 'acme' } });

    const request = await expectRequest(http, (r) => isList(r));
    expect(params(request)).toEqual({
      search: 'acme',
      status: 'active',
      sort: 'name',
      limit: '20',
    });
    expect(router.url).toBe('/clientes?q=acme');
    request.flush({ items: [], next_cursor: null });
    expect(await screen.findByText('No hay clientes')).toBeTruthy();
    http.verify();
  });

  it('filters by status on the server and keeps it in the URL', async () => {
    const { http, router } = await renderMastersRoute('/clientes');
    (await expectRequest(http, (r) => isList(r))).flush({ items: [ANA], next_cursor: null });

    fireEvent.change(await screen.findByLabelText('Estado'), { target: { value: 'inactive' } });

    const request = await expectRequest(http, (r) => isList(r));
    expect(params(request)['status']).toBe('inactive');
    await waitFor(() => expect(router.url).toBe('/clientes?estado=inactive'));
  });

  it('pages with the server cursor and sorts on the server', async () => {
    const { http } = await renderMastersRoute('/clientes');
    (await expectRequest(http, (r) => isList(r))).flush({ items: [ANA], next_cursor: 'cursor-2' });

    fireEvent.click(await screen.findByRole('button', { name: 'Siguiente' }));
    const next = await expectRequest(http, (r) => isList(r));
    expect(params(next)).toEqual({
      status: 'active',
      sort: 'name',
      cursor: 'cursor-2',
      limit: '20',
    });
    next.flush({ items: [ANA], next_cursor: null });

    fireEvent.click(await screen.findByRole('button', { name: /Nombre/ }));
    const sorted = await expectRequest(http, (r) => isList(r));
    expect(params(sorted)).toEqual({ status: 'active', sort: '-name', limit: '20' });
  });

  it('shows the error state and retries', async () => {
    const { http } = await renderMastersRoute('/clientes');
    const failed = await expectRequest(http, (r) => isList(r));
    const error = problem(503, 'http.unexpected', 'Servicio no disponible.');
    failed.flush(error.body, error.options);

    expect(await screen.findByText('No se pudieron cargar los clientes')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Reintentar' }));

    (await expectRequest(http, (r) => isList(r))).flush({ items: [ANA], next_cursor: null });
    expect(await screen.findByText('Ana Comercial SAS')).toBeTruthy();
  });

  it('deactivates a customer after confirming and reloads the page', async () => {
    const { http } = await renderMastersRoute('/clientes');
    (await expectRequest(http, (r) => isList(r))).flush({ items: [ANA], next_cursor: null });

    fireEvent.click(await screen.findByRole('button', { name: 'Desactivar a Ana Comercial SAS' }));
    const dialog = await screen.findByRole('dialog', { name: 'Desactivar cliente' });
    expect(dialog.textContent).toContain('Ana Comercial SAS dejará de aparecer al emitir.');
    http.expectNone((r) => r.url.endsWith('/deactivate'));
    fireEvent.click(within(dialog).getByRole('button', { name: 'Desactivar' }));

    const deactivate = await expectRequest(http, '/api/v1/customers/c-1/deactivate');
    expect(deactivate.request.method).toBe('POST');
    deactivate.flush(null, { status: 204, statusText: 'No Content' });
    (await expectRequest(http, (r) => isList(r))).flush({ items: [], next_cursor: null });
    await waitFor(() => expect(toastTexts()).toContain('Cliente desactivado.'));
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  });

  it('cancels the deactivation without calling the server', async () => {
    const { http } = await renderMastersRoute('/clientes');
    (await expectRequest(http, (r) => isList(r))).flush({ items: [ANA], next_cursor: null });

    fireEvent.click(await screen.findByRole('button', { name: 'Desactivar a Ana Comercial SAS' }));
    const dialog = await screen.findByRole('dialog', { name: 'Desactivar cliente' });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Cancelar' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    http.expectNone((r) => r.url.endsWith('/deactivate'));
  });

  it('reactivates an inactive customer with all its data', async () => {
    const { http } = await renderMastersRoute('/clientes?estado=inactive');
    const inactive = { ...ANA, is_active: false };
    (await expectRequest(http, (r) => isList(r))).flush({ items: [inactive], next_cursor: null });

    fireEvent.click(await screen.findByRole('button', { name: 'Reactivar a Ana Comercial SAS' }));

    const update = await expectRequest(http, '/api/v1/customers/c-1');
    expect(update.request.method).toBe('PUT');
    expect(update.request.body).toEqual({
      type_document_identification_id: 6,
      identification_number: '900123456',
      dv: '8',
      name: 'Ana Comercial SAS',
      phone: '3001234567',
      address: 'Calle 1 # 2-3',
      email: 'facturas@ana.test',
      municipality_id: 149,
      type_organization_id: 1,
      type_regime_id: 1,
      type_liability_id: 117,
      merchant_registration: null,
      is_active: true,
    });
    update.flush({ ...ANA });
    const reload = await expectRequest(http, (r) => isList(r));
    expect(params(reload)['status']).toBe('inactive');
    await waitFor(() => expect(toastTexts()).toContain('Cliente reactivado.'));
  });

  it('keeps users without the owner or seller role out', async () => {
    const { router, http } = await renderMastersRoute('/clientes', { roles: ['customer'] });

    await waitFor(() => expect(router.url).toBe('/'));
    http.expectNone((r) => isList(r));
  });

  it('asks for a company before showing the list', async () => {
    const { router } = await renderMastersRoute('/clientes', { companyId: null });

    await waitFor(() => expect(router.url).toBe('/empresas/seleccionar'));
  });
});
