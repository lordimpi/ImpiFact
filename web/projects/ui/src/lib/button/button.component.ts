import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  input,
  output,
} from '@angular/core';
import { ButtonDirective, ButtonLabel } from '@openng/optimus-ui/button';

/**
 * Variante visual del botón. `primary` for the main action of a view, `secondary` (white with a border) for the rest,
 * `danger` for destructive actions; `ghost` (alias `text`) is a borderless button for low-emphasis actions.
 */
export type UiButtonVariant = 'primary' | 'secondary' | 'danger' | 'ghost' | 'text';

/** Button height: `small` inside tables and toolbars, `large` for the main action of a short form. */
export type UiButtonSize = 'small' | 'normal' | 'large';

/**
 * Botón del panel sobre un <button> nativo con el estilo de Optimus UI.
 * Recibe el texto ya traducido. Mientras carga queda deshabilitado y con `aria-busy`.
 */
@Component({
  selector: 'ui-button',
  imports: [ButtonDirective, ButtonLabel],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class.ui-button--block]': 'block()' },
  template: `
    <button
      pButton
      [attr.type]="type()"
      [severity]="severity()"
      [text]="borderless()"
      [size]="optimusSize()"
      [fluid]="block()"
      [disabled]="disabled() || loading()"
      [attr.aria-busy]="loading() || null"
      [attr.aria-label]="ariaLabel() ?? null"
      (click)="pressed.emit($event)"
    >
      @if (loading()) {
        <span class="ui-button__spinner" aria-hidden="true"></span>
      }
      <span pButtonLabel>{{ label() }}</span>
    </button>
  `,
  styles: `
    :host {
      display: inline-flex;
    }
    :host(.ui-button--block) {
      display: flex;
    }
    button {
      white-space: nowrap;
    }
    .ui-button__spinner {
      inline-size: 1em;
      block-size: 1em;
      border: 2px solid currentColor;
      border-inline-end-color: transparent;
      border-radius: 50%;
      animation: ui-button-spin 0.8s linear infinite;
    }
    @keyframes ui-button-spin {
      to {
        transform: rotate(360deg);
      }
    }
    @media (prefers-reduced-motion: reduce) {
      .ui-button__spinner {
        animation-duration: 2.4s;
      }
    }
  `,
})
export class ButtonComponent {
  /** Texto visible (ya traducido). */
  readonly label = input.required<string>();
  /** Nombre accesible cuando debe ser más explícito que el texto visible; debe contenerlo. */
  readonly ariaLabel = input<string>();
  readonly variant = input<UiButtonVariant>('primary');
  readonly size = input<UiButtonSize>('normal');
  /** Fills the width of its container (for example, the submit button of the login form). */
  readonly block = input(false, { transform: booleanAttribute });
  readonly type = input<'button' | 'submit' | 'reset'>('button');
  readonly disabled = input(false, { transform: booleanAttribute });
  readonly loading = input(false, { transform: booleanAttribute });

  /** Se emite al pulsar el botón; no se emite si está deshabilitado o cargando. */
  readonly pressed = output<MouseEvent>();

  protected readonly borderless = computed(() => {
    const variant = this.variant();
    return variant === 'ghost' || variant === 'text';
  });

  protected readonly optimusSize = computed(() => {
    const size = this.size();
    return size === 'normal' ? undefined : size;
  });

  protected readonly severity = computed(() => {
    const variant = this.variant();
    return variant === 'secondary' || variant === 'danger' ? variant : undefined;
  });
}
