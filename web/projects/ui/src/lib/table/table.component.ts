import {
  ChangeDetectionStrategy,
  Component,
  TemplateRef,
  booleanAttribute,
  computed,
  contentChildren,
  input,
  numberAttribute,
  output,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Skeleton } from '@openng/optimus-ui/skeleton';
import { UiPageChange, UiPagination } from '../paginator/pagination';
import { PaginatorComponent } from '../paginator/paginator.component';
import { EmptyStateComponent } from '../states/empty-state.component';
import { ErrorStateComponent } from '../states/error-state.component';
import { TableCellDirective, UiTableCellContext } from './table-cell.directive';
import { UiFilters, UiSort, UiTableColumn, UiTableLabels } from './table.models';

let nextId = 0;

/**
 * Tabla de datos paginada en el servidor. Nunca ordena, filtra ni pagina en el navegador:
 * muestra las filas tal como llegan y emite `sortChange`, `filterChange` y `pageChange` para que
 * quien la usa pida al Api la página correspondiente. Muestra los estados de carga (esqueletos),
 * vacío y error (con reintento). La cabecera queda fija al desplazarse.
 *
 * On a narrow container (under 40rem: a phone, or a narrow card) each row stacks as a block where every cell shows
 * its column header next to the value, instead of scrolling sideways; sort buttons and filters stay above the rows.
 */
@Component({
  selector: 'ui-table',
  imports: [
    NgTemplateOutlet,
    InputText,
    Skeleton,
    PaginatorComponent,
    EmptyStateComponent,
    ErrorStateComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class.ui-table--stack]': 'stackOnNarrow()' },
  templateUrl: './table.component.html',
  styleUrl: './table.component.css',
})
export class TableComponent<T extends object> {
  /** Nombre accesible de la tabla (ya traducido). */
  readonly caption = input.required<string>();
  /** Muestra el título encima de la tabla; si no, queda solo para lectores de pantalla. */
  readonly showCaption = input(false, { transform: booleanAttribute });
  readonly columns = input.required<readonly UiTableColumn[]>();
  readonly rows = input<readonly T[]>([]);
  readonly labels = input.required<UiTableLabels>();
  readonly loading = input(false, { transform: booleanAttribute });
  readonly error = input(false, { transform: booleanAttribute });
  /** Orden aplicado por el Api a las filas recibidas. */
  readonly sort = input<UiSort | null>(null);
  /** Filtros aplicados por el Api a las filas recibidas. */
  readonly filters = input<UiFilters>({});
  /** Paginación entregada por el Api; sin ella no hay paginador. */
  readonly pagination = input<UiPagination | null>(null);
  /** Filas de esqueleto mientras carga. */
  readonly skeletonRows = input(5, { transform: numberAttribute });
  /** Without its own border and radius, to sit flush inside a `ui-card padding="none"`. */
  readonly flush = input(false, { transform: booleanAttribute });
  /** Alternate row background (zebra) for long, dense lists. */
  readonly striped = input(false, { transform: booleanAttribute });
  /**
   * Stacks the rows on a narrow container instead of scrolling sideways. On by default, because every list of the
   * panel reads better stacked on a phone; turn it off for a grid whose columns must be compared across rows.
   */
  readonly stackOnNarrow = input(true, { transform: booleanAttribute });
  /** Identidad de cada fila para el `@for`; por defecto, la propia fila. */
  readonly trackBy = input<(index: number, row: T) => unknown>((_, row) => row);

  readonly sortChange = output<UiSort>();
  readonly filterChange = output<UiFilters>();
  readonly pageChange = output<UiPageChange>();
  readonly retry = output<void>();

  protected readonly captionId = `ui-table-${nextId++}-caption`;

  private readonly cellDirectives = contentChildren<TableCellDirective<T>>(TableCellDirective);

  protected readonly cellTemplates = computed(() => {
    const templates = new Map<string, TemplateRef<UiTableCellContext<T>>>();
    for (const cell of this.cellDirectives()) templates.set(cell.key(), cell.template);
    return templates;
  });

  protected readonly hasFilters = computed(() =>
    this.columns().some((column) => column.filterable),
  );

  protected readonly hasSorting = computed(() => this.columns().some((column) => column.sortable));

  protected readonly skeletonList = computed(() =>
    Array.from({ length: Math.max(1, this.skeletonRows()) }, (_, index) => index),
  );

  protected ariaSort(key: string): 'ascending' | 'descending' | 'none' {
    const sort = this.sort();
    if (sort?.key !== key) return 'none';
    return sort.direction === 'asc' ? 'ascending' : 'descending';
  }

  protected sortIcon(key: string): string {
    const sort = this.ariaSort(key);
    return sort === 'ascending' ? '↑' : sort === 'descending' ? '↓' : '↕';
  }

  /** Columna sin orden o descendente → ascendente; ascendente → descendente. */
  protected toggleSort(key: string): void {
    const sort = this.sort();
    const direction = sort?.key === key && sort.direction === 'asc' ? 'desc' : 'asc';
    this.sortChange.emit({ key, direction });
  }

  protected changeFilter(key: string, event: Event): void {
    const value = (event.target as HTMLInputElement).value.trim();
    const next: Record<string, string> = { ...this.filters() };
    if (value) next[key] = value;
    else delete next[key];
    this.filterChange.emit(next);
  }

  protected cellValue(row: T, key: string): string {
    const value = (row as Record<string, unknown>)[key];
    return value === null || value === undefined ? '' : String(value);
  }
}
