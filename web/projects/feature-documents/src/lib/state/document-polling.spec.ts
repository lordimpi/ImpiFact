import { BehaviorSubject, Observable, Subscription, of } from 'rxjs';
import { filesAvailable, isPendingStatus } from '../models/document-status';
import { pollingDelay, pollWhilePending } from './document-polling';

interface Doc {
  status: string;
}

describe('document status rules', () => {
  it('keeps polling only while the document is queued, sent or unconfirmed', () => {
    expect(['Queued', 'Sent', 'Unconfirmed'].map(isPendingStatus)).toEqual([true, true, true]);
    expect(
      ['Accepted', 'AcceptedWithWarnings', 'Rejected', 'Failed', 'Received', undefined].map(
        isPendingStatus,
      ),
    ).toEqual([false, false, false, false, false, false]);
  });

  it('offers the accepted-only files after the DIAN accepts the document', () => {
    expect(filesAvailable('Accepted')).toBe(true);
    expect(filesAvailable('AcceptedWithWarnings')).toBe(true);
    expect(filesAvailable('Sent')).toBe(false);
    expect(filesAvailable('Rejected')).toBe(false);
  });

  it('backs off 2 s, 4 s, 8 s, 16 s and then stays at 30 s', () => {
    expect([0, 1, 2, 3, 4, 5, 9].map(pollingDelay)).toEqual([
      2_000, 4_000, 8_000, 16_000, 30_000, 30_000, 30_000,
    ]);
  });
});

describe('pollWhilePending', () => {
  let subscription: Subscription | undefined;

  beforeEach(() => vi.useFakeTimers());
  afterEach(() => {
    subscription?.unsubscribe();
    vi.useRealTimers();
  });

  function start(statuses: string[], visible: Observable<boolean> = of(true)) {
    const seen: string[] = [];
    let calls = 0;
    const fetch = () => of<Doc>({ status: statuses[Math.min(calls++, statuses.length - 1)]! });
    subscription = pollWhilePending(fetch, (doc) => isPendingStatus(doc.status), visible).subscribe(
      (doc) => seen.push(doc.status),
    );
    return { seen, calls: () => calls };
  }

  it('fetches again with the backoff while pending and stops on a final status', () => {
    const run = start(['Queued', 'Sent', 'Accepted', 'Accepted']);
    expect(run.seen).toEqual(['Queued']);

    vi.advanceTimersByTime(1_999);
    expect(run.calls()).toBe(1);
    vi.advanceTimersByTime(1);
    expect(run.seen).toEqual(['Queued', 'Sent']);

    vi.advanceTimersByTime(3_999);
    expect(run.calls()).toBe(2);
    vi.advanceTimersByTime(1);
    expect(run.seen).toEqual(['Queued', 'Sent', 'Accepted']);

    vi.advanceTimersByTime(120_000);
    expect(run.calls()).toBe(3);
  });

  it('does not fetch while the tab is hidden and resumes when it is visible again', () => {
    const visible = new BehaviorSubject(true);
    const run = start(['Queued', 'Queued', 'Accepted'], visible);

    visible.next(false);
    vi.advanceTimersByTime(60_000);
    expect(run.calls()).toBe(1);

    visible.next(true);
    expect(run.calls()).toBe(2);
    vi.advanceTimersByTime(4_000);
    expect(run.seen).toEqual(['Queued', 'Queued', 'Accepted']);
  });

  it('stops fetching when the subscriber leaves', () => {
    const run = start(['Queued']);
    subscription?.unsubscribe();

    vi.advanceTimersByTime(120_000);
    expect(run.calls()).toBe(1);
  });
});
