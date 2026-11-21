import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  viewChild,
  input,
  model,
  output,
} from '@angular/core';
import { Dialog } from '@openng/optimus-ui/dialog';

/**
 * Diálogo modal sobre el de Optimus UI: título como nombre accesible, foco atrapado, cierre con Escape
 * y con el botón de cerrar. El contenido se proyecta; las acciones van en un elemento `uiDialogFooter`.
 * Al abrirse, el foco va al primer campo del formulario y no al primer elemento enfocable: ese suele ser el botón de
 * ayuda de la primera etiqueta, y su tooltip se abriría solo.
 *
 * Uso: `<ui-dialog [header]="..." [closeLabel]="..." [(visible)]="open">…<div uiDialogFooter>…</div></ui-dialog>`
 */
@Component({
  selector: 'ui-dialog',
  imports: [Dialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p-dialog
      [header]="header()"
      [visible]="visible()"
      (visibleChange)="visible.set($event)"
      (onHide)="closed.emit()"
      [modal]="modal()"
      [closable]="true"
      [closeOnEscape]="true"
      [dismissableMask]="false"
      [draggable]="false"
      [resizable]="false"
      [closeAriaLabel]="closeLabel()"
      [focusOnShow]="false"
      (onShow)="focusFirstField()"
      [style]="{ width: width() }"
      [breakpoints]="{ '640px': '95vw' }"
    >
      <ng-content />
      <ng-template #footer>
        <ng-content select="[uiDialogFooter]" />
      </ng-template>
    </p-dialog>
  `,
})
export class DialogComponent {
  /** Título del diálogo (ya traducido); también es su nombre accesible. */
  readonly header = input.required<string>();
  /** Nombre accesible del botón de cerrar (ya traducido). */
  readonly closeLabel = input.required<string>();
  /** Si el diálogo está abierto. Admite enlace doble: `[(visible)]`. */
  readonly visible = model(false);
  readonly modal = input(true, { transform: booleanAttribute });
  /** Ancho en pantallas grandes; en móviles ocupa el 95 % del ancho. */
  readonly width = input('32rem');

  /** Se emite cuando el diálogo terminó de cerrarse. */
  readonly closed = output<void>();

  // Optimus may render the dialog under <body> (overlays are appended there), so it is found through the component.
  private readonly dialog = viewChild.required(Dialog);

  /**
   * Focuses the first enabled field of the content; without fields, the first focusable element that is not a help
   * button, then the footer actions, then the close button. Runs after the opening transition, like Optimus does.
   */
  protected focusFirstField(): void {
    setTimeout(() => {
      const dialog = this.dialog().container();
      if (!dialog || dialog.contains(document.activeElement)) {
        return;
      }
      const content = dialog.querySelector<HTMLElement>('.p-dialog-content');
      const notHelp = (element: HTMLElement) => !element.closest('.ui-help');
      const target =
        first(content, FIELDS, notHelp) ??
        first(content, FOCUSABLE, notHelp) ??
        first(dialog.querySelector('.p-dialog-footer'), FOCUSABLE) ??
        first(dialog, FOCUSABLE);
      target?.focus();
    }, OPEN_TRANSITION_MS);
  }
}

function first(
  root: Element | null,
  selector: string,
  accept: (element: HTMLElement) => boolean = () => true,
): HTMLElement | undefined {
  return root ? [...root.querySelectorAll<HTMLElement>(selector)].find(accept) : undefined;
}

/** Form fields that can take the initial focus (Optimus selects and date pickers render a combobox). */
const FIELDS = [
  'input:not([type="hidden"]):not([disabled]):not([readonly])',
  'textarea:not([disabled]):not([readonly])',
  '[role="combobox"]:not([aria-disabled="true"])',
].join(', ');
const FOCUSABLE = ['a[href]', 'button:not([disabled])', '[tabindex]:not([tabindex="-1"])'].join(
  ', ',
);
/** Optimus opens the dialog with a 150 ms transition and focuses after it. */
const OPEN_TRANSITION_MS = 150;
