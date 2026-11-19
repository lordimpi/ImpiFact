import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  input,
  model,
  output,
} from '@angular/core';
import { Dialog } from '@openng/optimus-ui/dialog';

/**
 * Diálogo modal sobre el de Optimus UI: título como nombre accesible, foco atrapado, cierre con Escape
 * y con el botón de cerrar. El contenido se proyecta; las acciones van en un elemento `uiDialogFooter`.
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
}
