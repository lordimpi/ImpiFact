import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Translation, TranslocoLoader, provideTransloco } from '@jsverse/transloco';
import { of } from 'rxjs';
import { ToastStore } from '../notifications/toast.store';
import { SessionStore } from '../session/session.store';
import { FakeSessionSync, panelTokens, provideFakeSessionSync } from '../testing/fakes';
import { InactivityMonitor } from './inactivity-monitor';

const MINUTE = 60_000;

class InMemoryLoader implements TranslocoLoader {
  getTranslation() {
    return of({
      core: { inactivity: { warning: 'Su sesión se cerrará en {{ minutes }} minutos.' } },
    } as Translation);
  }
}

describe('InactivityMonitor', () => {
  let sync: FakeSessionSync;
  let session: InstanceType<typeof SessionStore>;
  let toasts: InstanceType<typeof ToastStore>;
  let monitor: InactivityMonitor;
  let logout: ReturnType<typeof vi.fn>;
  let visibility: DocumentVisibilityState;

  beforeEach(() => {
    vi.useFakeTimers();
    visibility = 'visible';
    vi.spyOn(document, 'visibilityState', 'get').mockImplementation(() => visibility);
    const fake = provideFakeSessionSync();
    sync = fake.sync;
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideTransloco({
          config: { availableLangs: ['es-CO'], defaultLang: 'es-CO' },
          loader: InMemoryLoader,
        }),
        fake.provider,
      ],
    });
    vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    session = TestBed.inject(SessionStore);
    toasts = TestBed.inject(ToastStore);
    logout = vi.fn(async () => undefined);
    session.logout = logout as typeof session.logout;
    monitor = TestBed.inject(InactivityMonitor);
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  function signIn() {
    sync.receive({ type: 'session', tokens: panelTokens(), email: null });
    TestBed.tick();
  }

  function setVisibility(state: DocumentVisibilityState) {
    visibility = state;
    document.dispatchEvent(new Event('visibilitychange'));
  }

  it('does nothing without a session', () => {
    vi.advanceTimersByTime(40 * MINUTE);

    expect(monitor.warning()).toBe(false);
    expect(logout).not.toHaveBeenCalled();
  });

  it('warns at 25 minutes without activity and logs out at 30', () => {
    signIn();

    vi.advanceTimersByTime(25 * MINUTE - 1);
    expect(monitor.warning()).toBe(false);
    vi.advanceTimersByTime(1);
    expect(monitor.warning()).toBe(true);
    expect(toasts.toasts()).toEqual([
      expect.objectContaining({ kind: 'warning', key: 'inactivity' }),
    ]);

    vi.advanceTimersByTime(5 * MINUTE);
    expect(logout).toHaveBeenCalledOnce();
    expect(monitor.warning()).toBe(false);
    expect(toasts.toasts()).toEqual([]);
  });

  it('starts over with user activity and tells the other tabs', () => {
    signIn();
    vi.advanceTimersByTime(26 * MINUTE);
    expect(monitor.warning()).toBe(true);

    document.dispatchEvent(new KeyboardEvent('keydown'));

    expect(monitor.warning()).toBe(false);
    expect(toasts.toasts()).toEqual([]);
    expect(sync.posted).toContainEqual({ type: 'activity', at: Date.now() });
    vi.advanceTimersByTime(29 * MINUTE);
    expect(logout).not.toHaveBeenCalled();
  });

  it('counts the activity of another tab', () => {
    signIn();
    vi.advanceTimersByTime(20 * MINUTE);

    sync.receive({ type: 'activity', at: Date.now() });
    vi.advanceTimersByTime(20 * MINUTE);

    expect(monitor.warning()).toBe(false);
    expect(logout).not.toHaveBeenCalled();
  });

  it('pauses while the tab is hidden and checks the elapsed time when it is shown again', () => {
    signIn();
    setVisibility('hidden');

    vi.advanceTimersByTime(31 * MINUTE);
    expect(logout).not.toHaveBeenCalled();

    setVisibility('visible');
    expect(logout).toHaveBeenCalledOnce();
  });

  it('stops when the session ends', () => {
    signIn();
    sync.receive({ type: 'logout' });
    TestBed.tick();

    vi.advanceTimersByTime(31 * MINUTE);
    expect(logout).not.toHaveBeenCalled();
  });
});
