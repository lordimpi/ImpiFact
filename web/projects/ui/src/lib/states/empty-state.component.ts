import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** Estado vacío de una lista o pantalla: título, descripción y una acción opcional proyectada. */
@Component({
  selector: 'ui-empty-state',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="ui-empty-state">
      <p class="ui-empty-state__title">{{ title() }}</p>
      @if (description()) {
        <p class="ui-empty-state__description">{{ description() }}</p>
      }
      <ng-content />
    </div>
  `,
  styles: `
    .ui-empty-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: var(--if-space-2);
      padding: var(--if-space-5) var(--if-space-3);
      text-align: center;
    }
    .ui-empty-state__title {
      margin: 0;
      font-size: var(--if-font-size-md);
      font-weight: var(--if-font-weight-bold);
      color: var(--if-color-text);
    }
    .ui-empty-state__description {
      margin: 0;
      color: var(--if-color-text-muted);
    }
  `,
})
export class EmptyStateComponent {
  readonly title = input.required<string>();
  readonly description = input<string>();
}
