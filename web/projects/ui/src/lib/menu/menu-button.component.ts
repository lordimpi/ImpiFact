import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  booleanAttribute,
  computed,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { MenuItem } from '@openng/optimus-ui/api';
import { ButtonDirective, ButtonLabel } from '@openng/optimus-ui/button';
import { ChevronDownIcon } from '@openng/optimus-ui/icons/chevrondown';
import { Menu } from '@openng/optimus-ui/menu';

/** One option of a `ui-menu-button`: a stable id (what `selected` emits) and its translated label. */
export interface UiMenuItem {
  readonly id: string;
  readonly label: string;
  readonly disabled?: boolean;
}

let nextId = 0;

/**
 * Menu button (WAI-ARIA menu button pattern) with the look of a secondary `ui-button` and a chevron. Pressing it
 * opens the Optimus UI popup menu below it: the button carries `aria-haspopup="menu"` and `aria-expanded`, the
 * options are `menuitem`s, the arrow keys, Home and End move between them, Enter or Space picks one, and Escape or
 * Tab closes the menu. Focus goes back to the button when the menu closes. Adding an option is one more entry in
 * `items`.
 *
 * Usage: `<ui-menu-button [label]="'Exportar'" [items]="formats" (selected)="export($event)" />`; in a table row,
 * `<ui-menu-button size="small" label="Acciones" [ariaLabel]="'Acciones de ' + row.name" ... />`.
 */
@Component({
  selector: 'ui-menu-button',
  imports: [ButtonDirective, ButtonLabel, ChevronDownIcon, Menu],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button
      #trigger
      pButton
      type="button"
      [severity]="variant() === 'secondary' ? 'secondary' : undefined"
      [size]="size() === 'small' ? 'small' : undefined"
      [attr.aria-label]="ariaLabel() || null"
      [disabled]="disabled() || loading()"
      aria-haspopup="menu"
      [attr.aria-expanded]="open()"
      [attr.aria-controls]="open() ? menuId + '_list' : null"
      [attr.aria-busy]="loading() || null"
      (click)="menu.toggle($event)"
    >
      @if (loading()) {
        <span class="ui-menu-button__spinner" aria-hidden="true"></span>
      }
      <span pButtonLabel>{{ label() }}</span>
      <svg
        data-p-icon="chevron-down"
        class="ui-menu-button__chevron"
        aria-hidden="true"
        focusable="false"
      ></svg>
    </button>
    <p-menu
      #menu
      [id]="menuId"
      [model]="model()"
      [popup]="true"
      appendTo="body"
      [ariaLabel]="ariaLabel() || label()"
      (onShow)="open.set(true)"
      (onHide)="closed()"
    />
  `,
  styles: `
    :host {
      display: inline-flex;
    }
    button {
      white-space: nowrap;
    }
    .ui-menu-button__chevron {
      inline-size: 0.875em;
      block-size: 0.875em;
    }
    .ui-menu-button__spinner {
      inline-size: 1em;
      block-size: 1em;
      border: 2px solid currentColor;
      border-inline-end-color: transparent;
      border-radius: 50%;
      animation: ui-menu-button-spin 0.8s linear infinite;
    }
    @keyframes ui-menu-button-spin {
      to {
        transform: rotate(360deg);
      }
    }
    @media (prefers-reduced-motion: reduce) {
      .ui-menu-button__spinner {
        animation-duration: 2.4s;
      }
    }
  `,
})
export class MenuButtonComponent {
  private readonly document = inject(DOCUMENT);

  /** Visible text of the button (already translated); it is also the accessible name of the menu. */
  readonly label = input.required<string>();
  /** Options, in order; each one is a single line here. */
  readonly items = input.required<readonly UiMenuItem[]>();
  /**
   * Accessible name of the button and of its menu when the visible label is not enough on its own, for example one
   * «Acciones» menu per table row named «Acciones de <row>». It should start with the visible label (WCAG 2.5.3).
   */
  readonly ariaLabel = input<string>();
  readonly variant = input<'primary' | 'secondary'>('secondary');
  /** `small` inside tables and toolbars, like `ui-button`. */
  readonly size = input<'normal' | 'small'>('normal');
  readonly disabled = input(false, { transform: booleanAttribute });
  /** While loading the button is disabled, with a spinner and `aria-busy`. */
  readonly loading = input(false, { transform: booleanAttribute });

  /** Emits the id of the chosen option. */
  readonly selected = output<string>();

  /** Id of the Optimus menu; its `role="menu"` list gets `<id>_list`. */
  protected readonly menuId = `ui-menu-button-${nextId++}`;
  protected readonly open = signal(false);
  private readonly trigger = viewChild.required<ElementRef<HTMLButtonElement>>('trigger');

  protected readonly model = computed<MenuItem[]>(() =>
    this.items().map((item) => ({
      id: `${this.menuId}-${item.id}`,
      label: item.label,
      disabled: item.disabled,
      command: () => this.selected.emit(item.id),
    })),
  );

  /** Gives focus back to the button unless the user moved it elsewhere (for example, by clicking another control). */
  protected closed(): void {
    this.open.set(false);
    const active = this.document.activeElement;
    const menu = this.document.getElementById(this.menuId);
    if (!active || active === this.document.body || (menu?.contains(active) ?? false)) {
      this.trigger().nativeElement.focus();
    }
  }
}
