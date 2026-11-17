import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { ToastStore } from '../notifications/toast.store';
import { SessionStore } from '../session/session.store';
import { FakeSessionSync, panelTokens, provideFakeSessionSync } from '../testing/fakes';
import { InactivityMonitor } from './inactivity-monitor';

const MINUTE = 60_000;

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
    expect(monitor.minutesLeft()).toBe(5);
    // El aviso lo muestra el diálogo del shell, no un toast.
    expect(toasts.toasts()).toEqual([]);

    vi.advanceTimersByTime(5 * MINUTE);
    expect(logout).toHaveBeenCalledExactlyOnceWith({ reason: 'inactivity' });
    expect(monitor.warning()).toBe(false);
  });

  it('starts over with user activity and tells the other tabs', () => {
    signIn();
    vi.advanceTimersByTime(20 * MINUTE);

    document.dispatchEvent(new KeyboardEvent('keydown'));

    expect(sync.posted).toContainEqual({ type: 'activity', at: Date.now() });
    vi.advanceTimersByTime(24 * MINUTE);
    expect(monitor.warning()).toBe(false);
    expect(logout).not.toHaveBeenCalled();
  });

  it('keeps the warning up until the user chooses to stay connected', () => {
    signIn();
    vi.advanceTimersByTime(26 * MINUTE);
    expect(monitor.warning()).toBe(true);

    // Con el aviso abierto, la actividad suelta no lo cierra: así no desaparece bajo el puntero de quien pulsa
    // «Cerrar sesión».
    document.dispatchEvent(new KeyboardEvent('keydown'));
    expect(monitor.warning()).toBe(true);

    monitor.stayConnected();

    expect(monitor.warning()).toBe(false);
    expect(sync.posted).toContainEqual({ type: 'activity', at: Date.now() });
    vi.advanceTimersByTime(29 * MINUTE);
    expect(logout).not.toHaveBeenCalled();
  });

  it('closes the warning when another tab reports activity', () => {
    signIn();
    vi.advanceTimersByTime(26 * MINUTE);
    expect(monitor.warning()).toBe(true);

    sync.receive({ type: 'activity', at: Date.now() });

    expect(monitor.warning()).toBe(false);
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
