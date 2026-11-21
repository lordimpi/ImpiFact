import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { Api, DocumentDetailResponse, getDocument } from '@impifact/api';
import { SessionStore } from '@impifact/core';
import {
  ButtonComponent,
  ButtonLinkDirective,
  CardComponent,
  CardFooterDirective,
  ErrorStateComponent,
  LoadingComponent,
  PageTitleComponent,
  TableCellDirective,
  TableComponent,
  UiTableColumn,
  UiTableLabels,
} from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { EMPTY, Subject, catchError, combineLatest, startWith, switchMap, tap } from 'rxjs';
import { CopCurrencyPipe } from '../components/cop-currency.pipe';
import { StatusBadgeComponent } from '../components/status-badge.component';
import { TotalsPanelComponent } from '../components/totals-panel.component';
import { DOCUMENTS_I18N_SCOPE, provideDocumentsI18n } from '../documents-i18n';
import { ISSUE_SCOPE } from '../documents.routes';
import { filesAvailable, isPendingStatus, kindKey } from '../models/document-status';
import { DocumentActions, DocumentFileKind } from '../state/document-actions';
import { pageVisibility, pollWhilePending } from '../state/document-polling';

type DocumentLine = NonNullable<DocumentDetailResponse['lines']>[number];

/**
 * Detalle de un documento (`/documentos/:id`): encabezado, cliente, líneas, totales, estado en la DIAN con su
 * mensaje, archivos y acciones. Mientras la DIAN no da una respuesta final, el estado se consulta con retroceso
 * (sección 9), en pausa con la pestaña oculta y hasta salir de la página.
 */
@Component({
  selector: 'feat-document-detail-page',
  imports: [
    ButtonLinkDirective,
    RouterLink,
    TranslocoPipe,
    ButtonComponent,
    CardComponent,
    CardFooterDirective,
    ErrorStateComponent,
    LoadingComponent,
    PageTitleComponent,
    TableComponent,
    TableCellDirective,
    CopCurrencyPipe,
    StatusBadgeComponent,
    TotalsPanelComponent,
  ],
  providers: [provideDocumentsI18n(), DocumentActions],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './document-detail.page.html',
  styleUrls: ['./documents-page.css', './document-detail.page.css'],
})
export class DocumentDetailPage {
  private readonly api = inject(Api);
  private readonly actions = inject(DocumentActions);
  private readonly session = inject(SessionStore);
  private readonly transloco = inject(TranslocoService);

  /** Id del documento (parámetro de la ruta). */
  readonly id = input.required<string>();

  protected readonly document = signal<DocumentDetailResponse | null>(null);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly busy = signal(false);

  private readonly visibility = pageVisibility();
  protected readonly visible = toSignal(this.visibility, { initialValue: true });
  private readonly reload = new Subject<void>();

  protected readonly pending = computed(() => isPendingStatus(this.document()?.status));
  protected readonly accepted = computed(() => filesAvailable(this.document()?.status));
  protected readonly canWrite = computed(() => this.session.hasScope(ISSUE_SCOPE));
  /** El XML firmado existe desde que el documento quedó en cola. */
  protected readonly signed = computed(() => {
    const status = this.document()?.status;
    return !!status && status !== 'Received' && status !== 'Draft';
  });
  protected readonly kindKey = kindKey;

  protected readonly acceptedFiles: DocumentFileKind[] = ['pdf', 'attached-document', 'zip'];

  protected readonly lineColumns = computed<UiTableColumn[]>(() => [
    { key: 'line_number', header: this.text('detail.line.number') },
    { key: 'code', header: this.text('detail.line.code') },
    { key: 'description', header: this.text('detail.line.description') },
    { key: 'invoiced_quantity', header: this.text('detail.line.quantity'), align: 'end' },
    { key: 'price_amount', header: this.text('detail.line.unitPrice'), align: 'end' },
    { key: 'line_extension_amount', header: this.text('detail.line.amount'), align: 'end' },
  ]);

  protected readonly lineLabels = computed<UiTableLabels>(() => ({
    loading: this.text('detail.loading'),
    emptyTitle: this.text('detail.lines'),
    errorTitle: this.text('detail.errorTitle'),
    retry: this.transloco.translate('ui.retry'),
    paginator: { navigation: '', previous: '', next: '', page: '' },
  }));

  protected readonly trackLine = (_: number, line: DocumentLine) => line.line_number;

  constructor() {
    combineLatest([toObservable(this.id), this.reload.pipe(startWith(undefined))])
      .pipe(
        tap(() => {
          this.loading.set(true);
          this.failed.set(false);
        }),
        switchMap(([id]) =>
          pollWhilePending(
            () => this.api.invoke(getDocument, { id }),
            (doc) => isPendingStatus(doc.status),
            this.visibility,
          ).pipe(
            catchError(() => {
              this.failed.set(true);
              this.loading.set(false);
              return EMPTY;
            }),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((doc) => {
        this.document.set(doc);
        this.loading.set(false);
      });
  }

  protected retry(): void {
    this.reload.next();
  }

  protected download(kind: DocumentFileKind): Promise<void> {
    return this.run(() => this.actions.download(this.document() ?? {}, kind));
  }

  protected resendEmail(): Promise<void> {
    return this.run(() => this.actions.resendEmail(this.id()));
  }

  protected regeneratePdf(): Promise<void> {
    return this.run(() => this.actions.regeneratePdf(this.id()));
  }

  protected regenerateAttached(): Promise<void> {
    return this.run(() => this.actions.regenerateAttached(this.id()));
  }

  private async run(action: () => Promise<boolean>): Promise<void> {
    this.busy.set(true);
    try {
      await action();
    } finally {
      this.busy.set(false);
    }
  }

  private text(key: string): string {
    return this.transloco.translate(`${DOCUMENTS_I18N_SCOPE}.${key}`);
  }
}
