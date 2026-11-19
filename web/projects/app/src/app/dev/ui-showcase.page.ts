import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import {
  ButtonComponent,
  DialogComponent,
  EmptyStateComponent,
  ErrorStateComponent,
  FieldComponent,
  InputDirective,
  LoadingComponent,
  PageTitleComponent,
  PaginatorComponent,
  TableCellDirective,
  TableComponent,
  UiFilters,
  UiPageChange,
  UiPagination,
  UiPaginatorLabels,
  UiSort,
  UiTableColumn,
  UiTableLabels,
} from '@impifact/ui';

type Theme = 'system' | 'light' | 'dark';

interface DocumentRow {
  id: number;
  number: string;
  customer: string;
  status: string;
  total: string;
}

// Datos de ejemplo de la vitrina (no son textos de interfaz): simulan una página que devolvió el Api.
const SAMPLE_ROWS: DocumentRow[] = [
  {
    id: 3,
    number: 'SETP990000003',
    customer: 'Zapatos del Valle SAS',
    status: 'Rechazado',
    total: '$ 1.190.000',
  },
  {
    id: 1,
    number: 'SETP990000001',
    customer: 'Alfa Ingeniería Ltda',
    status: 'Aceptado',
    total: '$ 238.000',
  },
  {
    id: 2,
    number: 'SETP990000002',
    customer: 'Mercados Andinos SA',
    status: 'En cola',
    total: '$ 59.500',
  },
];

/**
 * Vitrina de la librería ui, solo para desarrollo (la ruta no existe en el build de producción).
 * Muestra cada componente en cada estado y registra los eventos que emiten. La usa la prueba E2E de
 * accesibilidad con axe (e2e/ui-showcase.spec.ts).
 */
@Component({
  selector: 'app-ui-showcase-page',
  imports: [
    TranslocoPipe,
    PageTitleComponent,
    ButtonComponent,
    FieldComponent,
    InputDirective,
    EmptyStateComponent,
    LoadingComponent,
    ErrorStateComponent,
    TableComponent,
    TableCellDirective,
    PaginatorComponent,
    DialogComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './ui-showcase.page.html',
  styleUrl: './ui-showcase.page.scss',
})
export class UiShowcasePage {
  private readonly document = inject(DOCUMENT);
  private readonly transloco = inject(TranslocoService);
  // Cambia cuando terminan de cargar los textos, para recalcular las etiquetas traducidas.
  private readonly translation = toSignal(this.transloco.selectTranslation());

  protected readonly rows = SAMPLE_ROWS;
  protected readonly themes: readonly Theme[] = ['system', 'light', 'dark'];
  protected readonly theme = signal<Theme>(this.currentTheme());
  protected readonly lastEvent = signal<string | null>(null);
  protected readonly sort = signal<UiSort | null>(null);
  protected readonly filters = signal<UiFilters>({});
  protected readonly pagination = signal<UiPagination>({
    kind: 'pages',
    page: 1,
    pageSize: 3,
    total: 30,
  });
  protected readonly cursorPagination: UiPagination = {
    kind: 'cursor',
    previousCursor: null,
    nextCursor: 'eyJpZCI6M30',
  };
  protected readonly dialogOpen = signal(false);

  protected readonly columns = computed<UiTableColumn[]>(() => {
    const t = this.translator();
    return [
      { key: 'number', header: t('devUi.table.number'), sortable: true },
      {
        key: 'customer',
        header: t('devUi.table.customer'),
        filterable: true,
        filterLabel: t('devUi.table.filterCustomer'),
      },
      {
        key: 'status',
        header: t('devUi.table.status'),
        sortable: true,
        filterable: true,
        filterLabel: t('devUi.table.filterStatus'),
      },
      { key: 'total', header: t('devUi.table.total'), align: 'end' },
    ];
  });

  protected readonly paginatorLabels = computed<UiPaginatorLabels>(() => {
    const t = this.translator();
    return {
      navigation: t('ui.paginator.navigation'),
      previous: t('ui.paginator.previous'),
      next: t('ui.paginator.next'),
      page: t('ui.paginator.page'),
    };
  });

  protected readonly cursorPaginatorLabels = computed<UiPaginatorLabels>(() => ({
    ...this.paginatorLabels(),
    navigation: this.translator()('devUi.paginator.cursorNav'),
  }));

  protected readonly tableLabels = computed<UiTableLabels>(() => {
    const t = this.translator();
    return {
      loading: t('devUi.table.loading'),
      emptyTitle: t('devUi.table.emptyTitle'),
      emptyDescription: t('devUi.table.emptyDescription'),
      errorTitle: t('devUi.table.errorTitle'),
      errorDescription: t('devUi.table.errorDescription'),
      retry: t('ui.retry'),
      paginator: this.paginatorLabels(),
    };
  });

  protected setTheme(theme: Theme): void {
    this.theme.set(theme);
    const root = this.document.documentElement;
    if (theme === 'system') root.removeAttribute('data-theme');
    else root.setAttribute('data-theme', theme);
  }

  protected onSort(sort: UiSort): void {
    this.sort.set(sort);
    this.log('sortChange', sort);
  }

  protected onFilter(filters: UiFilters): void {
    this.filters.set(filters);
    this.log('filterChange', filters);
  }

  protected onPage(change: UiPageChange): void {
    if (change.kind === 'pages') {
      this.pagination.update((current) => ({ ...current, page: change.page }));
    }
    this.log('pageChange', change);
  }

  protected log(name: string, payload?: unknown): void {
    this.lastEvent.set(payload === undefined ? name : `${name} ${JSON.stringify(payload)}`);
  }

  private translator(): (key: string) => string {
    this.translation();
    return (key) => this.transloco.translate(key);
  }

  private currentTheme(): Theme {
    const value = this.document.documentElement.getAttribute('data-theme');
    return value === 'light' || value === 'dark' ? value : 'system';
  }
}
