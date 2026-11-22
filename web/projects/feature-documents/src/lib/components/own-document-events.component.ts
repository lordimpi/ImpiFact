import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import {
  Api,
  DianEventListResponse,
  DocumentEventResponse,
  listDocumentEvents,
} from '@impifact/api';
import { ButtonComponent } from '@impifact/ui';
import { TranslocoPipe } from '@jsverse/transloco';
import { EMPTY, Subject, catchError, combineLatest, startWith, switchMap, tap } from 'rxjs';
import { EventBlock, TACIT_ACCEPTANCE, tacitAcceptanceBlock } from '../models/radian-events';
import { RadianActions } from '../state/radian-actions';
import { DianEventsComponent } from './dian-events.component';
import { RadianTimelineComponent } from './radian-timeline.component';

/**
 * «Eventos RADIAN» of an accepted own sale invoice: the events the company sent (the tacit acceptance, 034), what the
 * DIAN reports (also the 030 to 033 of the buyer) and «Aceptación tácita», confirmed first and then sent. After a send
 * the events are read again; the DIAN answer is asked again only when the person wants.
 */
@Component({
  selector: 'feat-own-document-events',
  imports: [TranslocoPipe, ButtonComponent, DianEventsComponent, RadianTimelineComponent],
  providers: [RadianActions],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="own-events">
      <p class="own-events__help">{{ 'documents.radian.ownDescription' | transloco }}</p>
      @if (failed()) {
        <p class="own-events__failed" role="status">
          {{ 'documents.radian.loadFailed' | transloco }}
        </p>
      } @else {
        <feat-radian-timeline [events]="events()" emptyKey="documents.radian.noOwnEvents" />
      }
      @if (canSend()) {
        <div class="own-events__action">
          <ui-button
            variant="secondary"
            [label]="'documents.radian.actions.034' | transloco"
            [disabled]="!!block() || busy()"
            [loading]="busy()"
            (pressed)="sendTacitAcceptance()"
          />
          @if (block(); as block) {
            <p class="own-events__why">{{ blockText(block) }}</p>
          }
        </div>
      }
      <h3 class="own-events__title">{{ 'documents.radian.dian.title' | transloco }}</h3>
      <feat-dian-events [result]="dian()" [busy]="consulting()" (consult)="consultDian()" />
    </div>
  `,
  styles: `
    .own-events {
      display: flex;
      flex-direction: column;
      gap: var(--if-space-3);
    }
    .own-events__help,
    .own-events__why,
    .own-events__failed {
      margin: 0;
      font-size: var(--if-font-size-sm);
      color: var(--if-color-text-muted);
    }
    .own-events__action {
      display: flex;
      flex-wrap: wrap;
      gap: var(--if-space-2) var(--if-space-3);
      align-items: center;
    }
    .own-events__title {
      margin: var(--if-space-2) 0 0;
      font-size: var(--if-font-size-md);
      font-weight: var(--if-font-weight-bold);
    }
  `,
})
export class OwnDocumentEventsComponent {
  private readonly api = inject(Api);
  private readonly radian = inject(RadianActions);

  readonly documentId = input.required<string>();
  /** Number of the invoice, for the confirm question. */
  readonly number = input('');
  /** Whether the session may send the 034 (`documents:write`). */
  readonly canSend = input(false, { transform: booleanAttribute });

  protected readonly events = signal<readonly DocumentEventResponse[]>([]);
  protected readonly failed = signal(false);
  protected readonly busy = signal(false);
  protected readonly dian = signal<DianEventListResponse | null>(null);
  protected readonly consulting = signal(false);
  private readonly reload = new Subject<void>();

  /** Why the 034 is not available; before asking the DIAN only a 034 already sent blocks it. */
  protected readonly block = computed(() =>
    tacitAcceptanceBlock(
      this.events(),
      this.dian()?.events.map((event) => event.dian_code) ?? null,
    ),
  );

  constructor() {
    combineLatest([toObservable(this.documentId), this.reload.pipe(startWith(undefined))])
      .pipe(
        tap(() => this.failed.set(false)),
        switchMap(([id]) =>
          this.api.invoke(listDocumentEvents, { id }).pipe(
            catchError(() => {
              this.failed.set(true);
              return EMPTY;
            }),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((list) => this.events.set(list.data));
  }

  protected blockText(block: EventBlock): string {
    return this.radian.blockText(block);
  }

  protected async sendTacitAcceptance(): Promise<void> {
    this.busy.set(true);
    try {
      const outcome = await this.radian.send('own', this.documentId(), this.number(), {
        event_code: TACIT_ACCEPTANCE,
      });
      if (outcome !== 'cancelled') {
        this.reload.next();
        this.dian.set(null);
      }
    } finally {
      this.busy.set(false);
    }
  }

  protected async consultDian(): Promise<void> {
    this.consulting.set(true);
    try {
      const answer = await this.radian.dianEvents('own', this.documentId());
      if (answer) {
        this.dian.set(answer);
      }
    } finally {
      this.consulting.set(false);
    }
  }
}
