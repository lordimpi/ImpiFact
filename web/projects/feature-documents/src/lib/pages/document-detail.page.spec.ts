import { fireEvent, screen, waitFor, within } from '@testing-library/angular';
import { renderDocumentsRoute } from '../testing/documents-testing';

export const DETAIL = {
  id: 'doc-1',
  full_number: 'SETP990000002',
  kind: 'invoice',
  issue_date: '2026-11-14',
  issue_time: '10:05:00',
  status: 'Queued',
  email_status: 'NotSent',
  cufe: 'cufe-123',
  track_id: null,
  dian_status_code: null,
  dian_status_description: null,
  notes: 'Gracias por su compra',
  customer: {
    name: 'CLIENTE DE PRUEBA GOLDEN SAS',
    identification_number: '800000001',
    dv: '1',
    email: 'cliente.golden@example.com',
  },
  lines: [
    {
      line_number: 1,
      code: 'SERV01',
      description: 'Servicio de consultoría',
      invoiced_quantity: '1',
      price_amount: '1500000.00',
      line_extension_amount: '1500000.00',
    },
  ],
  taxes: [{ tax_id: 1, percent: '19.00', tax_amount: '285000.00', taxable_amount: '1500000.00' }],
  totals: {
    line_extension_amount: '1500000.00',
    tax_exclusive_amount: '1500000.00',
    tax_inclusive_amount: '1785000.00',
    allowance_total_amount: '0.00',
    charge_total_amount: '0.00',
    pre_paid_amount: '0.00',
    payable_amount: '1785000.00',
  },
};

const flushMicrotasks = () => new Promise((resolve) => setTimeout(resolve));

describe('DocumentDetailPage', () => {
  afterEach(() => vi.useRealTimers());

  it('shows the header, customer, lines, totals and DIAN status of the document', async () => {
    const { http } = await renderDocumentsRoute('/documentos/doc-1');
    (await waitFor(() => http.expectOne('/api/v1/documents/doc-1'))).flush({
      ...DETAIL,
      status: 'Rejected',
      dian_status_code: '99',
      dian_status_description: 'Regla FAD06: el CUFE no es válido.',
    });

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Factura SETP990000002' }),
    ).toBeTruthy();
    expect(screen.getByText('Rechazado')).toBeTruthy();
    expect(screen.getByText('CLIENTE DE PRUEBA GOLDEN SAS')).toBeTruthy();
    const line = screen.getByText('Servicio de consultoría').closest('tr')!;
    expect(within(line).getByText('SERV01')).toBeTruthy();
    expect(screen.getByText('Regla FAD06: el CUFE no es válido.')).toBeTruthy();
    expect(screen.getAllByText(/1\.785\.000,00/).length).toBeGreaterThan(0);
    expect(screen.getByText(/IVA 19/)).toBeTruthy();
    // Sin aceptación no hay PDF ni acciones de correo.
    expect(screen.queryByRole('button', { name: 'PDF' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Reenviar correo al cliente' })).toBeNull();
    expect(screen.getByRole('button', { name: 'XML firmado' })).toBeTruthy();
  });

  it('polls with backoff while the document is pending and stops when it is accepted', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const { http } = await renderDocumentsRoute('/documentos/doc-1');
    (await waitFor(() => http.expectOne('/api/v1/documents/doc-1'))).flush(DETAIL);
    expect(await screen.findByText('En cola')).toBeTruthy();
    expect(screen.getByText('Consultando el estado en la DIAN…')).toBeTruthy();

    await vi.advanceTimersByTimeAsync(2_000);
    http.expectOne('/api/v1/documents/doc-1').flush({ ...DETAIL, status: 'Sent' });
    expect(await screen.findByText('Enviado a la DIAN')).toBeTruthy();

    await vi.advanceTimersByTimeAsync(3_000);
    http.expectNone('/api/v1/documents/doc-1');
    await vi.advanceTimersByTimeAsync(1_000);
    http.expectOne('/api/v1/documents/doc-1').flush({ ...DETAIL, status: 'Accepted' });

    expect(await screen.findByText('Aceptado')).toBeTruthy();
    expect(screen.queryByText('Consultando el estado en la DIAN…')).toBeNull();
    expect(screen.getByRole('button', { name: 'PDF' })).toBeTruthy();
    await vi.advanceTimersByTimeAsync(120_000);
    http.expectNone('/api/v1/documents/doc-1');
  });

  it('stops polling when the page is left', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const { http, router } = await renderDocumentsRoute('/documentos/doc-1');
    (await waitFor(() => http.expectOne('/api/v1/documents/doc-1'))).flush(DETAIL);
    await screen.findByText('En cola');

    await router.navigateByUrl('/');
    await flushMicrotasks();
    await vi.advanceTimersByTimeAsync(60_000);

    http.expectNone('/api/v1/documents/doc-1');
  });

  it('runs the actions of an accepted document', async () => {
    URL.createObjectURL = vi.fn(() => 'blob:doc');
    URL.revokeObjectURL = vi.fn();
    const { http, toasts } = await renderDocumentsRoute('/documentos/doc-1');
    (await waitFor(() => http.expectOne('/api/v1/documents/doc-1'))).flush({
      ...DETAIL,
      status: 'Accepted',
    });

    fireEvent.click(await screen.findByRole('button', { name: 'Regenerar PDF' }));
    const regenerate = await waitFor(() =>
      http.expectOne('/api/v1/documents/doc-1/pdf/regenerate'),
    );
    expect(regenerate.request.method).toBe('POST');
    regenerate.flush({ success: true });
    await waitFor(() =>
      expect(toasts.toasts().map((toast) => toast.text)).toEqual(['El PDF se está regenerando.']),
    );

    fireEvent.click(screen.getByRole('button', { name: 'ZIP del correo' }));
    (await waitFor(() => http.expectOne('/api/v1/documents/doc-1/files/zip'))).flush(
      new Blob(['zip']),
    );
    await waitFor(() => expect(URL.createObjectURL).toHaveBeenCalled());
  });

  it('shows an error with retry when the document cannot be loaded', async () => {
    const { http } = await renderDocumentsRoute('/documentos/doc-1');
    (await waitFor(() => http.expectOne('/api/v1/documents/doc-1'))).flush(
      { code: 'document.not_found' },
      { status: 404, statusText: 'Not Found' },
    );

    expect(await screen.findByText('No se pudo cargar el documento')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Reintentar' }));
    (await waitFor(() => http.expectOne('/api/v1/documents/doc-1'))).flush({
      ...DETAIL,
      status: 'Accepted',
    });
    expect(await screen.findByText('Aceptado')).toBeTruthy();
  });
});
