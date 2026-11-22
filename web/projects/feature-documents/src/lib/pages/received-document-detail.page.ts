import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import {
  Api,
  DianEventListResponse,
  ReceivedDocumentResponse,
  getReceivedDocument,
} from '@impifact/api';
import { SessionStore, ToastStore } from '@impifact/core';
import {
  BadgeComponent,
  ButtonComponent,
  CardComponent,
  ErrorStateComponent,
  LoadingComponent,
  PageTitleComponent,
  UiBadgeTone,
} from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { EMPTY, Subject, catchError, combineLatest, startWith, switchMap, tap } from 'rxjs';
import { ColombiaTimePipe } from '../components/colombia-time.pipe';
import { CopCurrencyPipe } from '../components/cop-currency.pipe';
import { DianEventsComponent } from '../components/dian-events.component';
import {
  EventRequest,
  RadianEventDialogComponent,
} from '../components/radian-event-dialog.component';
import { RadianTimelineComponent } from '../components/radian-timeline.component';
import { DOCUMENTS_I18N_SCOPE, provideDocumentsI18n } from '../documents-i18n';
import { ISSUE_SCOPE } from '../documents.routes';
import {
  CLAIM,
  EventBlock,
  RECEIVED_EVENT_CODES,
  RadianStage,
  ReceivedEventCode,
  eventBlock,
  radianStage,
  registeredCodes,
} from '../models/radian-events';
import { DocumentActions } from '../state/document-actions';
import { RadianActions } from '../state/radian-actions';

const STAGE_TONES: Record<RadianStage, UiBadgeTone> = {
  none: 'neutral',
  acknowledged: 'info',
  received: 'info',
  accepted: 'success',
  tacitlyAccepted: 'success',
  claimed: 'danger',
};

const KNOWN_TYPES = ['1', '2', '3', '12'];
const KNOWN_ENVIRONMENTS = ['1', '2'];

/** An event action of the page: its code and why it is not available now (`null` when it is). */
interface EventAction {
  code: ReceivedEventCode;
  block: EventBlock | null;
}

/**
 * Received invoice (`/documentos/recibidos/:id`, RF-16): supplier, totals, dates and CUFE, the XML, the history of
 * its RADIAN events, what the DIAN reports and the four events of the buyer (030, 032, 033 and 031). Each action is
 * enabled by the order of the API (`radian-events.ts`); every send goes through the confirm dialog and the page reads
 * the invoice again afterwards, whatever the answer.
 */
@Component({
  selector: 'feat-received-document-detail-page',
  imports: [
    RouterLink,
    TranslocoPipe,
    BadgeComponent,
    ButtonComponent,
    CardComponent,
    ErrorStateComponent,
    LoadingComponent,
    PageTitleComponent,
    ColombiaTimePipe,
    CopCurrencyPipe,
    DianEventsComponent,
    RadianEventDialogComponent,
    RadianTimelineComponent,
  ],
  providers: [provideDocumentsI18n(), DocumentActions, RadianActions],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './received-document-detail.page.html',
  styleUrls: [
    './documents-page.css',
    './document-detail.page.css',
    './received-document-detail.page.css',
  ],
})
export class ReceivedDocumentDetailPage {
  private readonly api = inject(Api);
  private readonly actions = inject(DocumentActions);
  private readonly radian = inject(RadianActions);
  private readonly session = inject(SessionStore);
  private readonly toasts = inject(ToastStore);
  private readonly router = inject(Router);
  private readonly transloco = inject(TranslocoService);

  /** Id of the received invoice (route parameter). */
  readonly id = input.required<string>();

  protected readonly document = signal<ReceivedDocumentResponse | null>(null);
  protected readonly failed = signal(false);
  protected readonly busy = signal(false);
  protected readonly dian = signal<DianEventListResponse | null>(null);
  protected readonly consulting = signal(false);
  protected readonly dialogCode = signal<ReceivedEventCode | null>(null);
  protected readonly dialogOpen = signal(false);
  private readonly reload = new Subject<void>();

  protected readonly canWrite = computed(() => this.session.hasScope(ISSUE_SCOPE));
  protected readonly stage = computed(() => radianStage(this.document()?.events));
  protected readonly stageTone = computed(() => STAGE_TONES[this.stage()]);
  protected readonly typeKey = computed(() => {
    const type = String(this.document()?.type_document_id ?? '');
    return KNOWN_TYPES.includes(type) ? type : 'unknown';
  });
  protected readonly environmentKey = computed(() => {
    const environment = String(this.document()?.type_environment_id ?? '');
    return KNOWN_ENVIRONMENTS.includes(environment) ? environment : 'unknown';
  });

  /** The four events of the buyer, each with why it is not available now; what the DIAN lists counts too. */
  protected readonly eventActions = computed<EventAction[]>(() => {
    const registered = registeredCodes(
      this.document()?.events,
      this.dian()?.events.map((event) => event.dian_code) ?? [],
    );
    return RECEIVED_EVENT_CODES.map((code) => ({ code, block: eventBlock(code, registered) }));
  });
  /** The first available event other than the claim is the suggested next step. */
  protected readonly suggested = computed(
    () => this.eventActions().find((action) => !action.block && action.code !== CLAIM)?.code,
  );

  constructor() {
    combineLatest([toObservable(this.id), this.reload.pipe(startWith(undefined))])
      .pipe(
        tap(() => this.failed.set(false)),
        switchMap(([id]) =>
          this.api.invoke(getReceivedDocument, { id }).pipe(
            catchError(() => {
              this.failed.set(true);
              return EMPTY;
            }),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((doc) => this.document.set(doc));
  }

  protected retry(): void {
    this.reload.next();
  }

  protected blockText(block: EventBlock): string {
    return this.radian.blockText(block);
  }

  protected openEvent(code: ReceivedEventCode): void {
    this.dialogCode.set(code);
    this.dialogOpen.set(true);
  }

  /** Confirms and sends the event; then reads the invoice again (and, after a 404, goes back to the list). */
  protected async send(request: EventRequest): Promise<void> {
    const doc = this.document();
    if (!doc) {
      return;
    }
    this.busy.set(true);
    try {
      const outcome = await this.radian.send('received', doc.id, doc.number, request);
      if (outcome === 'cancelled') {
        return;
      }
      this.dialogOpen.set(false);
      if (outcome === 'notFound') {
        await this.router.navigate(['/documentos/recibidos']);
        return;
      }
      this.reload.next();
      // What the DIAN listed before the send is out of date.
      this.dian.set(null);
    } finally {
      this.busy.set(false);
    }
  }

  protected async consultDian(): Promise<void> {
    const doc = this.document();
    if (!doc) {
      return;
    }
    this.consulting.set(true);
    try {
      const answer = await this.radian.dianEvents('received', doc.id);
      if (answer) {
        this.dian.set(answer);
      }
    } finally {
      this.consulting.set(false);
    }
  }

  protected downloadXml(): Promise<boolean> {
    const doc = this.document();
    return doc ? this.actions.downloadReceivedXml(doc) : Promise.resolve(false);
  }

  protected async copyCufe(): Promise<void> {
    const cufe = this.document()?.cufe;
    if (!cufe) {
      return;
    }
    try {
      await navigator.clipboard.writeText(cufe);
      this.toasts.show({ kind: 'success', text: this.text('received.detail.copied') });
    } catch {
      this.toasts.show({ kind: 'warning', text: this.text('received.detail.copyFailed') });
    }
  }

  private text(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate(`${DOCUMENTS_I18N_SCOPE}.${key}`, params);
  }
}
