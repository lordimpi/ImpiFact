import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { ButtonComponent } from '../button/button.component';

/**
 * Estado de error con acción de reintento. Usa `role="alert"` para que el lector de pantalla lo anuncie.
 * El botón aparece solo si se da `retryLabel`.
 */
@Component({
  selector: 'ui-error-state',
  imports: [ButtonComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="ui-error-state" role="alert">
      <p class="ui-error-state__title">{{ title() }}</p>
      @if (description()) {
        <p class="ui-error-state__description">{{ description() }}</p>
      }
      @if (retryLabel(); as retryText) {
        <ui-button variant="secondary" [label]="retryText" (pressed)="retry.emit()" />
      }
    </div>
  `,
  styles: `
    .ui-error-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: var(--if-space-2);
      padding: var(--if-space-5) var(--if-space-3);
      text-align: center;
    }
    .ui-error-state__title {
      margin: 0;
      font-size: var(--if-font-size-md);
      font-weight: var(--if-font-weight-bold);
      color: var(--if-color-danger);
    }
    .ui-error-state__description {
      margin: 0;
      color: var(--if-color-text-muted);
    }
  `,
})
export class ErrorStateComponent {
  readonly title = input.required<string>();
  readonly description = input<string>();
  /** Texto del botón de reintento; sin él no hay botón. */
  readonly retryLabel = input<string>();

  readonly retry = output<void>();
}
