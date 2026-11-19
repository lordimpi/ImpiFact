import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  input,
  model,
  output,
} from '@angular/core';
import { ButtonComponent, DialogComponent } from '@impifact/ui';

/** Textos del diálogo, ya traducidos. */
export interface ConfirmDialogLabels {
  header: string;
  body: string;
  confirm: string;
  cancel: string;
  close: string;
}

/**
 * Confirmación de una acción sobre un registro (componente de presentación): emite `confirmed` y quien lo usa hace
 * la llamada al Api y lo cierra al terminar. Mientras `busy`, el botón de confirmar muestra la carga.
 */
@Component({
  selector: 'feat-confirm-dialog',
  imports: [ButtonComponent, DialogComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-dialog [header]="labels().header" [closeLabel]="labels().close" [(visible)]="visible">
      <p>{{ labels().body }}</p>
      <div uiDialogFooter class="confirm-actions">
        <ui-button variant="secondary" [label]="labels().cancel" (pressed)="visible.set(false)" />
        <ui-button
          variant="danger"
          [label]="labels().confirm"
          [loading]="busy()"
          (pressed)="confirmed.emit()"
        />
      </div>
    </ui-dialog>
  `,
  styles: `
    .confirm-actions {
      display: flex;
      justify-content: flex-end;
      gap: var(--if-space-2);
    }
  `,
})
export class ConfirmDialogComponent {
  readonly labels = input.required<ConfirmDialogLabels>();
  readonly visible = model(false);
  readonly busy = input(false, { transform: booleanAttribute });
  readonly confirmed = output<void>();
}
