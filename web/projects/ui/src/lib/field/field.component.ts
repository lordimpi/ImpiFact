import {
  ChangeDetectionStrategy,
  Component,
  ViewEncapsulation,
  booleanAttribute,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { UI_TEXTS } from '../i18n/ui-texts';

let nextId = 0;

/**
 * Campo de formulario: etiqueta, ayuda y error alrededor de un control proyectado con la directiva `uiInput`.
 * La directiva toma de aquí el id, `aria-describedby`, `aria-invalid` y `aria-required`. Con `help`, junto a la
 * etiqueta aparece un botón de información con un tooltip que explica el campo: se abre al pasar el mouse, al
 * llegar con el teclado o al tocarlo en una pantalla táctil, y se cierra con Escape. El texto describe también al
 * control (`aria-describedby`), así un lector de pantalla lo lee aunque el tooltip esté cerrado.
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
      <div class="ui-field__head">
        <label class="ui-field__label" [id]="labelId" [attr.for]="controlId">
          {{ label() }}
          @if (required()) {
            <span class="ui-field__required" aria-hidden="true">*</span>
          }
        </label>
        @if (help()) {
          <button
            type="button"
            class="ui-field__help-button"
            [attr.aria-label]="helpButtonLabel()"
            [attr.aria-describedby]="helpId"
            [attr.aria-expanded]="helpOpen()"
            (focus)="setHelp('focused', true)"
            (blur)="closeHelp()"
            (mouseenter)="setHelp('hovered', true)"
            (mouseleave)="setHelp('hovered', false)"
            (click)="toggleHelp()"
            (keydown.escape)="dismissHelp($event)"
          >
            <span aria-hidden="true">i</span>
          </button>
        }
      </div>
      @if (help()) {
        <span
          role="tooltip"
          class="ui-field__tooltip"
          [class.ui-field__tooltip--open]="helpOpen()"
          [id]="helpId"
          >{{ help() }}</span
        >
      }
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
      gap: var(--if-space-1);
      margin-block-end: var(--if-space-3);
    }
    .ui-field__head {
      display: flex;
      align-items: center;
      gap: var(--if-space-2);
    }
    .ui-field__help-button {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      flex: none;
      inline-size: 1.5rem;
      block-size: 1.5rem;
      padding: 0;
      font: inherit;
      font-size: var(--if-font-size-sm);
      font-weight: var(--if-font-weight-bold);
      font-style: italic;
      color: var(--if-color-primary);
      background: var(--if-color-primary-subtle);
      border: 1px solid var(--if-color-primary);
      border-radius: 50%;
      cursor: help;
    }
    .ui-field__help-button:focus-visible {
      outline: var(--if-focus-ring-width) solid var(--if-color-focus);
      outline-offset: var(--if-focus-ring-offset);
    }
    .ui-field__tooltip {
      display: none;
      position: absolute;
      inset-block-start: 2rem;
      inset-inline: 0;
      z-index: 20;
      max-inline-size: 28rem;
      padding: var(--if-space-2) var(--if-space-3);
      font-size: var(--if-font-size-sm);
      line-height: var(--if-line-height);
      color: var(--if-color-text);
      background: var(--if-color-bg);
      border: 1px solid var(--if-color-border-strong);
      border-radius: var(--if-radius-sm);
      box-shadow: var(--if-shadow-overlay);
    }
    .ui-field__tooltip--open {
      display: block;
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
    .ui-select {
      min-block-size: var(--if-control-min-height);
      padding: var(--if-control-padding-y) var(--if-control-padding-x);
      font: inherit;
      color: var(--if-color-text);
      background: var(--if-color-surface);
      border: 1px solid var(--if-color-border-strong);
      border-radius: var(--if-radius-sm);
    }
    .ui-select:focus-visible {
      outline: var(--if-focus-ring-width) solid var(--if-color-focus);
      outline-offset: var(--if-focus-ring-offset);
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

  private readonly texts = inject(UI_TEXTS);
  protected readonly helpButtonLabel = computed(() => this.texts.fieldHelp(this.label()));
  private readonly helpState = signal({
    focused: false,
    hovered: false,
    pinned: false,
    dismissed: false,
  });
  /** El tooltip se ve con foco, mouse encima o tocado, salvo que se haya cerrado con Escape. */
  protected readonly helpOpen = computed(() => {
    const state = this.helpState();
    return (state.focused || state.hovered || state.pinned) && !state.dismissed;
  });

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

  protected setHelp(key: 'focused' | 'hovered', value: boolean): void {
    this.helpState.update((state) => ({
      ...state,
      [key]: value,
      dismissed: value ? false : state.dismissed,
    }));
  }

  /** En una pantalla táctil no hay mouse ni siempre foco: cada toque abre o cierra el tooltip. */
  protected toggleHelp(): void {
    this.helpState.update((state) => ({ ...state, pinned: !state.pinned, dismissed: false }));
  }

  protected closeHelp(): void {
    this.helpState.update((state) => ({
      ...state,
      focused: false,
      pinned: false,
      dismissed: false,
    }));
  }

  protected dismissHelp(event: Event): void {
    if (this.helpOpen()) {
      event.stopPropagation();
    }
    this.helpState.update((state) => ({ ...state, pinned: false, dismissed: true }));
  }
}
