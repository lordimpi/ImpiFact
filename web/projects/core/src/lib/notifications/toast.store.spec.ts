import { TestBed } from '@angular/core/testing';
import { MAX_TOASTS, ToastStore } from './toast.store';

describe('ToastStore', () => {
  let store: InstanceType<typeof ToastStore>;

  beforeEach(() => {
    vi.useFakeTimers();
    store = TestBed.inject(ToastStore);
  });

  afterEach(() => vi.useRealTimers());

  it('adds toasts with increasing ids and dismisses them by id', () => {
    const first = store.show({ kind: 'info', text: 'Guardado' });
    const second = store.show({ kind: 'error', text: 'Falló', correlationId: 'abc' });

    expect(second).toBeGreaterThan(first);
    expect(store.toasts()).toEqual([
      { id: first, kind: 'info', text: 'Guardado', correlationId: null },
      { id: second, kind: 'error', text: 'Falló', correlationId: 'abc' },
    ]);

    store.dismiss(first);
    expect(store.toasts().map((toast) => toast.id)).toEqual([second]);
  });

  it('closes info and success toasts by themselves and keeps errors until dismissed', () => {
    store.show({ kind: 'success', text: 'Listo' });
    store.show({ kind: 'error', text: 'Falló' });

    vi.advanceTimersByTime(5_000);

    expect(store.toasts().map((toast) => toast.kind)).toEqual(['error']);
  });

  it('replaces a toast that reuses a key instead of stacking it', () => {
    store.show({ kind: 'warning', text: 'Quedan 5 minutos', key: 'inactivity' });
    store.show({ kind: 'warning', text: 'Quedan 4 minutos', key: 'inactivity' });

    expect(store.toasts().map((toast) => toast.text)).toEqual(['Quedan 4 minutos']);
    store.dismissKey('inactivity');
    expect(store.toasts()).toEqual([]);
  });

  it(`keeps at most ${MAX_TOASTS} toasts, dropping the oldest`, () => {
    for (let index = 1; index <= MAX_TOASTS + 2; index++) {
      store.show({ kind: 'error', text: `Error ${index}` });
    }

    expect(store.toasts()).toHaveLength(MAX_TOASTS);
    expect(store.toasts()[0]?.text).toBe('Error 3');
  });
});
