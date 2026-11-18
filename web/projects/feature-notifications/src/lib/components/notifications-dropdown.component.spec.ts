import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { NotificationsStore, SessionStore } from '@impifact/core';
import { TranslocoService } from '@jsverse/transloco';
import { fireEvent, render, screen, waitFor } from '@testing-library/angular';
import { loadNotificationsTexts } from '../notifications-i18n';
import { fakeSession, notice, translationProviders } from '../testing/notifications-testing';
import { NotificationsDropdownComponent } from './notifications-dropdown.component';

const isLatest = (request: { url: string; method: string }) =>
  request.method === 'GET' && request.url === '/api/v1/notifications';

async function renderDropdown() {
  const session = fakeSession();
  session.scopes.set(['documents:read']);
  await render(NotificationsDropdownComponent, {
    providers: [
      provideRouter([]),
      provideHttpClient(),
      provideHttpClientTesting(),
      translationProviders(),
      { provide: SessionStore, useValue: session },
    ],
  });
  await loadNotificationsTexts(TestBed.inject(TranslocoService));
  TestBed.tick();
  return { http: TestBed.inject(HttpTestingController), store: TestBed.inject(NotificationsStore) };
}

/** Responde todas las consultas de los últimos avisos (la del store al empezar y la del componente al abrir). */
async function flushLatest(http: HttpTestingController, body: Record<string, unknown>) {
  // `match` saca de la cola lo que encuentra: se responde lo mismo que se encontró.
  const found = await waitFor(() => {
    const requests = http.match(isLatest);
    expect(requests.length).toBeGreaterThan(0);
    return requests;
  });
  found.forEach((request) => request.flush(body));
}

describe('NotificationsDropdownComponent', () => {
  it('lists the latest notices and links to the full page', async () => {
    const { http } = await renderDropdown();
    await flushLatest(http, {
      items: [notice('n-1'), notice('n-2', { is_read: true })],
      next_cursor: null,
      unread_count: 1,
    });

    expect(await screen.findByText('Aviso n-1')).toBeTruthy();
    expect(screen.getByText('Aviso n-2')).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Ver todos los avisos' }).getAttribute('href')).toBe(
      '/notificaciones',
    );
  });

  it('marks one and then every notice as read', async () => {
    const { http, store } = await renderDropdown();
    await flushLatest(http, {
      items: [notice('n-1'), notice('n-2')],
      next_cursor: null,
      unread_count: 2,
    });

    fireEvent.click(await screen.findByRole('button', { name: 'Marcar como leído: Aviso n-1' }));
    (await waitFor(() => http.expectOne('/api/v1/notifications/n-1/read'))).flush(null, {
      status: 204,
      statusText: 'No Content',
    });
    await waitFor(() => expect(store.unreadCount()).toBe(1));

    fireEvent.click(screen.getByRole('button', { name: 'Marcar todos como leídos' }));
    (await waitFor(() => http.expectOne('/api/v1/notifications/read-all'))).flush({
      marked_count: 1,
    });
    await waitFor(() => expect(store.unreadCount()).toBe(0));
    expect(
      (screen.getByRole('button', { name: 'Marcar todos como leídos' }) as HTMLButtonElement)
        .disabled,
    ).toBe(true);
  });

  it('shows the empty state', async () => {
    const { http } = await renderDropdown();
    await flushLatest(http, { items: [], next_cursor: null, unread_count: 0 });

    expect(await screen.findByText('No tiene avisos')).toBeTruthy();
  });
});
