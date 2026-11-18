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
  const rendered = await renderMastersRoute('/productos/nuevo');
  await screen.findByRole('heading', { level: 1, name: 'Nuevo producto' });
  return rendered;
}

describe('ProductFormPage', () => {
  it('lists the catalog taxes without withholdings and searches unit measures on the server', async () => {
    await openNew();
    await waitFor(() =>
      expect(optionTexts(/^Impuesto/)).toContain('IVA (Impuesto sobre la Ventas)'),
    );
    expect(optionTexts(/^Impuesto/).join()).not.toContain('ReteIVA');
    await waitFor(() => expect(optionTexts(/^Unidad de medida/)).toContain('unidad (94)'));

    fireEvent.input(screen.getByLabelText(/^Buscar unidad de medida/), {
      target: { value: 'kgm' },
    });
    await waitFor(() => expect(catalogRequests.at(-1)?.params.get('q')).toBe('kgm'));
    // La unidad elegida sigue en la lista aunque la búsqueda no la traiga.
    await waitFor(() =>
      expect(optionTexts(/^Unidad de medida/)).toEqual(['kilogramo (KGM)', 'unidad (94)']),
    );
  });

  it('starts with the unit measure and marks the required fields', async () => {
    const { http } = await openNew();
    expect((screen.getByLabelText(/^Unidad de medida/) as HTMLSelectElement).value).toBe('70');
    expect(screen.queryByLabelText(/Producto activo/)).toBeNull();

    save();

    expect(await screen.findAllByText('Este campo es obligatorio.')).toHaveLength(3);
    http.expectNone('/api/v1/products');
  });

  it('shows the tax rate or the per-unit amount according to the tax', async () => {
    await openNew();
    expect(screen.queryByLabelText(/^Tarifa/)).toBeNull();
    expect(screen.queryByLabelText(/^Valor por unidad/)).toBeNull();

    choose(/^Impuesto/, '1');
    expect(await screen.findByLabelText(/^Tarifa/)).toBeTruthy();

    choose(/^Impuesto/, '10');
    await waitFor(() => expect(screen.queryByLabelText(/^Tarifa/)).toBeNull());
    expect(screen.getByLabelText(/^Valor por unidad/)).toBeTruthy();

    choose(/^Impuesto/, '');
    await waitFor(() => expect(screen.queryByLabelText(/^Valor por unidad/)).toBeNull());
  });

  it('rejects a price with thousands separators', async () => {
    const { http } = await openNew();
    type(/^Código/, 'CAF-500');
    type(/^Nombre/, 'Café');
    type(/^Precio unitario/, '12.500,50');

    save();

    expect(
      await screen.findByText(
        'Escriba un número sin separadores de miles, con punto para los decimales.',
      ),
    ).toBeTruthy();
    http.expectNone('/api/v1/products');
  });

  it('shows the server field errors next to each field and then saves', async () => {
    const { http, router } = await openNew();
    type(/^Código/, 'CAF-500');
    type(/^Nombre/, 'Café molido 500 g');
    type(/^Precio unitario/, '12500.50');
    choose(/^Impuesto/, '1');
    type(/^Tarifa/, '190');

    save();
    const rejected = await expectRequest(http, '/api/v1/products');
    expect(rejected.request.method).toBe('POST');
    expect(rejected.request.body).toEqual({
      code: 'CAF-500',
      name: 'Café molido 500 g',
      description: null,
      unit_measure_id: 70,
      unit_price: '12500.50',
      tax_id: 1,
      tax_percent: '190',
      tax_per_unit_amount: null,
    });
    const error = problem(422, 'validation.failed', 'Los datos no son válidos.', {
      tax_percent: ['tax percent es inválido.'],
    });
    rejected.flush(error.body, error.options);

    expect(await screen.findByText('tax percent es inválido.')).toBeTruthy();
    expect(field(/^Tarifa/).getAttribute('aria-invalid')).toBe('true');

    type(/^Tarifa/, '19');
    save();
    const accepted = await expectRequest(http, '/api/v1/products');
    expect((accepted.request.body as { tax_percent: string }).tax_percent).toBe('19');
    accepted.flush({ id: 'p-9', code: 'CAF-500', is_active: true });

    await waitFor(() => expect(router.url).toBe('/productos'));
    expect(toastTexts()).toContain('Producto guardado.');
  });

  it('explains a duplicate code in the form', async () => {
    const { http } = await openNew();
    type(/^Código/, 'CAF-500');
    type(/^Nombre/, 'Café');
    type(/^Precio unitario/, '100');

    save();
    const error = problem(409, 'product.duplicate', 'Código repetido.');
    (await expectRequest(http, '/api/v1/products')).flush(error.body, error.options);

    expect((await screen.findByRole('alert')).textContent).toContain(
      'Ya existe otro producto con ese código.',
    );
    expect(toastTexts()).toEqual([]);
  });

  it('loads a product, deactivates it from the form and keeps its item type', async () => {
    const { http, router } = await renderMastersRoute('/productos/p-1');
    (await expectRequest(http, '/api/v1/products/p-1')).flush({
      id: 'p-1',
      code: 'BOL-1',
      name: 'Bolsa plástica',
      description: null,
      type_item_identification_id: 1,
      unit_measure_id: 70,
      unit_price: 0,
      tax_id: 10,
      tax_percent: null,
      tax_per_unit_amount: 66,
      is_active: true,
    });
    expect(await screen.findByRole('heading', { level: 1, name: 'Editar producto' })).toBeTruthy();
    await waitFor(() => expect(field(/^Valor por unidad/).value).toBe('66'));
    const active = screen.getByLabelText(/Producto activo/) as HTMLInputElement;
    expect(active.checked).toBe(true);

    fireEvent.click(active);
    save();

    const update = await expectRequest(http, '/api/v1/products/p-1');
    expect(update.request.method).toBe('PUT');
    expect(update.request.body).toEqual({
      code: 'BOL-1',
      name: 'Bolsa plástica',
      description: null,
      unit_measure_id: 70,
      unit_price: '0',
      tax_id: 10,
      tax_percent: null,
      tax_per_unit_amount: '66',
      type_item_identification_id: 1,
      is_active: false,
    });
    update.flush({ id: 'p-1' });
    await waitFor(() => expect(router.url).toBe('/productos'));
  });
});
