import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ReceivedDocumentResponse } from '@impifact/api';
import { SessionStore, ToastStore } from '@impifact/core';
import {
  BadgeComponent,
  ButtonComponent,
  CardActionsDirective,
  CardComponent,
  PageTitleComponent,
  TableCellDirective,
  TableComponent,
  UiBadgeTone,
  UiTableColumn,
  UiTableLabels,
} from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { map } from 'rxjs';
import { CopCurrencyPipe } from '../components/cop-currency.pipe';
import { CufeLoadDialogComponent } from '../components/cufe-load-dialog.component';
import { ReceivedFiltersComponent } from '../components/received-filters.component';
import { DOCUMENTS_I18N_SCOPE, provideDocumentsI18n } from '../documents-i18n';
import { ISSUE_SCOPE } from '../documents.routes';
import { RadianStage, radianStage } from '../models/radian-events';
import { DocumentActions } from '../state/document-actions';
import { RadianActions } from '../state/radian-actions';
import { ReceivedDocumentsStore } from '../state/received-documents.store';
import {
  ReceivedFilters,
  receivedFiltersFromParams,
  receivedFiltersToQueryParams,
} from '../state/received-filters';

const STAGE_TONES: Record<RadianStage, UiBadgeTone> = {
  none: 'neutral',
  acknowledged: 'info',
  received: 'info',
  accepted: 'success',
  tacitlyAccepted: 'success',
  claimed: 'danger',
};

/**
 * Received documents (`/documentos/recibidos`, RF-16): the invoices other sellers addressed to the company, paged on the
 * server with page numbers, filters in the URL, the furthest RADIAN step of each one and «Cargar por CUFE», which
 * downloads one from the DIAN and opens it.
 */
@Component({
  selector: 'feat-received-document-list-page',
  imports: [
    RouterLink,
    TranslocoPipe,
    BadgeComponent,
    ButtonComponent,
    CardComponent,
    CardActionsDirective,
    PageTitleComponent,
    TableComponent,
    TableCellDirective,
    CopCurrencyPipe,
    CufeLoadDialogComponent,
    ReceivedFiltersComponent,
  ],
  providers: [provideDocumentsI18n(), ReceivedDocumentsStore, DocumentActions, RadianActions],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './received-document-list.page.html',
  styleUrls: ['./documents-page.css', './document-list.page.css'],
})
export class ReceivedDocumentListPage {
  protected readonly store = inject(ReceivedDocumentsStore);
  private readonly actions = inject(DocumentActions);
  private readonly radian = inject(RadianActions);
  private readonly session = inject(SessionStore);
  private readonly toasts = inject(ToastStore);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly transloco = inject(TranslocoService);

  protected readonly filters = toSignal(
    this.route.queryParamMap.pipe(map((params) => receivedFiltersFromParams(params))),
    { initialValue: {} as ReceivedFilters },
  );

  /** Loading an invoice and sending events need `documents:write`. */
  protected readonly canWrite = computed(() => this.session.hasScope(ISSUE_SCOPE));
  protected readonly loadOpen = signal(false);
  protected readonly loading = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly busyRow = signal<string | null>(null);

  protected readonly loadMessages = computed(() => ({
    required: this.text('received.load.required'),
    format: this.text('received.load.format'),
  }));

  protected readonly trackById = (_: number, row: ReceivedDocumentResponse) => row.id;
  protected readonly stageOf = (row: ReceivedDocumentResponse) => radianStage(row.events);
  protected readonly stageTone = (row: ReceivedDocumentResponse) => STAGE_TONES[this.stageOf(row)];

  protected readonly columns = computed<UiTableColumn[]>(() => [
    { key: 'number', header: this.text('received.list.columns.number') },
    { key: 'supplier', header: this.text('received.list.columns.supplier') },
    { key: 'issue_date', header: this.text('received.list.columns.issueDate') },
    { key: 'payable_amount', header: this.text('received.list.columns.total'), align: 'end' },
    { key: 'stage', header: this.text('received.list.columns.stage') },
    { key: 'actions', header: this.text('received.list.columns.actions') },
  ]);

  protected readonly labels = computed<UiTableLabels>(() => ({
    loading: this.text('received.list.loading'),
    emptyTitle: this.text('received.list.emptyTitle'),
    emptyDescription: this.text('received.list.emptyDescription'),
    errorTitle: this.text('received.list.errorTitle'),
    errorDescription: this.text('received.list.errorDescription'),
    retry: this.transloco.translate('ui.retry'),
    paginator: {
      navigation: this.transloco.translate('ui.paginator.navigation'),
      previous: this.transloco.translate('ui.paginator.previous'),
      next: this.transloco.translate('ui.paginator.next'),
      page: this.transloco.translate('ui.paginator.page'),
    },
  }));

  constructor() {
    // Each change of the URL (filters applied, back, a shared link) is a new query from page 1.
    this.route.queryParamMap
      .pipe(
        map((params) => receivedFiltersFromParams(params)),
        takeUntilDestroyed(),
      )
      .subscribe((filters) => this.store.setFilters(filters));
  }

  protected applyFilters(filters: ReceivedFilters): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: receivedFiltersToQueryParams(filters),
      queryParamsHandling: 'merge',
    });
  }

  protected openLoad(): void {
    this.loadError.set(null);
    this.loadOpen.set(true);
  }

  /** Loads the invoice by its CUFE and opens it; a new one (201) and one already loaded (200) say so in a toast. */
  protected async load(cufe: string): Promise<void> {
    this.loading.set(true);
    this.loadError.set(null);
    try {
      const result = await this.radian.loadByCufe(cufe);
      if (!result.ok) {
        this.loadError.set(result.message);
        return;
      }
      const number = result.document.number;
      this.toasts.show(
        result.created
          ? { kind: 'success', text: this.text('received.load.loaded', { number }) }
          : { kind: 'info', text: this.text('received.load.alreadyLoaded', { number }) },
      );
      this.loadOpen.set(false);
      await this.router.navigate(['/documentos/recibidos', result.document.id]);
    } finally {
      this.loading.set(false);
    }
  }

  protected async downloadXml(row: ReceivedDocumentResponse): Promise<void> {
    this.busyRow.set(row.id);
    try {
      await this.actions.downloadReceivedXml(row);
    } finally {
      this.busyRow.set(null);
    }
  }

  private text(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate(`${DOCUMENTS_I18N_SCOPE}.${key}`, params);
  }
}
