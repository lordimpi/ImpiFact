import { TestRequest } from '@angular/common/http/testing';
import { fireEvent, screen, waitFor, within } from '@testing-library/angular';
import { renderDocumentsRoute } from '../testing/documents-testing';

const ROW = {
  id: 'doc-1',
  full_number: 'SETP990000001',
  kind: 'invoice',
  issue_date: '2026-11-14',
  status: 'Accepted',
  email_status: 'Sent',
  customer: { name: 'CLIENTE GOLDEN SAS', identification_number: '800000001' },
  totals: { payable_amount: '1785000.00' },
};

const isList = (request: { url: string }) => request.url === '/api/v1/documents';

/** The collapsible section that holds the DIAN status checkboxes; its summary reads «Más/Menos filtros». */
const moreFilters = () => screen.getByText(/^(Más|Menos) filtros$/).closest('details')!;

describe('DocumentListPage', () => {
  // jsdom has no matchMedia nor scrollIntoView; the Optimus select and date picker overlays use them.
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

  beforeEach(() => {
    URL.createObjectURL = vi.fn(() => 'blob:documents');
    URL.revokeObjectURL = vi.fn();
  });

  it('loads the page with the filters of the URL and shows each document', async () => {
    const { http } = await renderDocumentsRoute(
      '/documentos?estado=Accepted,Rejected&cliente=golden&desde=2026-11-01',
    );

    const request = await waitFor(() => http.expectOne(isList));
    expect(request.request.params.getAll('status')).toEqual(['Accepted', 'Rejected']);
    expect(request.request.params.get('customer')).toBe('golden');
    expect(request.request.params.get('from')).toBe('2026-11-01');
    request.flush({ items: [ROW], next_cursor: null });

    expect(await screen.findByRole('heading', { level: 1, name: 'Documentos' })).toBeTruthy();
    const row = (await screen.findByText('SETP990000001')).closest('tr')!;
    expect(within(row).getByText('CLIENTE GOLDEN SAS')).toBeTruthy();
    expect(within(row).getByText('Aceptado')).toBeTruthy();
    expect(within(row).getByText(/1\.785\.000,00/)).toBeTruthy();
    expect(within(row).getByRole('link', { name: 'Ver SETP990000001' }).getAttribute('href')).toBe(
      '/documentos/doc-1',
    );
    expect((screen.getByLabelText('Cliente') as HTMLInputElement).value).toBe('golden');
    expect((screen.getByLabelText('Cliente') as HTMLInputElement).placeholder).toBe(
      'Buscar por identificación o nombre',
    );
    expect((screen.getByLabelText('Rechazado') as HTMLInputElement).checked).toBe(true);
    // With DIAN statuses in the URL the collapsible section opens, so the active filter is visible.
    expect(moreFilters().open).toBe(true);
    expect(moreFilters().textContent).toContain('Estado DIAN: 2');
    await waitFor(() => expect(screen.getByText('Menos filtros')).toBeTruthy());
    // The date filter shows the date of the URL as dd/mm/aaaa.
    expect((screen.getByRole('combobox', { name: 'Desde' }) as HTMLInputElement).value).toBe(
      '01/11/2026',
    );
  });

  it('reads «Más filtros» while the status section is folded and «Menos filtros» while it is open', async () => {
    const { http } = await renderDocumentsRoute('/documentos');
    (await waitFor(() => http.expectOne(isList))).flush({ items: [], next_cursor: null });

    const summary = await screen.findByText('Más filtros');
    expect(moreFilters().open).toBe(false);
    fireEvent.click(summary);
    await waitFor(() => expect(screen.getByText('Menos filtros')).toBeTruthy());
    expect(moreFilters().open).toBe(true);
    expect(screen.queryByText('Más filtros')).toBeNull();

    fireEvent.click(screen.getByText('Menos filtros'));
    await waitFor(() => expect(screen.getByText('Más filtros')).toBeTruthy());
    expect(moreFilters().open).toBe(false);
  });

  it('puts the applied filters in the URL and asks the API again', async () => {
    const { http, router } = await renderDocumentsRoute('/documentos');
    (await waitFor(() => http.expectOne(isList))).flush({ items: [], next_cursor: null });

    // Without DIAN statuses the long status list stays folded under «Más filtros».
    expect(moreFilters().open).toBe(false);
    const kind = await screen.findByRole('combobox', { name: 'Tipo de documento' });
    expect(kind.textContent?.trim()).toBe('Todos los tipos');
    fireEvent.click(kind);
    fireEvent.click(await screen.findByRole('option', { name: 'Nota crédito' }));
    const from = screen.getByRole('combobox', { name: 'Desde' });
    fireEvent.keyDown(from, { key: '0' });
    fireEvent.input(from, { target: { value: '01/11/2026' } });
    fireEvent.click(screen.getByLabelText('En cola'));
    fireEvent.click(screen.getByLabelText('Enviado a la DIAN'));
    fireEvent.input(screen.getByLabelText('Número'), { target: { value: 'NC10' } });
    fireEvent.click(screen.getByLabelText('Mostrar cuántos documentos hay'));
    fireEvent.click(screen.getByRole('button', { name: 'Aplicar filtros' }));

    await waitFor(() =>
      expect(router.url).toBe(
        '/documentos?desde=2026-11-01&tipo=credit_note&estado=Queued,Sent&numero=NC10&total=1',
      ),
    );
    const request = await waitFor(() => http.expectOne(isList));
    expect(request.request.params.get('from')).toBe('2026-11-01');
    expect(request.request.params.get('kind')).toBe('credit_note');
    expect(request.request.params.getAll('status')).toEqual(['Queued', 'Sent']);
    expect(request.request.params.get('number')).toBe('NC10');
    expect(request.request.params.get('include_total')).toBe('true');
    request.flush({ items: [ROW], next_cursor: null, total_count: 1 });

    expect(await screen.findByText('1 documentos con estos filtros')).toBeTruthy();
  });

  it('shows the empty state and the error state with retry', async () => {
    const { http } = await renderDocumentsRoute('/documentos');
    (await waitFor(() => http.expectOne(isList))).flush(
      { code: 'http.unexpected' },
      { status: 500, statusText: 'Server Error' },
    );

    expect(await screen.findByText('No se pudieron cargar los documentos')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Reintentar' }));
    (await waitFor(() => http.expectOne(isList))).flush({ items: [], next_cursor: null });

    expect(await screen.findByText('No hay documentos')).toBeTruthy();
  });

  it('offers to issue only with the documents:write scope', async () => {
    const { http } = await renderDocumentsRoute('/documentos', { scopes: ['documents:read'] });
    (await waitFor(() => http.expectOne(isList))).flush({ items: [ROW], next_cursor: null });

    await screen.findByText('SETP990000001');
    expect(screen.queryByRole('link', { name: 'Emitir factura' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Reenviar correo' })).toBeNull();
  });

  it('exports the CSV with the same filters through HttpClient and saves the file', async () => {
    const { http, toasts } = await renderDocumentsRoute('/documentos?tipo=invoice&estado=Accepted');
    (await waitFor(() => http.expectOne(isList))).flush({ items: [ROW], next_cursor: null });

    fireEvent.click(await screen.findByRole('button', { name: 'Exportar CSV' }));

    const request: TestRequest = await waitFor(() =>
      http.expectOne((r) => r.url === '/api/v1/documents/export.csv'),
    );
    expect(request.request.responseType).toBe('blob');
    expect(request.request.params.get('kind')).toBe('invoice');
    expect(request.request.params.getAll('status')).toEqual(['Accepted']);
    expect(request.request.params.has('limit')).toBe(false);
    request.flush(new Blob(['numero;cliente']), {
      headers: { 'Content-Disposition': 'attachment; filename="documentos-2026.csv"' },
    });

    await waitFor(() => expect(URL.createObjectURL).toHaveBeenCalledTimes(1));
    await waitFor(() =>
      expect(toasts.toasts().map((toast) => toast.text)).toEqual(['Se descargó el archivo CSV.']),
    );
  });

  it('downloads the PDF of a row and resends its email', async () => {
    const { http, toasts } = await renderDocumentsRoute('/documentos');
    (await waitFor(() => http.expectOne(isList))).flush({ items: [ROW], next_cursor: null });

    fireEvent.click(await screen.findByRole('button', { name: 'Descargar PDF' }));
    const pdf = await waitFor(() => http.expectOne('/api/v1/documents/doc-1/files/pdf'));
    expect(pdf.request.responseType).toBe('blob');
    pdf.flush(new Blob(['%PDF']));
    await waitFor(() => expect(URL.createObjectURL).toHaveBeenCalled());

    fireEvent.click(screen.getByRole('button', { name: 'Reenviar correo' }));
    const email = await waitFor(() => http.expectOne('/api/v1/documents/doc-1/email'));
    expect(email.request.method).toBe('POST');
    email.flush({ success: true, message: 'ok' });
    await waitFor(() =>
      expect(toasts.toasts().map((toast) => toast.text)).toEqual([
        'El correo quedó en cola para enviarse.',
      ]),
    );
  });
});
