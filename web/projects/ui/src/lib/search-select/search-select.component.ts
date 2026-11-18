import { ChangeDetectionStrategy, Component, booleanAttribute, input, output } from '@angular/core';
import { ButtonComponent } from '../button/button.component';
import { FieldComponent } from '../field/field.component';
import { InputDirective } from '../field/input.directive';

/** Textos del buscador, ya traducidos. */
export interface SearchSelectLabels {
  /** Etiqueta del campo de búsqueda («Buscar municipio»). */
  search: string;
  hint?: string;
  loading: string;
  error: string;
  /** Sin resultados para el texto buscado. */
  empty: string;
  /** Botón para traer la página siguiente. */
  more: string;
  retry: string;
}

/**
 * Lista desplegable con búsqueda en el servidor (presentación): proyecta el `<select uiSelect>` del formulario,
 * que sigue siendo nativo y guarda el valor, y le suma un campo de búsqueda, el estado de la consulta (anunciado a
 * los lectores de pantalla) y «Cargar más». Quien la usa pide los datos al Api; aquí no se filtra nada.
 *
 * Uso: `<ui-search-select [labels]="..." [status]="..." (termChange)="..."><ui-field ...><select uiSelect ...>`
 */
@Component({
  selector: 'ui-search-select',
  imports: [ButtonComponent, FieldComponent, InputDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="ui-search-select">
      <ng-content />
      <ui-field [label]="labels().search" [hint]="labels().hint">
        <input
          uiInput
          type="search"
          autocomplete="off"
          [value]="term()"
          (input)="termChange.emit(read($event))"
        />
      </ui-field>
      <div class="ui-search-select__status" aria-live="polite">
        @switch (status()) {
          @case ('loading') {
            <p class="ui-search-select__muted">{{ labels().loading }}</p>
          }
          @case ('error') {
            <p class="ui-search-select__error">{{ labels().error }}</p>
            <ui-button
              variant="text"
              size="small"
              [label]="labels().retry"
              (pressed)="retry.emit()"
            />
          }
          @default {
            @if (empty()) {
              <p class="ui-search-select__muted">{{ labels().empty }}</p>
            }
          }
        }
      </div>
      @if (hasMore()) {
        <ui-button
          variant="text"
          size="small"
          [label]="labels().more"
          (pressed)="loadMore.emit()"
        />
      }
    </div>
  `,
  styles: `
    .ui-search-select {
      display: flex;
      flex-direction: column;
      gap: var(--if-space-1);
      margin-block-end: var(--if-space-3);
    }
    .ui-search-select__status:empty {
      display: none;
    }
    .ui-search-select__muted,
    .ui-search-select__error {
      margin: 0;
      font-size: var(--if-font-size-sm);
    }
    .ui-search-select__muted {
      color: var(--if-color-text-muted);
    }
    .ui-search-select__error {
      color: var(--if-color-danger);
    }
  `,
})
export class SearchSelectComponent {
  readonly labels = input.required<SearchSelectLabels>();
  readonly term = input('');
  readonly status = input<'loading' | 'ready' | 'error'>('ready');
  readonly hasMore = input(false, { transform: booleanAttribute });
  /** La búsqueda terminó sin filas. */
  readonly empty = input(false, { transform: booleanAttribute });
  readonly termChange = output<string>();
  readonly loadMore = output<void>();
  readonly retry = output<void>();

  protected read(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }
}
