import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { FieldComponent, InputDirective } from '@impifact/ui';

/** Resultado de búsqueda ya preparado para mostrar. */
export interface PickerOption {
  id: string;
  title: string;
  detail?: string;
}

/** Textos del buscador, ya traducidos. */
export interface PickerLabels {
  search: string;
  hint: string;
  results: string;
  none: string;
  searching: string;
  error: string;
  /** Verbo al inicio del nombre accesible de cada resultado («Elegir», «Agregar»). */
  choose: string;
}

/**
 * Buscador de un maestro (clientes o productos) para la emisión (presentación): emite el texto que se busca y el
 * resultado elegido. La página consulta el Api; aquí no se filtra nada en el navegador.
 */
@Component({
  selector: 'feat-search-picker',
  imports: [FieldComponent, InputDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-field [label]="labels().search" [hint]="labels().hint">
      <input
        uiInput
        type="search"
        autocomplete="off"
        [value]="term()"
        (input)="termChange.emit(read($event))"
      />
    </ui-field>
    <div aria-live="polite">
      @if (loading()) {
        <p class="picker__muted">{{ labels().searching }}</p>
      } @else if (error()) {
        <p class="picker__error">{{ labels().error }}</p>
      } @else if (term().trim().length >= minLength()) {
        @if (options().length > 0) {
          <ul class="picker__list" [attr.aria-label]="labels().results">
            @for (option of options(); track option.id) {
              <li>
                <!-- El nombre accesible repite el texto visible precedido del verbo («Elegir …»). -->
                <button
                  type="button"
                  class="picker__option"
                  [attr.aria-label]="accessibleName(option)"
                  (click)="picked.emit(option.id)"
                >
                  <span class="picker__title">{{ option.title }}</span>
                  @if (option.detail) {
                    <span class="picker__muted">{{ option.detail }}</span>
                  }
                </button>
              </li>
            }
          </ul>
        } @else {
          <p class="picker__muted">{{ labels().none }}</p>
        }
      }
    </div>
  `,
  styles: `
    .picker__list {
      display: flex;
      flex-direction: column;
      margin: 0 0 var(--if-space-2);
      padding: 0;
      list-style: none;
      overflow: hidden;
      border: 1px solid var(--if-color-border);
      border-radius: var(--if-radius-md);
      box-shadow: var(--if-shadow-card);
    }
    .picker__list li + li {
      border-block-start: 1px solid var(--if-color-border);
    }
    .picker__option {
      display: flex;
      flex-wrap: wrap;
      gap: var(--if-space-1) var(--if-space-3);
      align-items: baseline;
      inline-size: 100%;
      padding: var(--if-space-2) var(--if-space-3);
      font: inherit;
      text-align: start;
      color: var(--if-color-text);
      background: var(--if-color-bg);
      border: 0;
      cursor: pointer;
    }
    .picker__option:hover {
      background: var(--if-color-primary-subtle);
    }
    .picker__option:focus-visible {
      outline: var(--if-focus-ring-width) solid var(--if-color-focus);
      outline-offset: calc(-1 * var(--if-focus-ring-width));
    }
    .picker__title {
      font-weight: var(--if-font-weight-medium);
    }
    .picker__muted {
      margin: 0;
      font-size: var(--if-font-size-sm);
      color: var(--if-color-text-muted);
    }
    .picker__error {
      margin: 0;
      color: var(--if-color-danger);
    }
  `,
})
export class SearchPickerComponent {
  readonly labels = input.required<PickerLabels>();
  readonly term = input('');
  readonly options = input<readonly PickerOption[]>([]);
  readonly loading = input(false);
  readonly error = input(false);
  /** Letras mínimas para buscar. */
  readonly minLength = input(2);
  readonly termChange = output<string>();
  readonly picked = output<string>();

  protected read(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected accessibleName(option: PickerOption): string {
    return [`${this.labels().choose} ${option.title}`, option.detail].filter(Boolean).join(', ');
  }
}
