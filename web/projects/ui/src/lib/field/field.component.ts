import {
  ChangeDetectionStrategy,
  Component,
  ViewEncapsulation,
  booleanAttribute,
  computed,
  input,
} from '@angular/core';

let nextId = 0;

/**
 * Campo de formulario: etiqueta, ayuda y error alrededor de un control proyectado con la directiva `uiInput`.
 * La directiva toma de aquí el id, `aria-describedby`, `aria-invalid` y `aria-required`.
 * Los textos llegan ya traducidos. Las validaciones de negocio no viven aquí: solo se muestra el error recibido.
 *
 * Uso: `<ui-field [label]="..." [error]="..."><input uiInput formControlName="nit" /></ui-field>`
 */
@Component({
  selector: 'ui-field',
  changeDetection: ChangeDetectionStrategy.OnPush,
  // Sin encapsulación para dar estilo al control proyectado; las clases llevan el prefijo ui-field.
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="ui-field" [class.ui-field--invalid]="!!error()">
      <label class="ui-field__label" [attr.for]="controlId">
        {{ label() }}
        @if (required()) {
          <span class="ui-field__required" aria-hidden="true">*</span>
        }
      </label>
      <ng-content />
      @if (hint()) {
        <p class="ui-field__hint" [id]="hintId">{{ hint() }}</p>
      }
      @if (error()) {
        <p class="ui-field__error" [id]="errorId">{{ error() }}</p>
      }
    </div>
  `,
  styles: `
    .ui-field {
      display: flex;
      flex-direction: column;
      gap: var(--if-space-1);
      margin-block-end: var(--if-space-3);
    }
    .ui-field__label {
      font-weight: var(--if-font-weight-medium);
      color: var(--if-color-text);
    }
    .ui-field__required {
      margin-inline-start: var(--if-space-1);
      color: var(--if-color-danger);
    }
    .ui-field__hint,
    .ui-field__error {
      margin: 0;
      font-size: var(--if-font-size-sm);
    }
    .ui-field__hint {
      color: var(--if-color-text-muted);
    }
    .ui-field__error {
      color: var(--if-color-danger);
    }
    .ui-field--invalid .ui-field__control {
      border-color: var(--if-color-danger);
    }
  `,
})
export class FieldComponent {
  /** Etiqueta visible del control (ya traducida). */
  readonly label = input.required<string>();
  /** Texto de ayuda bajo el control. */
  readonly hint = input<string>();
  /** Mensaje de error; si existe, el control queda marcado como inválido. */
  readonly error = input<string | null>();
  readonly required = input(false, { transform: booleanAttribute });

  readonly controlId = `ui-field-${nextId++}`;
  readonly hintId = `${this.controlId}-hint`;
  readonly errorId = `${this.controlId}-error`;

  /** Ids que describen el control: ayuda y error, en ese orden. */
  readonly describedBy = computed(() => {
    const ids = [this.hint() ? this.hintId : null, this.error() ? this.errorId : null];
    const value = ids.filter((id) => id !== null).join(' ');
    return value || null;
  });

  readonly invalid = computed(() => !!this.error());
}
