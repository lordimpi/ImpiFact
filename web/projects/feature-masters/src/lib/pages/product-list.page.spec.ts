import { TestRequest } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ProductResponse } from '@impifact/api';
import { ToastStore } from '@impifact/core';
import { fireEvent, screen, waitFor, within } from '@testing-library/angular';
import { expectRequest, renderMastersRoute } from '../testing/masters-testing';

const COFFEE: ProductResponse = {
  id: 'p-1',
  code: 'CAF-500',
  name: 'Café molido 500 g',
  description: 'Tostión media',
  type_item_identification_id: 4,
  unit_measure_id: 70,
  unit_price: 12500.5,
  tax_id: 1,
  tax_percent: 19,
  tax_per_unit_amount: null,
  is_active: true,
};

const isList = (request: { url: string; method: string }) =>
  request.method === 'GET' && request.url === '/api/v1/products';

function params(request: TestRequest) {
  const query = request.request.params;
  return Object.fromEntries(query.keys().map((key) => [key, query.get(key)]));
}

describe('ProductListPage', () => {
  it('asks the server for the first page and shows the products', async () => {
    const { http } = await renderMastersRoute('/productos?q=caf');

    const request = await expectRequest(http, (r) => isList(r));
    expect(params(request)).toEqual({ search: 'caf', status: 'active', sort: 'name', limit: '20' });
    request.flush({ items: [COFFEE], next_cursor: null });

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Productos y servicios' }),
    ).toBeTruthy();
    const table = screen.getByRole('table', { name: 'Productos y servicios' });
    expect(await within(table).findByText('Café molido 500 g')).toBeTruthy();
    expect(within(table).getByText('CAF-500')).toBeTruthy();
    expect(within(table).getByText(/12\.500,5/)).toBeTruthy();
    expect(
      screen.getByRole('link', { name: 'Editar Café molido 500 g' }).getAttribute('href'),
    ).toBe('/productos/p-1');
  });

  it('sorts by code on the server', async () => {
    const { http } = await renderMastersRoute('/productos');
    (await expectRequest(http, (r) => isList(r))).flush({ items: [COFFEE], next_cursor: null });

    fireEvent.click(await screen.findByRole('button', { name: /Código/ }));

    const sorted = await expectRequest(http, (r) => isList(r));
    expect(params(sorted)['sort']).toBe('code');
  });

  it('deactivates a product after confirming', async () => {
    const { http } = await renderMastersRoute('/productos');
    (await expectRequest(http, (r) => isList(r))).flush({ items: [COFFEE], next_cursor: null });

    fireEvent.click(await screen.findByRole('button', { name: 'Desactivar Café molido 500 g' }));
    const dialog = await screen.findByRole('dialog', { name: 'Desactivar producto' });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Desactivar' }));

    const deactivate = await expectRequest(http, '/api/v1/products/p-1/deactivate');
    expect(deactivate.request.method).toBe('POST');
    deactivate.flush(null, { status: 204, statusText: 'No Content' });
    (await expectRequest(http, (r) => isList(r))).flush({ items: [], next_cursor: null });
    const texts = () =>
      TestBed.inject(ToastStore)
        .toasts()
        .map((toast) => toast.text);
    await waitFor(() => expect(texts()).toContain('Producto desactivado.'));
  });

  it('reactivates an inactive product with all its data', async () => {
    const { http } = await renderMastersRoute('/productos?estado=all');
    (await expectRequest(http, (r) => isList(r))).flush({
      items: [{ ...COFFEE, is_active: false }],
      next_cursor: null,
    });

    fireEvent.click(await screen.findByRole('button', { name: 'Reactivar Café molido 500 g' }));

    const update = await expectRequest(http, '/api/v1/products/p-1');
    expect(update.request.method).toBe('PUT');
    expect(update.request.body).toEqual({
      code: 'CAF-500',
      name: 'Café molido 500 g',
      description: 'Tostión media',
      type_item_identification_id: 4,
      unit_measure_id: 70,
      unit_price: 12500.5,
      tax_id: 1,
      tax_percent: 19,
      tax_per_unit_amount: null,
      is_active: true,
    });
  });
});
