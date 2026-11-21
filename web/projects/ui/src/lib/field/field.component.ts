import {
  ChangeDetectionStrategy,
  Component,
  ViewEncapsulation,
  booleanAttribute,
  computed,
  input,
} from '@angular/core';
import { HelpComponent } from '../help/help.component';

let nextId = 0;

/**
 * Campo de formulario: etiqueta, ayuda y error alrededor de un control proyectado con la directiva `uiInput`.
 * La directiva toma de aquí el id, `aria-describedby`, `aria-invalid` y `aria-required`. Con `help`, junto a la
 * etiqueta aparece un botón de información (`ui-help`) con un tooltip que explica el campo; ese texto describe
 * también al control (`aria-describedby`), así un lector de pantalla lo lee aunque el tooltip esté cerrado.
 * Los textos llegan ya traducidos. Las validaciones de negocio no viven aquí: solo se muestra el error recibido.
 *
 * Uso: `<ui-field [label]="..." [error]="..."><input uiInput formControlName="nit" /></ui-field>`
 */
@Component({
  selector: 'ui-field',
  imports: [HelpComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  // Sin encapsulación para dar estilo al control proyectado; las clases llevan el prefijo ui-field.
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="ui-field" [class.ui-field--invalid]="!!error()">
      <div class="ui-field__head">
        <label class="ui-field__label" [id]="labelId" [attr.for]="controlId">
          {{ label() }}
          @if (required()) {
            <span class="ui-field__required" aria-hidden="true">*</span>
          }
        </label>
        @if (help(); as help) {
          <ui-help [text]="help" [label]="label()" [tooltipId]="helpId" />
        }
      </div>
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
      position: relative;
      display: flex;
      flex-direction: column;
      gap: 0.375rem;
      margin-block-end: var(--if-space-3);
    }
    .ui-field__head {
      display: flex;
      align-items: center;
      gap: var(--if-space-2);
    }
    .ui-field__label {
      font-size: var(--if-font-size-sm);
      font-weight: var(--if-font-weight-medium);
      color: var(--if-color-text);
    }
    .ui-field__control {
      min-block-size: var(--if-control-min-height);
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
    .ui-select {
      min-block-size: var(--if-control-min-height);
      padding: var(--if-control-padding-y) var(--if-control-padding-x);
      font: inherit;
      color: var(--if-color-text);
      background: var(--if-color-bg);
      border: 1px solid var(--if-color-border-strong);
      border-radius: var(--if-radius-sm);
      box-shadow: 0 1px 2px rgb(16 24 40 / 5%);
      cursor: pointer;
    }
    .ui-select:hover {
      border-color: var(--if-color-text-muted);
    }
    .ui-select:focus-visible {
      border-color: var(--if-color-primary);
      outline: var(--if-focus-ring-width) solid var(--if-color-focus);
      outline-offset: 0;
    }
    .ui-select:disabled {
      color: var(--if-color-text-muted);
      background: var(--if-color-surface-raised);
      cursor: not-allowed;
    }
    /* Optimus controls (ui-select, ui-combobox, ui-date-picker) take the whole width of the field. */
    .ui-field .p-select,
    .ui-field .p-datepicker {
      inline-size: 100%;
    }
    /* Campo de contraseña con el botón para mostrarla (uiPasswordToggle). */
    .ui-password {
      position: relative;
      display: flex;
    }
    .ui-password > .ui-field__control {
      flex: 1;
      padding-inline-end: calc(var(--if-control-min-height) + var(--if-space-1));
    }
    /* Edge pone su propio botón de mostrar la contraseña; con el nuestro sobraría (se verían dos ojos). */
    .ui-password > .ui-field__control::-ms-reveal,
    .ui-password > .ui-field__control::-ms-clear {
      display: none;
    }
    .ui-password__toggle {
      position: absolute;
      inset-block: 0;
      inset-inline-end: 0;
      display: grid;
      place-items: center;
      inline-size: var(--if-control-min-height);
      padding: 0;
      color: var(--if-color-text-muted, var(--if-color-text));
      background: transparent;
      border: 0;
      border-radius: var(--if-radius-sm);
      cursor: pointer;
    }
    .ui-password__toggle:hover {
      color: var(--if-color-text);
    }
    .ui-password__toggle:focus-visible {
      outline: var(--if-focus-ring-width) solid var(--if-color-focus);
      outline-offset: calc(-1 * var(--if-focus-ring-width));
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
  /** Explicación del campo (ya traducida) para el tooltip del botón de información. */
  readonly help = input<string | null>();

  readonly controlId = `ui-field-${nextId++}`;
  readonly labelId = `${this.controlId}-label`;
  readonly hintId = `${this.controlId}-hint`;
  readonly errorId = `${this.controlId}-error`;
  readonly helpId = `${this.controlId}-help`;

  /** Ids que describen el control: explicación, ayuda y error, en ese orden. */
  readonly describedBy = computed(() => {
    const ids = [
      this.help() ? this.helpId : null,
      this.hint() ? this.hintId : null,
      this.error() ? this.errorId : null,
    ];
    const value = ids.filter((id) => id !== null).join(' ');
    return value || null;
  });

  readonly invalid = computed(() => !!this.error());
}
