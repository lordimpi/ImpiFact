import {
  ChangeDetectionStrategy,
  Component,
  computed,
  input,
  numberAttribute,
} from '@angular/core';
import { Skeleton } from '@openng/optimus-ui/skeleton';

/**
 * Estado de carga con esqueletos. Anuncia el texto a los lectores de pantalla con `role="status"`
 * y lo oculta a la vista: el esqueleto ya comunica la carga visualmente.
 */
@Component({
  selector: 'ui-loading',
  imports: [Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="ui-loading" role="status" aria-busy="true">
      <span class="ui-visually-hidden">{{ label() }}</span>
      @for (line of lineList(); track line) {
        <p-skeleton aria-hidden="true" [width]="line === lineList().length ? '60%' : '100%'" />
      }
    </div>
  `,
  styles: `
    .ui-loading {
      display: flex;
      flex-direction: column;
      gap: var(--if-space-2);
      padding: var(--if-space-3) 0;
    }
    .ui-visually-hidden {
      position: absolute;
      inline-size: 1px;
      block-size: 1px;
      overflow: hidden;
      clip-path: inset(50%);
      white-space: nowrap;
    }
  `,
})
export class LoadingComponent {
  /** Texto que se anuncia mientras carga (ya traducido). */
  readonly label = input.required<string>();
  /** Número de líneas de esqueleto. */
  readonly lines = input(3, { transform: numberAttribute });

  protected readonly lineList = computed(() =>
    Array.from({ length: Math.max(1, this.lines()) }, (_, index) => index + 1),
  );
}
