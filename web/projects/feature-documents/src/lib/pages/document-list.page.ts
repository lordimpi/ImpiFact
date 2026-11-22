import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { DocumentSummaryResponse } from '@impifact/api';
import { SessionStore } from '@impifact/core';
import {
  ButtonLinkDirective,
  CardActionsDirective,
  CardComponent,
  MenuButtonComponent,
  PageTitleComponent,
  TableCellDirective,
  TableComponent,
  UiMenuItem,
  UiTableColumn,
  UiTableLabels,
} from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { map } from 'rxjs';
import { CopCurrencyPipe } from '../components/cop-currency.pipe';
import { DocumentFiltersComponent } from '../components/document-filters.component';
import { StatusBadgeComponent } from '../components/status-badge.component';
import { DOCUMENTS_I18N_SCOPE, provideDocumentsI18n } from '../documents-i18n';
import { ISSUE_SCOPE } from '../documents.routes';
import { documentKindOf, filesAvailable, kindKey } from '../models/document-status';
import {
  DocumentActions,
  DocumentFileKind,
  EXPORT_FORMATS,
  ExportFormat,
} from '../state/document-actions';
import { DocumentsListStore } from '../state/documents-list.store';
import { DocumentFilters, filtersFromParams, filtersToQueryParams } from '../state/list-filters';

/**
 * Lista de documentos (`/documentos`, sección 8.3): paginación por cursor en el servidor, filtros guardados en la
 * URL (se pueden compartir y sobreviven a una recarga), exportación (menú «Exportar»: Excel o CSV) y acciones por fila.
 */
@Component({
  selector: 'feat-document-list-page',
  imports: [
    ButtonLinkDirective,
    RouterLink,
    TranslocoPipe,
    CardComponent,
    CardActionsDirective,
    MenuButtonComponent,
    PageTitleComponent,
    TableComponent,
    TableCellDirective,
    CopCurrencyPipe,
    DocumentFiltersComponent,
    StatusBadgeComponent,
  ],
  providers: [provideDocumentsI18n(), DocumentsListStore, DocumentActions],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './document-list.page.html',
  styleUrls: ['./documents-page.css', './document-list.page.css'],
})
export class DocumentListPage {
  protected readonly store = inject(DocumentsListStore);
  private readonly actions = inject(DocumentActions);
  private readonly session = inject(SessionStore);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly transloco = inject(TranslocoService);

  /** Filtros de la URL: la única fuente de verdad de la consulta. */
  protected readonly filters = toSignal(
    this.route.queryParamMap.pipe(map((params) => filtersFromParams(params))),
    { initialValue: {} as DocumentFilters },
  );

  protected readonly canIssue = computed(() => this.session.hasScope(ISSUE_SCOPE));
  protected readonly exporting = signal(false);
  /** Options of the «Exportar» menu, one per export format. */
  protected readonly exportFormats = computed<UiMenuItem[]>(() =>
    EXPORT_FORMATS.map((format) => ({
      id: format,
      label: this.text(`list.exportFormats.${format}`),
    })),
  );
  /** Fila con una acción en curso (para deshabilitar sus botones). */
  protected readonly busyRow = signal<string | null>(null);

  protected readonly kindKey = kindKey;
  protected readonly documentKindOf = documentKindOf;
  protected readonly filesAvailable = filesAvailable;
  protected readonly trackById = (_: number, row: DocumentSummaryResponse) => row.id;

  // Los textos ya están cargados: el *resolver* de la ruta los pide antes de abrir la página.
  protected readonly columns = computed<UiTableColumn[]>(() => [
    { key: 'full_number', header: this.text('list.columns.number') },
    { key: 'kind', header: this.text('list.columns.kind') },
    { key: 'issue_date', header: this.text('list.columns.issueDate') },
    { key: 'customer', header: this.text('list.columns.customer') },
    { key: 'total', header: this.text('list.columns.total'), align: 'end' },
    { key: 'status', header: this.text('list.columns.status') },
    { key: 'actions', header: this.text('list.columns.actions') },
  ]);

  protected readonly labels = computed<UiTableLabels>(() => ({
    loading: this.text('list.loading'),
    emptyTitle: this.text('list.emptyTitle'),
    emptyDescription: this.text('list.emptyDescription'),
    errorTitle: this.text('list.errorTitle'),
    errorDescription: this.text('list.errorDescription'),
    retry: this.transloco.translate('ui.retry'),
    paginator: {
      navigation: this.transloco.translate('ui.paginator.navigation'),
      previous: this.transloco.translate('ui.paginator.previous'),
      next: this.transloco.translate('ui.paginator.next'),
      page: this.transloco.translate('ui.paginator.page'),
    },
  }));

  constructor() {
    // Cada cambio de la URL (filtros aplicados, atrás, enlace compartido) es una consulta nueva desde la página 1.
    this.route.queryParamMap
      .pipe(
        map((params) => filtersFromParams(params)),
        takeUntilDestroyed(),
      )
      .subscribe((filters) => this.store.setFilters(filters));
  }

  /** Lleva los filtros a la URL; el cambio de la URL dispara la consulta. */
  protected applyFilters(filters: DocumentFilters): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: filtersToQueryParams(filters),
      queryParamsHandling: 'merge',
    });
  }

  protected async exportDocuments(format: string): Promise<void> {
    this.exporting.set(true);
    try {
      await this.actions.exportDocuments(format as ExportFormat, this.filters());
    } finally {
      this.exporting.set(false);
    }
  }

  protected download(row: DocumentSummaryResponse, kind: DocumentFileKind): Promise<void> {
    return this.run(row, () => this.actions.download(row, kind));
  }

  protected resendEmail(row: DocumentSummaryResponse): Promise<void> {
    return this.run(row, (id) => this.actions.resendEmail(id));
  }

  protected regeneratePdf(row: DocumentSummaryResponse): Promise<void> {
    return this.run(row, (id) => this.actions.regeneratePdf(id));
  }

  private async run(
    row: DocumentSummaryResponse,
    action: (id: string) => Promise<boolean>,
  ): Promise<void> {
    if (!row.id) {
      return;
    }
    this.busyRow.set(row.id);
    try {
      await action(row.id);
    } finally {
      this.busyRow.set(null);
    }
  }

  private text(key: string): string {
    return this.transloco.translate(`${DOCUMENTS_I18N_SCOPE}.${key}`);
  }
}
