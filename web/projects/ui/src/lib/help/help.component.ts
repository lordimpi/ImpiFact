import {
  ChangeDetectionStrategy,
  Component,
  ViewEncapsulation,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { InfoCircleIcon } from '@openng/optimus-ui/icons/infocircle';
import { UI_TEXTS } from '../i18n/ui-texts';

let nextId = 0;

/**
 * Botón de información (el ícono `info-circle` de Optimus UI, chico y junto al texto de la etiqueta) con un tooltip que explica un campo (presentación). Se abre al pasar el mouse, al llegar con
 * el teclado o al tocarlo en una pantalla táctil (cada toque abre o cierra), y se cierra con Escape o al salir. El
 * tooltip está siempre en el DOM con su id (`tooltipId`): el control que explica lo pone en su `aria-describedby`,
 * así un lector de pantalla lee la explicación aunque el tooltip esté cerrado. Se ubica bajo la etiqueta, a lo ancho
 * del contenedor posicionado más cercano (`ui-field` ya lo es; en un `fieldset`, póngale `position: relative`).
 *
 * Uso: `<legend>Responsabilidades <ui-help [text]="..." [label]="..." tooltipId="liabilities-help" /></legend>`
 *
 * The bubble is ours and not Optimus `pTooltip`: that directive creates its element on show and removes it on hide,
 * so the control's `aria-describedby` would point at nothing while it is closed. It is styled like the Optimus
 * tooltip (inverted surface, small text) with the same tokens the Optimus preset maps.
 */
@Component({
  selector: 'ui-help',
  imports: [InfoCircleIcon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  // Sin encapsulación: el tooltip se ubica respecto del campo que lo contiene; las clases llevan el prefijo ui-help.
  encapsulation: ViewEncapsulation.None,
  host: { class: 'ui-help' },
  template: `
    <button
      type="button"
      class="ui-help__button"
      [attr.aria-describedby]="id()"
      [attr.aria-expanded]="open()"
      (focus)="set('focused', true)"
      (blur)="close()"
      (mouseenter)="set('hovered', true)"
      (mouseleave)="set('hovered', false)"
      (click)="toggle()"
      (keydown.escape)="dismiss($event)"
    >
      <svg
        data-p-icon="info-circle"
        class="ui-help__icon"
        aria-hidden="true"
        focusable="false"
      ></svg>
      <!-- El nombre va como texto y no en aria-label: así no se confunde con la etiqueta del campo. -->
      <span class="ui-visually-hidden">{{ buttonLabel() }}</span>
    </button>
    <span
      role="tooltip"
      class="ui-help__tooltip"
      [class.ui-help__tooltip--open]="open()"
      [id]="id()"
      >{{ text() }}</span
    >
  `,
  styles: `
    .ui-help {
      display: inline-flex;
      vertical-align: middle;
    }
    /* A 24px hit area (WCAG 2.5.8) around a 1rem icon; the negative margin keeps the label line height. */
    .ui-help__button {
      position: relative;
      display: inline-flex;
      align-items: center;
      justify-content: center;
      flex: none;
      inline-size: 1.5rem;
      block-size: 1.5rem;
      margin-block: -0.25rem;
      padding: 0;
      color: var(--if-color-text-muted);
      background: none;
      border: 0;
      border-radius: var(--if-radius-sm);
      cursor: help;
    }
    .ui-help__button:hover,
    .ui-help__button:focus-visible,
    .ui-help__button[aria-expanded='true'] {
      color: var(--if-color-primary);
    }
    .ui-help__button:focus-visible {
      outline: var(--if-focus-ring-width) solid var(--if-color-focus);
      outline-offset: 0;
    }
    .ui-help__button svg {
      inline-size: 1rem;
      block-size: 1rem;
    }
    .ui-help__tooltip {
      display: none;
      position: absolute;
      inset-inline: 0;
      z-index: 20;
      max-inline-size: 22rem;
      margin-block-start: 1.75rem;
      padding: var(--if-space-2) var(--if-space-3);
      font-size: var(--if-font-size-sm);
      font-weight: var(--if-font-weight-normal);
      line-height: var(--if-line-height);
      color: var(--if-color-bg);
      background: var(--if-color-text);
      border-radius: var(--if-radius-md);
      box-shadow: var(--if-shadow-overlay);
    }
    .ui-help__tooltip--open {
      display: block;
    }
  `,
})
export class HelpComponent {
  /** Explicación (ya traducida). */
  readonly text = input.required<string>();
  /** Etiqueta del campo que explica: con ella se nombra el botón («Ayuda sobre NIT»). */
  readonly label = input.required<string>();
  /** Id del tooltip, para el `aria-describedby` del control; si no llega, se genera uno. */
  readonly tooltipId = input<string>();

  private readonly ownId = `ui-help-${nextId++}`;
  protected readonly id = computed(() => this.tooltipId() ?? this.ownId);

  private readonly texts = inject(UI_TEXTS);
  protected readonly buttonLabel = computed(() => this.texts.fieldHelp(this.label()));
  private readonly state = signal({
    focused: false,
    hovered: false,
    pinned: false,
    dismissed: false,
  });
  /** Se ve con foco, mouse encima o tocado, salvo que se haya cerrado con Escape. */
  protected readonly open = computed(() => {
    const state = this.state();
    return (state.focused || state.hovered || state.pinned) && !state.dismissed;
  });

  protected set(key: 'focused' | 'hovered', value: boolean): void {
    this.state.update((state) => ({
      ...state,
      [key]: value,
      dismissed: value ? false : state.dismissed,
    }));
  }

  /** En una pantalla táctil no hay mouse ni siempre foco: cada toque abre o cierra el tooltip. */
  protected toggle(): void {
    this.state.update((state) => ({ ...state, pinned: !state.pinned, dismissed: false }));
  }

  protected close(): void {
    this.state.update((state) => ({ ...state, focused: false, pinned: false, dismissed: false }));
  }

  protected dismiss(event: Event): void {
    if (this.open()) {
      // Que Escape no cierre también el diálogo que contiene el campo.
      event.stopPropagation();
    }
    this.state.update((state) => ({ ...state, pinned: false, dismissed: true }));
  }
}
