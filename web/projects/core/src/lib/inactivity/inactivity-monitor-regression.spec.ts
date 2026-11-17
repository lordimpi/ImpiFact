import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { SessionStore } from '../session/session.store';
import { FakeSessionSync, panelTokens, provideFakeSessionSync } from '../testing/fakes';
import { InactivityMonitor } from './inactivity-monitor';

const MINUTE = 60_000;

describe('InactivityMonitor deadline regressions', () => {
  let sync: FakeSessionSync;
  let monitor: InactivityMonitor;
  let logout: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    vi.useFakeTimers();
    vi.spyOn(document, 'visibilityState', 'get').mockImplementation(() => 'visible');
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
    const session = TestBed.inject(SessionStore);
    logout = vi.fn(async () => undefined);
    session.logout = logout as typeof session.logout;
    monitor = TestBed.inject(InactivityMonitor);
    sync.receive({ type: 'session', tokens: panelTokens(), email: null });
    TestBed.tick();
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('logs out at the deadline while the dialog is open even with stray activity', () => {
    vi.advanceTimersByTime(25 * MINUTE);
    expect(monitor.warning()).toBe(true);
    for (let i = 0; i < 4; i++) {
      vi.advanceTimersByTime(MINUTE);
      document.dispatchEvent(new Event('pointerdown'));
      document.dispatchEvent(new Event('keydown'));
    }
    vi.advanceTimersByTime(MINUTE);
    expect(logout).toHaveBeenCalledExactlyOnceWith({ reason: 'inactivity' });
  });

  it('stayConnected restarts the full 30 minutes and tells the other tabs', () => {
    vi.advanceTimersByTime(26 * MINUTE);
    monitor.stayConnected();
    expect(monitor.warning()).toBe(false);
    expect(sync.posted.some((m) => m.type === 'activity')).toBe(true);
    vi.advanceTimersByTime(30 * MINUTE - 1);
    expect(logout).not.toHaveBeenCalled();
    vi.advanceTimersByTime(1);
    expect(logout).toHaveBeenCalledOnce();
  });
});
