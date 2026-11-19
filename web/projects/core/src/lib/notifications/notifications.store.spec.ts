import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { SKIP_ERROR_TOAST } from '../http/api-request';
import { SessionStore } from '../session/session.store';
import { NOTIFICATIONS_POLL_MS, NotificationsStore } from './notifications.store';

const LIST_URL = '/api/v1/notifications';

const unread = (id: string) => ({
  id,
  type: 'resolution.running_out',
  title: `Aviso ${id}`,
  body: 'Quedan pocos números',
  is_read: false,
  created_at: '2026-11-17T10:00:00Z',
});

/** Sesión falsa: el store solo lee estos signals. */
function fakeSession() {
  const isAuthenticated = signal(true);
  const scopes = signal(['documents:read']);
  const companyId = signal<string | null>('company-1');
  return {
    isAuthenticated,
    scopes,
    companyId,
    hasScope: (scope: string) => scopes().includes(scope),
  };
}

function setVisibility(state: 'visible' | 'hidden') {
  Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => state });
  document.dispatchEvent(new Event('visibilitychange'));
}

describe('NotificationsStore', () => {
  let session: ReturnType<typeof fakeSession>;
  let http: HttpTestingController;

  function setUp() {
    session = fakeSession();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: SessionStore, useValue: session },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    const store = TestBed.inject(NotificationsStore);
    TestBed.tick();
    return store;
  }

  const pending = () => http.match((request) => request.url === LIST_URL);
  /** Deja correr las promesas pendientes (la respuesta llega al store en una microtarea). */
  const settle = () => vi.advanceTimersByTimeAsync(0);

  beforeEach(() => {
    vi.useFakeTimers();
    setVisibility('visible');
  });

  afterEach(() => {
    vi.useRealTimers();
    setVisibility('visible');
  });

  it('loads the unread count and the latest notices silently as soon as there is a company', async () => {
    const store = setUp();

    const [request] = pending();
    expect(request!.request.params.get('limit')).toBe('5');
    expect(request!.request.params.has('unread')).toBe(false);
    expect(request!.request.context.get(SKIP_ERROR_TOAST)).toBe(true);
    request!.flush({ items: [unread('n-1')], next_cursor: null, unread_count: 3 });
    await settle();

    expect(store.unreadCount()).toBe(3);
    expect(store.latest().map((item) => item.id)).toEqual(['n-1']);
  });

  it('polls every 60 seconds', async () => {
    const store = setUp();
    pending()[0]!.flush({ items: [], next_cursor: null, unread_count: 0 });

    vi.advanceTimersByTime(NOTIFICATIONS_POLL_MS - 1);
    expect(pending()).toHaveLength(0);
    vi.advanceTimersByTime(1);
    const [next] = pending();
    next!.flush({ items: [unread('n-2')], next_cursor: null, unread_count: 1 });
    await settle();

    expect(store.unreadCount()).toBe(1);
  });

  it('pauses while the tab is hidden and refreshes when it comes back', () => {
    setUp();
    pending()[0]!.flush({ items: [], next_cursor: null, unread_count: 0 });

    setVisibility('hidden');
    vi.advanceTimersByTime(NOTIFICATIONS_POLL_MS * 3);
    expect(pending()).toHaveLength(0);

    setVisibility('visible');
    expect(pending()).toHaveLength(1);
  });

  it('stops polling and clears the count on logout', () => {
    const store = setUp();
    pending()[0]!.flush({ items: [unread('n-1')], next_cursor: null, unread_count: 4 });

    session.isAuthenticated.set(false);
    TestBed.tick();

    expect(store.unreadCount()).toBe(0);
    expect(store.latest()).toEqual([]);
    vi.advanceTimersByTime(NOTIFICATIONS_POLL_MS * 2);
    expect(pending()).toHaveLength(0);
  });

  it('does not poll without the documents:read scope', () => {
    session = fakeSession();
    session.scopes.set(['platform:*']);
    session.companyId.set(null);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: SessionStore, useValue: session },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(NotificationsStore);
    TestBed.tick();

    vi.advanceTimersByTime(NOTIFICATIONS_POLL_MS);
    expect(pending()).toHaveLength(0);
  });

  it('ignores a late answer from the company the session left', async () => {
    const store = setUp();
    const [stale] = pending();

    session.companyId.set('company-2');
    TestBed.tick();
    stale!.flush({ items: [unread('old')], next_cursor: null, unread_count: 9 });
    pending()[0]!.flush({ items: [], next_cursor: null, unread_count: 1 });
    await settle();

    expect(store.unreadCount()).toBe(1);
    expect(store.latest()).toEqual([]);
  });

  it('marks one notice as read and lowers the count', async () => {
    const store = setUp();
    pending()[0]!.flush({ items: [unread('n-1')], next_cursor: null, unread_count: 2 });

    const done = store.markRead({ id: 'n-1', is_read: false });
    const request = http.expectOne('/api/v1/notifications/n-1/read');
    expect(request.request.method).toBe('POST');
    request.flush(null, { status: 204, statusText: 'No Content' });

    expect(await done).toBe(true);
    expect(store.unreadCount()).toBe(1);
    expect(store.latest()[0]!.is_read).toBe(true);
  });

  it('marks every notice as read', async () => {
    const store = setUp();
    pending()[0]!.flush({
      items: [unread('n-1'), unread('n-2')],
      next_cursor: null,
      unread_count: 2,
    });

    const done = store.markAllRead();
    http.expectOne('/api/v1/notifications/read-all').flush({ marked_count: 2 });

    expect(await done).toBe(2);
    expect(store.unreadCount()).toBe(0);
    expect(store.latest().every((item) => item.is_read)).toBe(true);
  });
});
