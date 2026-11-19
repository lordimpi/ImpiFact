import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  effect,
  input,
  model,
  output,
  signal,
} from '@angular/core';
import { ButtonComponent, DialogComponent } from '@impifact/ui';

/** Textos del diálogo, ya traducidos. */
export interface ProductionConfirmLabels {
  header: string;
  body: string;
  accept: string;
  confirm: string;
  cancel: string;
  close: string;
}

/**
 * Confirmación del paso a producción (componente de presentación). El botón de confirmar solo se habilita después de
 * marcar que se entiende el efecto fiscal real; emite `confirmed` y quien lo usa llama al Api y lo cierra.
 */
@Component({
  selector: 'feat-production-confirm-dialog',
  imports: [ButtonComponent, DialogComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-dialog [header]="labels().header" [closeLabel]="labels().close" [(visible)]="visible">
      <p>{{ labels().body }}</p>
      <label class="confirm-accept">
        <input
          type="checkbox"
          [checked]="accepted()"
          (change)="accepted.set($any($event.target).checked)"
        />
        {{ labels().accept }}
      </label>
      <div uiDialogFooter class="confirm-actions">
        <ui-button variant="secondary" [label]="labels().cancel" (pressed)="visible.set(false)" />
        <ui-button
          variant="danger"
          [label]="labels().confirm"
          [disabled]="!accepted()"
          [loading]="busy()"
          (pressed)="confirmed.emit()"
        />
      </div>
    </ui-dialog>
  `,
  styles: `
    .confirm-accept {
      display: flex;
      gap: var(--if-space-2);
      align-items: flex-start;
      font-weight: var(--if-font-weight-medium);
    }
    .confirm-actions {
      display: flex;
      justify-content: flex-end;
      gap: var(--if-space-2);
    }
  `,
})
export class ProductionConfirmDialogComponent {
  readonly labels = input.required<ProductionConfirmLabels>();
  readonly visible = model(false);
  readonly busy = input(false, { transform: booleanAttribute });
  readonly confirmed = output<void>();

  protected readonly accepted = signal(false);

  constructor() {
    // Cada vez que se abre, la casilla empieza sin marcar.
    effect(() => {
      if (this.visible()) {
        this.accepted.set(false);
      }
    });
  }
}
