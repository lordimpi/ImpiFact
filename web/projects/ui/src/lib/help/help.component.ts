import {
  ChangeDetectionStrategy,
  Component,
  ViewEncapsulation,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { UI_TEXTS } from '../i18n/ui-texts';

let nextId = 0;

/**
 * Botón de información con un tooltip que explica un campo (presentación). Se abre al pasar el mouse, al llegar con
 * el teclado o al tocarlo en una pantalla táctil (cada toque abre o cierra), y se cierra con Escape o al salir. El
 * tooltip está siempre en el DOM con su id (`tooltipId`): el control que explica lo pone en su `aria-describedby`,
 * así un lector de pantalla lee la explicación aunque el tooltip esté cerrado. Se ubica bajo la etiqueta, a lo ancho
 * del contenedor posicionado más cercano (`ui-field` ya lo es; en un `fieldset`, póngale `position: relative`).
 *
 * Uso: `<legend>Responsabilidades <ui-help [text]="..." [label]="..." tooltipId="liabilities-help" /></legend>`
 */
@Component({
  selector: 'ui-help',
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
      <span aria-hidden="true">i</span>
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
    .ui-help__button {
      position: relative;
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
      line-height: 1;
      color: var(--if-color-primary);
      background: var(--if-color-primary-subtle);
      border: 1px solid var(--if-color-primary);
      border-radius: 50%;
      cursor: help;
    }
    .ui-help__button:hover {
      background: var(--if-color-primary-subtle-strong);
    }
    .ui-help__button:focus-visible {
      outline: var(--if-focus-ring-width) solid var(--if-color-focus);
      outline-offset: var(--if-focus-ring-offset);
    }
    .ui-help__tooltip {
      display: none;
      position: absolute;
      inset-inline: 0;
      z-index: 20;
      max-inline-size: 28rem;
      margin-block-start: 1.75rem;
      padding: var(--if-space-2) var(--if-space-3);
      font-size: var(--if-font-size-sm);
      font-weight: var(--if-font-weight-normal);
      line-height: var(--if-line-height);
      color: var(--if-color-text);
      background: var(--if-color-bg);
      border: 1px solid var(--if-color-border);
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
