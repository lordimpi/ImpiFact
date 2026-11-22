import { HttpTestingController } from '@angular/common/http/testing';
import { fireEvent, screen, waitFor, within } from '@testing-library/angular';
import { renderDocumentsRoute } from '../testing/documents-testing';
import { RECEIVED } from '../testing/radian-testing';

const DETAIL = '/api/v1/received-documents/rd-1';
const EVENTS = `${DETAIL}/events`;
const isDetail = (request: { method: string; url: string }) =>
  request.method === 'GET' && request.url === DETAIL;
const isSend = (request: { method: string; url: string }) =>
  request.method === 'POST' && request.url === EVENTS;

const ACCEPTANCE = {
  id: 'ev-3',
  event_code: '033',
  event_name: 'Aceptación expresa',
  status: 'accepted',
  cude: 'e'.repeat(96),
  number: 'AE1',
  issued_at: '2026-11-20T14:00:00Z',
  dian_status_code: '00',
  dian_status_description: 'Procesado Correctamente.',
};

/** The answer of a send: the event as the DIAN left it. */
const sent = (event: object) => ({
  success: (event as { status: string }).status === 'accepted',
  message: 'ApplicationResponse #AE1 generada con éxito',
  cude: 'e'.repeat(96),
  event,
});

async function openDetail(invoice: object = RECEIVED, session = {}) {
  const route = await renderDocumentsRoute('/documentos/recibidos/rd-1', session);
  (await waitFor(() => route.http.expectOne(isDetail))).flush(invoice);
  await screen.findByRole('heading', { level: 1, name: 'Factura recibida FV1001' });
  return route;
}

const actionButton = (name: string) =>
  within(
    screen.getByRole('heading', { name: 'Registrar un evento' }).closest('ui-card')!,
  ).getByRole('button', { name }) as HTMLButtonElement;

/** Opens the action, continues its dialog and returns the confirm dialog (loaded on the first question). */
async function continueTo(action: string) {
  fireEvent.click(actionButton(action));
  const dialog = await screen.findByRole('dialog');
  fireEvent.click(within(dialog).getByRole('button', { name: 'Continuar' }));
  return screen.findByRole('alertdialog', {}, { timeout: 8000 });
}

async function confirmSend(http: HttpTestingController, action: string) {
  const confirm = await continueTo(action);
  fireEvent.click(within(confirm).getByRole('button', { name: 'Registrar en la DIAN' }));
  return waitFor(() => http.expectOne(isSend));
}

describe('ReceivedDocumentDetailPage', { timeout: 15_000 }, () => {
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

  afterEach(() => {
    // The confirm dialog lives under <body>, outside the test fixture.
    document.body.querySelectorAll('ui-confirm-dialog').forEach((host) => host.remove());
  });

  it('shows the supplier, totals, CUFE and the history of its events', async () => {
    await openDetail({
      ...RECEIVED,
      events: [
        ...RECEIVED.events,
        {
          ...ACCEPTANCE,
          status: 'rejected',
          dian_status_code: '99',
          dian_status_description: 'Validación contiene errores en campos mandatorios.',
          error_messages: ['Regla: LGC06, Rechazo: el plazo para la aceptación expresa venció.'],
        },
      ],
    });

    const breadcrumb = screen.getByRole('navigation', { name: 'Ruta de navegación' });
    expect(
      within(breadcrumb).getByRole('link', { name: 'Documentos recibidos' }).getAttribute('href'),
    ).toBe('/documentos/recibidos');
    expect(screen.getAllByText('PROVEEDOR DE EJEMPLO SAS').length).toBeGreaterThan(0);
    expect(screen.getByText('800000003-4')).toBeTruthy();
    expect(screen.getByText('a'.repeat(96))).toBeTruthy();
    expect(screen.getAllByText(/1\.190\.000,00/).length).toBeGreaterThan(0);
    expect(screen.getByText('Habilitación')).toBeTruthy();
    expect(screen.getByText('Factura electrónica de venta')).toBeTruthy();

    const history = screen.getByRole('list', { name: 'Historial de eventos RADIAN' });
    const items = [...history.querySelectorAll<HTMLElement>(':scope > li')];
    expect(items.map((item) => item.querySelector('.timeline__name')?.textContent?.trim())).toEqual(
      [
        'Acuse de recibo de la factura (030)',
        'Recibo del bien o prestación del servicio (032)',
        'Aceptación expresa (033)',
      ],
    );
    expect(within(items[0]!).getByText('Registrado')).toBeTruthy();
    expect(within(items[2]!).getByText('Rechazado')).toBeTruthy();
    expect(
      within(items[2]!).getByText('Validación contiene errores en campos mandatorios.'),
    ).toBeTruthy();
    expect(
      within(items[2]!).getByText(
        'Regla: LGC06, Rechazo: el plazo para la aceptación expresa venció.',
      ),
    ).toBeTruthy();
  });

  it('enables each event by the order of the DIAN and explains the ones it cannot send', async () => {
    await openDetail({ ...RECEIVED, events: [RECEIVED.events[0]] });

    expect(actionButton('Acuse de recibo').disabled).toBe(true);
    expect(screen.getByText('Ya está registrado.')).toBeTruthy();
    expect(actionButton('Recibo del bien o servicio').disabled).toBe(false);
    expect(actionButton('Aceptar').disabled).toBe(true);
    expect(actionButton('Reclamar').disabled).toBe(true);
    expect(
      screen.getAllByText('Requiere antes: Recibo del bien o prestación del servicio.'),
    ).toHaveLength(2);
  });

  it('confirms, sends the acceptance and reads the invoice again', async () => {
    const { http, toasts } = await openDetail();
    expect(actionButton('Aceptar').disabled).toBe(false);

    const confirm = await continueTo('Aceptar');
    expect(within(confirm).getByText(/no se puede deshacer ni anular/)).toBeTruthy();
    expect(confirm.textContent).toContain('FV1001');
    fireEvent.click(within(confirm).getByRole('button', { name: 'Registrar en la DIAN' }));

    const send = await waitFor(() => http.expectOne(isSend));
    expect(send.request.body).toEqual({ event_code: '033' });
    send.flush(sent(ACCEPTANCE));

    const reload = await waitFor(() => http.expectOne(isDetail));
    reload.flush({ ...RECEIVED, events: [...RECEIVED.events, ACCEPTANCE] });
    expect(await screen.findAllByText('Aceptada')).toBeTruthy();
    expect(toasts.toasts().map((toast) => [toast.kind, toast.text])).toEqual([
      ['success', 'La DIAN registró el evento «Aceptación expresa».'],
    ]);
    expect(actionButton('Reclamar').disabled).toBe(true);
    expect(
      screen.getByText('No disponible: la factura ya tiene «Aceptación expresa».'),
    ).toBeTruthy();
  });

  it('sends nothing when the person steps back in the confirm dialog', async () => {
    const { http } = await openDetail();

    const confirm = await continueTo('Aceptar');
    fireEvent.click(within(confirm).getByRole('button', { name: 'Volver' }));

    await waitFor(() => expect(screen.queryByRole('alertdialog')).toBeNull());
    http.expectNone(isSend);
    // The event dialog is still there, so the person can go on or cancel.
    expect(screen.getByRole('dialog', { name: 'Aceptar la factura' })).toBeTruthy();
  });

  it('asks the claim reason and the person, and sends them', async () => {
    const { http } = await openDetail();

    fireEvent.click(actionButton('Reclamar'));
    const dialog = await screen.findByRole('dialog', { name: 'Reclamar la factura' });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Continuar' }));
    expect(await within(dialog).findByText('Elija el motivo del reclamo.')).toBeTruthy();

    fireEvent.click(within(dialog).getByRole('combobox', { name: /Motivo del reclamo/ }));
    fireEvent.click(
      await screen.findByRole('option', { name: '02 · Mercancía no entregada totalmente' }),
    );
    fireEvent.click(within(dialog).getByLabelText('Indicar la persona que recibe o decide'));
    const fill = (label: string, value: string) =>
      fireEvent.input(within(dialog).getByLabelText(new RegExp(`^${label}`)), {
        target: { value },
      });
    fill('Número de documento', '1000000001');
    fill('Nombres', 'Ana María');
    fill('Apellidos', 'Pérez Gómez');
    fill('Cargo', 'Auxiliar contable');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Continuar' }));
    // Every field of the person is required once it is asked for.
    expect(
      await within(dialog).findByText('Este dato es obligatorio si indica la persona.'),
    ).toBeTruthy();
    http.expectNone(isSend);
    fill('Área o dependencia', 'Compras');

    fireEvent.click(within(dialog).getByRole('button', { name: 'Continuar' }));
    const confirm = await screen.findByRole('alertdialog', {}, { timeout: 8000 });
    fireEvent.click(within(confirm).getByRole('button', { name: 'Registrar en la DIAN' }));

    const send = await waitFor(() => http.expectOne(isSend));
    expect(send.request.body).toEqual({
      event_code: '031',
      claim_reason: '02',
      issuer_party: {
        identification_number: '1000000001',
        first_name: 'Ana María',
        last_name: 'Pérez Gómez',
        job_title: 'Auxiliar contable',
        organization_department: 'Compras',
      },
    });
  });

  it('shows the reason of a 422 in a toast and reads the invoice again', async () => {
    const { http, toasts } = await openDetail();

    (await confirmSend(http, 'Aceptar')).flush(
      {
        code: 'radian.event_out_of_order',
        message:
          'No se puede registrar el evento 033 (Aceptación expresa) porque el documento ya tiene el evento 031 (Reclamo de la Factura Electrónica de Venta).',
      },
      { status: 422, statusText: 'Unprocessable Entity' },
    );

    await waitFor(() =>
      expect(toasts.toasts().map((toast) => [toast.kind, toast.text])).toEqual([
        [
          'error',
          'No se puede registrar el evento 033 (Aceptación expresa) porque el documento ya tiene el evento 031 (Reclamo de la Factura Electrónica de Venta).',
        ],
      ]),
    );
    (await waitFor(() => http.expectOne(isDetail))).flush(RECEIVED);
  });

  it('explains a plan without RADIAN events (403)', async () => {
    const { http, toasts } = await openDetail();

    (await confirmSend(http, 'Aceptar')).flush(
      {
        code: 'plan.limit_reached',
        message:
          'La empresa ha llegado al limite de documentos del plan por paquetes, por favor renueve su membresia...',
      },
      { status: 403, statusText: 'Forbidden' },
    );

    await waitFor(() =>
      expect(toasts.toasts().map((toast) => toast.text)).toEqual([
        'El plan de la empresa no tiene eventos RADIAN disponibles. Renueve o amplíe el plan para registrar más eventos.',
      ]),
    );
  });

  it('says when the DIAN rejects the event and keeps it in the history to send again', async () => {
    const { http, toasts } = await openDetail();

    (await confirmSend(http, 'Aceptar')).flush(
      sent({ ...ACCEPTANCE, status: 'rejected', error_messages: ['Regla: LGC06'] }),
    );

    await waitFor(() => expect(toasts.toasts().map((toast) => toast.kind)).toEqual(['error']));
    (await waitFor(() => http.expectOne(isDetail))).flush({
      ...RECEIVED,
      events: [
        ...RECEIVED.events,
        { ...ACCEPTANCE, status: 'rejected', error_messages: ['Regla: LGC06'] },
      ],
    });
    expect(await screen.findByText('Regla: LGC06')).toBeTruthy();
    expect(actionButton('Aceptar').disabled).toBe(false);
  });

  it('goes back to the list when the invoice no longer exists (404)', async () => {
    const { http, router } = await openDetail();

    (await confirmSend(http, 'Aceptar')).flush(
      { code: 'received_document.not_found', message: 'No se encontró el documento recibido.' },
      { status: 404, statusText: 'Not Found' },
    );

    await waitFor(() => expect(router.url).toBe('/documentos/recibidos'));
  });

  it('shows what the DIAN reports for the invoice', async () => {
    const { http } = await openDetail();

    fireEvent.click(screen.getByRole('button', { name: 'Consultar en la DIAN' }));
    const query = await waitFor(() => http.expectOne(`${DETAIL}/dian-events`));
    query.flush({
      success: true,
      message: 'Consulta de eventos generada con exito',
      cufe: 'a'.repeat(96),
      events: [
        {
          dian_code: '030',
          description: 'Acuse de recibo de Factura Electrónica de Venta',
          date: '2026-11-19',
          time: '10:40:00',
          event_number: 'AR1',
        },
        { dian_code: '033', description: null, date: '2026-11-20', time: '09:00:00' },
      ],
    });

    const table = await screen.findByRole('table', { name: 'Eventos registrados en la DIAN' });
    expect(within(table).getByText('Acuse de recibo de Factura Electrónica de Venta')).toBeTruthy();
    expect(within(table).getByText('2026-11-19 10:40')).toBeTruthy();
    // An acceptance the DIAN lists also closes the claim.
    expect(within(table).getByText('Aceptación expresa')).toBeTruthy();
    expect(actionButton('Reclamar').disabled).toBe(true);
  });

  it('says when the DIAN does not answer', async () => {
    const { http, toasts } = await openDetail();

    fireEvent.click(screen.getByRole('button', { name: 'Consultar en la DIAN' }));
    (await waitFor(() => http.expectOne(`${DETAIL}/dian-events`))).flush(
      { code: 'dian.unavailable', message: 'El servicio DIAN no se encuentra disponible.' },
      { status: 503, statusText: 'Service Unavailable' },
    );

    await waitFor(() =>
      expect(toasts.toasts().map((toast) => toast.text)).toEqual([
        'La DIAN no respondió. Intente de nuevo en unos minutos.',
      ]),
    );
  });

  it('shows the invoice without event actions to a session that cannot send them', async () => {
    await openDetail(RECEIVED, { scopes: ['documents:read'] });

    expect(screen.queryByRole('heading', { name: 'Registrar un evento' })).toBeNull();
    expect(screen.getByRole('button', { name: 'Consultar en la DIAN' })).toBeTruthy();
  });

  it('shows an error with retry when the invoice cannot be loaded', async () => {
    const { http } = await renderDocumentsRoute('/documentos/recibidos/rd-1');
    (await waitFor(() => http.expectOne(isDetail))).flush(
      { code: 'received_document.not_found', message: 'No se encontró el documento recibido.' },
      { status: 404, statusText: 'Not Found' },
    );

    expect(await screen.findByText('No se pudo cargar la factura recibida')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Reintentar' }));
    (await waitFor(() => http.expectOne(isDetail))).flush(RECEIVED);
    expect(
      await screen.findByRole('heading', { level: 1, name: 'Factura recibida FV1001' }),
    ).toBeTruthy();
  });
});
