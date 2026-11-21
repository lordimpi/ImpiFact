import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { FieldComponent, InputDirective, SelectComponent } from '@impifact/ui';
import { MASTER_STATUSES, MasterStatus } from '../state/master-list';

/** Textos de la barra, ya traducidos. */
export interface MasterListToolbarLabels {
  search: string;
  /** Example inside the empty search box («Buscar por …»). */
  searchPlaceholder: string;
  status: string;
  statuses: Record<MasterStatus, string>;
}

/**
 * Barra de búsqueda y filtro por estado de un listado (componente de presentación): muestra los valores que recibe
 * y emite cada cambio; la página decide cuándo pedirlos al Api (la búsqueda espera una pausa al escribir).
 * It sits at the top of the list card (`ui-card padding="none"`), above the flush table.
 */
@Component({
  selector: 'feat-master-list-toolbar',
  imports: [FieldComponent, InputDirective, SelectComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="toolbar" role="search">
      <ui-field class="toolbar__search" [label]="labels().search">
        <input
          uiInput
          type="search"
          autocomplete="off"
          [placeholder]="labels().searchPlaceholder"
          [value]="search()"
          (input)="changeSearch($event)"
        />
      </ui-field>
      <ui-field class="toolbar__status" [label]="labels().status">
        <ui-select
          [options]="statusOptions()"
          [value]="status()"
          (valueChange)="changeStatus($event)"
        />
      </ui-field>
    </div>
  `,
  styles: `
    .toolbar {
      display: flex;
      flex-wrap: wrap;
      gap: 0 var(--if-space-3);
      align-items: flex-end;
      padding: var(--if-space-3) var(--if-space-4) 0;
      border-block-end: 1px solid var(--if-color-border);
    }
    .toolbar__search {
      flex: 1 1 18rem;
      max-inline-size: 32rem;
    }
    .toolbar__status {
      flex: 0 1 12rem;
    }
    @media (max-width: 40rem) {
      .toolbar {
        padding-inline: var(--if-space-3);
      }
      .toolbar__search,
      .toolbar__status {
        flex-basis: 100%;
        max-inline-size: none;
      }
    }
  `,
})
export class MasterListToolbarComponent {
  readonly labels = input.required<MasterListToolbarLabels>();
  readonly search = input('');
  readonly status = input<MasterStatus>('active');

  /** Cada cambio del texto de búsqueda, sin esperar. */
  readonly searchInput = output<string>();
  readonly statusChange = output<MasterStatus>();

  protected readonly statusOptions = computed(() =>
    MASTER_STATUSES.map((id) => ({ id, name: this.labels().statuses[id] })),
  );

  protected changeSearch(event: Event): void {
    this.searchInput.emit((event.target as HTMLInputElement).value);
  }

  protected changeStatus(value: string): void {
    const status = MASTER_STATUSES.find((option) => option === value);
    if (status) {
      this.statusChange.emit(status);
    }
  }
}
