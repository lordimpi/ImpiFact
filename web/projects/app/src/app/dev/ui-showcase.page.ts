import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import {
  BadgeComponent,
  ButtonComponent,
  CardActionsDirective,
  CardComponent,
  CardFooterDirective,
  CheckboxComponent,
  ComboboxComponent,
  ComboboxLabels,
  DatePickerComponent,
  DialogComponent,
  EmptyStateComponent,
  ErrorStateComponent,
  FieldComponent,
  FileUploadComponent,
  InputDirective,
  LoadingComponent,
  MeterComponent,
  PageTitleComponent,
  PaginatorComponent,
  SelectComponent,
  StatComponent,
  TableCellDirective,
  TableComponent,
  TabsComponent,
  ToastRegionComponent,
  UiBadgeTone,
  UiFilters,
  UiPageChange,
  UiPagination,
  UiPaginatorLabels,
  UiSort,
  UiTab,
  UiTableColumn,
  UiTableLabels,
  UiToast,
  UiToastLabels,
} from '@impifact/ui';
import {
  SAMPLE_DEPARTMENTS,
  SAMPLE_MUNICIPALITIES,
  SAMPLE_REGIMES,
  SHOWCASE_TEXTS,
} from './ui-showcase.texts';

interface DocumentRow {
  id: number;
  number: string;
  customer: string;
  status: string;
  tone: UiBadgeTone;
  total: string;
}

// Datos de ejemplo de la vitrina (no son textos de interfaz): simulan una página que devolvió el Api.
const SAMPLE_ROWS: DocumentRow[] = [
  {
    id: 3,
    number: 'SETP990000003',
    customer: 'Zapatos del Valle SAS',
    status: 'Rechazado',
    tone: 'danger',
    total: '$ 1.190.000',
  },
  {
    id: 1,
    number: 'SETP990000001',
    customer: 'Alfa Ingeniería Ltda',
    status: 'Aceptado',
    tone: 'success',
    total: '$ 238.000',
  },
  {
    id: 2,
    number: 'SETP990000002',
    customer: 'Mercados Andinos SA',
    status: 'En cola',
    tone: 'info',
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
    BadgeComponent,
    StatComponent,
    CardComponent,
    CheckboxComponent,
    CardActionsDirective,
    CardFooterDirective,
    TabsComponent,
    FieldComponent,
    InputDirective,
    ComboboxComponent,
    SelectComponent,
    DatePickerComponent,
    FileUploadComponent,
    MeterComponent,
    EmptyStateComponent,
    LoadingComponent,
    ErrorStateComponent,
    TableComponent,
    TableCellDirective,
    PaginatorComponent,
    DialogComponent,
    ToastRegionComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './ui-showcase.page.html',
  styleUrl: './ui-showcase.page.scss',
})
export class UiShowcasePage {
  private readonly transloco = inject(TranslocoService);
  // Cambia cuando terminan de cargar los textos, para recalcular las etiquetas traducidas.
  private readonly translation = toSignal(this.transloco.selectTranslation());

  protected readonly rows = SAMPLE_ROWS;
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
  protected readonly municipality = signal('');
  protected readonly term = signal('');
  protected readonly departments = SAMPLE_DEPARTMENTS;
  protected readonly department = signal('');
  protected readonly regimes = SAMPLE_REGIMES;
  protected readonly regime = signal('');
  protected readonly sendEmail = signal(false);
  protected readonly expiresAt = signal('');
  /** Largest certificate the example accepts: 2 MB. */
  protected readonly maxCertificateSize = 2 * 1024 * 1024;
  protected readonly toasts = signal<readonly UiToast[]>([]);
  private nextToastId = 1;

  constructor() {
    // The texts of the newer sections live next to the page (see ui-showcase.texts.ts).
    this.transloco.setTranslation(SHOWCASE_TEXTS, this.transloco.getActiveLang(), { merge: true });
  }

  protected readonly documentTypes = computed(() => {
    const t = this.translator();
    return (
      [
        ['13', 'cc'],
        ['31', 'nit'],
        ['22', 'ce'],
      ] as const
    ).map(([id, key]) => ({ id, name: t(`devUi.fields.documentTypes.${key}`) }));
  });

  protected readonly tabs = computed<UiTab[]>(() => {
    const t = this.translator();
    return (['data', 'resolutions', 'certificate', 'users'] as const).map((tab) => ({
      label: t(`devUi.tabs.${tab}`),
      link: '/dev/ui',
      queryParams: { tab },
    }));
  });

  /**
   * The showcase plays the server: it lists the sample municipalities of the chosen department that match the typed
   * term. Without a department the search is idle and the municipality stays locked.
   */
  protected readonly municipalities = computed(() => {
    const department = Number(this.department());
    const term = this.term().trim().toLocaleLowerCase('es-CO');
    return SAMPLE_MUNICIPALITIES.filter(
      (option) =>
        Math.floor(option.id / 1000) === department &&
        option.name.toLocaleLowerCase('es-CO').includes(term),
    );
  });

  protected chooseDepartment(id: string): void {
    this.department.set(id);
    this.municipality.set('');
    this.log('department', id);
  }

  protected readonly comboboxLabels = computed<ComboboxLabels>(() => {
    const t = this.translator();
    return {
      placeholder: t('devUi.fields.municipalityPlaceholder'),
      idle: t('devUi.fields.municipalityIdle'),
      loading: t('devUi.fields.municipalityLoading'),
      error: t('devUi.fields.municipalityError'),
      empty: t('devUi.fields.municipalityEmpty'),
      retry: t('ui.retry'),
    };
  });

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

  protected readonly toastLabels = computed<UiToastLabels>(() => {
    const t = this.translator();
    return {
      region: t('ui.toasts.region'),
      close: t('ui.toasts.close'),
      copy: t('ui.toasts.copy'),
      reference: t('ui.toasts.reference'),
    };
  });

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

  protected showToasts(): void {
    const t = this.translator();
    this.toasts.update((current) => [
      ...current,
      { id: this.nextToastId++, kind: 'success', text: t('devUi.toasts.success') },
      {
        id: this.nextToastId++,
        kind: 'error',
        text: t('devUi.toasts.error'),
        reference: '7f3c9a2e-41b0',
      },
    ]);
  }

  protected dismissToast(id: number): void {
    this.toasts.update((current) => current.filter((toast) => toast.id !== id));
  }

  protected log(name: string, payload?: unknown): void {
    this.lastEvent.set(payload === undefined ? name : `${name} ${JSON.stringify(payload)}`);
  }

  private translator(): (key: string) => string {
    this.translation();
    return (key) => this.transloco.translate(key);
  }
}
