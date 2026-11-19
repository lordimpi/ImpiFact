import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { FieldComponent, InputDirective, SelectDirective } from '@impifact/ui';
import { MASTER_STATUSES, MasterStatus } from '../state/master-list';

/** Textos de la barra, ya traducidos. */
export interface MasterListToolbarLabels {
  search: string;
  status: string;
  statuses: Record<MasterStatus, string>;
}

/**
 * Barra de búsqueda y filtro por estado de un listado (componente de presentación): muestra los valores que recibe
 * y emite cada cambio; la página decide cuándo pedirlos al Api (la búsqueda espera una pausa al escribir).
 */
@Component({
  selector: 'feat-master-list-toolbar',
  imports: [FieldComponent, InputDirective, SelectDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="toolbar" role="search">
      <ui-field class="toolbar__search" [label]="labels().search">
        <input
          uiInput
          type="search"
          autocomplete="off"
          [value]="search()"
          (input)="changeSearch($event)"
        />
      </ui-field>
      <ui-field class="toolbar__status" [label]="labels().status">
        <select uiSelect [value]="status()" (change)="changeStatus($event)">
          @for (option of statuses; track option) {
            <option [value]="option" [selected]="option === status()">
              {{ labels().statuses[option] }}
            </option>
          }
        </select>
      </ui-field>
    </div>
  `,
  styles: `
    .toolbar {
      display: flex;
      flex-wrap: wrap;
      gap: var(--if-space-3);
      align-items: flex-end;
    }
    .toolbar__search {
      flex: 1 1 18rem;
    }
    .toolbar__status {
      flex: 0 1 12rem;
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

  protected readonly statuses = MASTER_STATUSES;

  protected changeSearch(event: Event): void {
    this.searchInput.emit((event.target as HTMLInputElement).value);
  }

  protected changeStatus(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    const status = MASTER_STATUSES.find((option) => option === value);
    if (status) {
      this.statusChange.emit(status);
    }
  }
}
