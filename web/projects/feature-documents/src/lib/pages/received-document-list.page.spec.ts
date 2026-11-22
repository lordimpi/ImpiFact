import { HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { fireEvent, screen, waitFor, within } from '@testing-library/angular';
import { renderDocumentsRoute } from '../testing/documents-testing';
import { RECEIVED } from '../testing/radian-testing';

const LIST = '/api/v1/received-documents';
const isList = (request: { method: string; url: string }) =>
  request.method === 'GET' && request.url === LIST;
const isLoad = (request: { method: string; url: string }) =>
  request.method === 'POST' && request.url === LIST;

async function respondList(
  http: HttpTestingController,
  data: object[],
  total = data.length,
): Promise<TestRequest> {
  const request = await waitFor(() => http.expectOne(isList));
  request.flush({ data, total_count: total, limit: 20, offset: 0 });
  return request;
}

/** The folded section with the date filters; its summary reads «Más/Menos filtros». */
const moreFilters = () => screen.getByText(/^(Más|Menos) filtros$/).closest('details')!;

describe('ReceivedDocumentListPage', () => {
  // jsdom has no matchMedia nor scrollIntoView; the Optimus date picker and dialog use them.
  beforeAll(() => {
    window.matchMedia ??= ((query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
      addListener: () => undefined,
      removeListener: () => undefined,
      dispatchEvent: () => false,
    })) as typeof window.matchMedia;
    Element.prototype.scrollIntoView ??= () => undefined;
  });

  it('loads the page with the filters of the URL and shows each invoice with its RADIAN step', async () => {
    const { http } = await renderDocumentsRoute(
      '/documentos/recibidos?proveedor=800000003&desde=2026-11-01',
    );

    const request = await respondList(http, [RECEIVED]);
    expect(request.request.params.get('supplier_nit')).toBe('800000003');
    expect(request.request.params.get('from')).toBe('2026-11-01');
    expect(request.request.params.get('to')).toBeNull();
    expect(request.request.params.get('limit')).toBe('20');
    expect(request.request.params.get('offset')).toBe('0');

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Documentos recibidos' }),
    ).toBeTruthy();
    const row = (await screen.findByText('FV1001')).closest('tr')!;
    expect(within(row).getByText('PROVEEDOR DE EJEMPLO SAS')).toBeTruthy();
    expect(within(row).getByText('800000003-4')).toBeTruthy();
    expect(within(row).getByText('Bien recibido')).toBeTruthy();
    expect(within(row).getByText(/1\.190\.000,00/)).toBeTruthy();
    expect(within(row).getByRole('link', { name: 'Ver FV1001' }).getAttribute('href')).toBe(
      '/documentos/recibidos/rd-1',
    );
    expect((screen.getByLabelText('NIT del proveedor') as HTMLInputElement).value).toBe(
      '800000003',
    );
    // With dates in the URL the folded section opens, so the active filter is visible.
    expect(moreFilters().open).toBe(true);
    expect(screen.getByText('1 facturas con estos filtros')).toBeTruthy();
  });

  it('pages on the server with limit and offset', async () => {
    const { http } = await renderDocumentsRoute('/documentos/recibidos');
    await respondList(http, [RECEIVED], 45);
    await screen.findByText('FV1001');

    const nav = screen.getByRole('navigation', { name: 'Paginación' });
    fireEvent.click(within(nav).getByRole('button', { name: 'Página 2' }));

    const second = await waitFor(() => http.expectOne(isList));
    expect(second.request.params.get('offset')).toBe('20');
    expect(second.request.params.get('limit')).toBe('20');
    second.flush({ data: [{ ...RECEIVED, id: 'rd-2', number: 'FV1002' }], total_count: 45 });
    expect(await screen.findByText('FV1002')).toBeTruthy();
  });

  it('applies the filters through the URL and starts again from the first page', async () => {
    const { http, router } = await renderDocumentsRoute('/documentos/recibidos');
    await respondList(http, []);
    expect(await screen.findByText('No hay facturas recibidas')).toBeTruthy();
    expect(moreFilters().open).toBe(false);

    fireEvent.input(screen.getByLabelText('NIT del proveedor'), {
      target: { value: '900123456' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Aplicar filtros' }));

    const request = await waitFor(() => http.expectOne(isList));
    expect(request.request.params.get('supplier_nit')).toBe('900123456');
    expect(request.request.params.get('offset')).toBe('0');
    expect(router.url).toBe('/documentos/recibidos?proveedor=900123456');
    request.flush({ data: [], total_count: 0 });

    fireEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }));
    const cleared = await waitFor(() => http.expectOne(isList));
    expect(cleared.request.params.get('supplier_nit')).toBeNull();
    expect(router.url).toBe('/documentos/recibidos');
  });

  it('does not query a date range that ends before it starts', async () => {
    const { http } = await renderDocumentsRoute(
      '/documentos/recibidos?desde=2026-11-20&hasta=2026-11-10',
    );
    await respondList(http, []);
    await screen.findByText('No hay facturas recibidas');

    fireEvent.click(screen.getByRole('button', { name: 'Aplicar filtros' }));

    expect(
      await screen.findByText('La fecha «Hasta» debe ser igual o posterior a «Desde».'),
    ).toBeTruthy();
    http.expectNone(isList);
  });

  it('offers no loading by CUFE without the issuing scope', async () => {
    const { http } = await renderDocumentsRoute('/documentos/recibidos', {
      scopes: ['documents:read'],
    });
    await respondList(http, [RECEIVED]);
    await screen.findByText('FV1001');

    expect(screen.queryByRole('button', { name: 'Cargar por CUFE' })).toBeNull();
  });

  describe('Cargar por CUFE', () => {
    async function openDialog() {
      const route = await renderDocumentsRoute('/documentos/recibidos');
      await respondList(route.http, []);
      fireEvent.click(await screen.findByRole('button', { name: 'Cargar por CUFE' }));
      const dialog = await screen.findByRole('dialog', { name: 'Cargar factura por CUFE' });
      return { ...route, dialog };
    }

    it('checks the CUFE before asking the API', async () => {
      const { http, dialog } = await openDialog();

      fireEvent.click(within(dialog).getByRole('button', { name: 'Cargar factura' }));
      expect(await within(dialog).findByText('Escriba el CUFE de la factura.')).toBeTruthy();
      fireEvent.input(within(dialog).getByLabelText(/CUFE/), { target: { value: 'no-es-hex' } });
      fireEvent.click(within(dialog).getByRole('button', { name: 'Cargar factura' }));

      expect(
        await within(dialog).findByText(
          'El CUFE es un código hexadecimal (0-9 y a-f) de hasta 96 caracteres.',
        ),
      ).toBeTruthy();
      http.expectNone(isLoad);
    });

    it('opens a new invoice (201) and says it was loaded', async () => {
      const { http, dialog, router, toasts } = await openDialog();
      const cufe = 'A'.repeat(96);

      fireEvent.input(within(dialog).getByLabelText(/CUFE/), { target: { value: ` ${cufe} ` } });
      fireEvent.click(within(dialog).getByRole('button', { name: 'Cargar factura' }));

      const load = await waitFor(() => http.expectOne(isLoad));
      expect(load.request.body).toEqual({ cufe: 'a'.repeat(96) });
      load.flush(
        { ...RECEIVED, id: 'rd-9', number: 'FV9' },
        { status: 201, statusText: 'Created' },
      );

      await waitFor(() => expect(router.url).toBe('/documentos/recibidos/rd-9'));
      expect(toasts.toasts().map((toast) => [toast.kind, toast.text])).toEqual([
        ['success', 'Factura FV9 cargada.'],
      ]);
    });

    it('opens an invoice already loaded (200) with an informative notice', async () => {
      const { http, dialog, router, toasts } = await openDialog();

      fireEvent.input(within(dialog).getByLabelText(/CUFE/), {
        target: { value: 'a'.repeat(96) },
      });
      fireEvent.click(within(dialog).getByRole('button', { name: 'Cargar factura' }));
      (await waitFor(() => http.expectOne(isLoad))).flush(RECEIVED);

      await waitFor(() => expect(router.url).toBe('/documentos/recibidos/rd-1'));
      expect(toasts.toasts().map((toast) => [toast.kind, toast.text])).toEqual([
        ['info', 'La factura FV1001 ya estaba cargada.'],
      ]);
    });

    it('keeps the dialog open with the reason of the API next to the field', async () => {
      const { http, dialog, router, toasts } = await openDialog();

      fireEvent.input(within(dialog).getByLabelText(/CUFE/), {
        target: { value: 'b'.repeat(96) },
      });
      fireEvent.click(within(dialog).getByRole('button', { name: 'Cargar factura' }));
      (await waitFor(() => http.expectOne(isLoad))).flush(
        {
          code: 'radian.wrong_buyer',
          message: 'El adquiriente del documento no corresponde a la empresa con nit: 900123456',
        },
        { status: 422, statusText: 'Unprocessable Entity' },
      );

      expect(
        await within(dialog).findByText(
          'El adquiriente del documento no corresponde a la empresa con nit: 900123456',
        ),
      ).toBeTruthy();
      expect(router.url).toBe('/documentos/recibidos');
      // The reason is next to the field, not repeated in a toast.
      expect(toasts.toasts()).toEqual([]);
    });
  });
});
