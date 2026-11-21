import { fireEvent, screen, waitFor, within } from '@testing-library/angular';
import {
  expectRequest,
  notice,
  params,
  renderNotificationsRoute,
} from '../testing/notifications-testing';

const isList = (request: { url: string; method: string }) =>
  request.method === 'GET' && request.url === '/api/v1/notifications';

describe('NotificationListPage', () => {
  // jsdom has no matchMedia nor scrollIntoView; the Optimus select list uses them.
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

  it('asks the server for the first page and shows read and unread notices', async () => {
    const { http } = await renderNotificationsRoute('/notificaciones');

    const request = await expectRequest(http, isList);
    expect(params(request)).toEqual({ limit: '20' });
    request.flush({
      items: [notice('n-1'), notice('n-2', { is_read: true })],
      next_cursor: null,
      unread_count: 1,
    });

    expect(await screen.findByRole('heading', { level: 1, name: 'Avisos' })).toBeTruthy();
    const table = screen.getByRole('table', { name: 'Avisos' });
    expect(within(table).getByText('Aviso n-1')).toBeTruthy();
    expect(within(table).getByText('Detalle del aviso n-1')).toBeTruthy();
    expect(within(table).getByText('Sin leer')).toBeTruthy();
    expect(within(table).getByText('Leído')).toBeTruthy();
    expect(within(table).getAllByRole('button', { name: /Marcar como leído/ })).toHaveLength(1);
  });

  it('pages with the cursor the server returned', async () => {
    const { http } = await renderNotificationsRoute('/notificaciones');
    (await expectRequest(http, isList)).flush({
      items: [notice('n-1')],
      next_cursor: 'cursor-2',
      unread_count: 1,
    });

    fireEvent.click(await screen.findByRole('button', { name: 'Siguiente' }));

    const next = await expectRequest(http, isList);
    expect(params(next)).toEqual({ cursor: 'cursor-2', limit: '20' });
  });

  it('filters the unread notices on the server from the URL', async () => {
    const { http, router } = await renderNotificationsRoute('/notificaciones?estado=sin-leer');

    const request = await expectRequest(http, isList);
    expect(params(request)).toEqual({ unread: 'true', limit: '20' });
    request.flush({ items: [], next_cursor: null, unread_count: 0 });
    expect(await screen.findByText('No tiene avisos sin leer')).toBeTruthy();

    const filter = screen.getByRole('combobox', { name: 'Mostrar' });
    expect(filter.textContent?.trim()).toBe('Sin leer');
    fireEvent.click(filter);
    fireEvent.click(await screen.findByRole('option', { name: 'Todos' }));
    await waitFor(() => expect(router.url).toBe('/notificaciones'));
    expect(params(await expectRequest(http, isList))).toEqual({ limit: '20' });
  });

  it('marks one notice as read and reloads the page', async () => {
    const { http } = await renderNotificationsRoute('/notificaciones');
    (await expectRequest(http, isList)).flush({
      items: [notice('n-1')],
      next_cursor: null,
      unread_count: 1,
    });

    fireEvent.click(await screen.findByRole('button', { name: 'Marcar como leído: Aviso n-1' }));

    const mark = await expectRequest(http, '/api/v1/notifications/n-1/read');
    expect(mark.request.method).toBe('POST');
    mark.flush(null, { status: 204, statusText: 'No Content' });
    (await expectRequest(http, isList)).flush({
      items: [notice('n-1', { is_read: true })],
      next_cursor: null,
      unread_count: 0,
    });
    expect(await screen.findByText('Leído')).toBeTruthy();
  });

  it('marks every notice as read', async () => {
    const { http } = await renderNotificationsRoute('/notificaciones');
    (await expectRequest(http, isList)).flush({
      items: [notice('n-1'), notice('n-2')],
      next_cursor: null,
      unread_count: 2,
    });

    fireEvent.click(await screen.findByRole('button', { name: 'Marcar todos como leídos' }));

    (await expectRequest(http, '/api/v1/notifications/read-all')).flush({ marked_count: 2 });
    (await expectRequest(http, isList)).flush({ items: [], next_cursor: null, unread_count: 0 });
  });

  it('shows the error state with a retry', async () => {
    const { http } = await renderNotificationsRoute('/notificaciones');
    (await expectRequest(http, isList)).flush(
      { status: 500, code: 'internal', message: 'Falla' },
      { status: 500, statusText: 'Error' },
    );

    expect(await screen.findByText('No se pudieron cargar los avisos')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Reintentar' }));
    await expectRequest(http, isList);
  });
});
