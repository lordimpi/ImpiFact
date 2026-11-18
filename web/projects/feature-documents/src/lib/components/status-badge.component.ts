import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { statusKey, statusTone } from '../models/document-status';

/** Estado del documento en la DIAN, con texto y borde de color (el color nunca es la única señal). */
@Component({
  selector: 'feat-status-badge',
  imports: [TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="status-badge" [attr.data-tone]="tone()">{{
    'documents.' + key() | transloco
  }}</span>`,
  styles: `
    .status-badge {
      display: inline-block;
      padding: 0 var(--if-space-2);
      border: 1px solid currentColor;
      border-radius: var(--if-radius-sm);
      font-size: var(--if-font-size-sm);
      white-space: nowrap;
      color: var(--if-color-text-muted);
    }
    .status-badge[data-tone='success'] {
      color: var(--if-color-success);
    }
    .status-badge[data-tone='danger'] {
      color: var(--if-color-danger);
    }
  `,
})
export class StatusBadgeComponent {
  readonly status = input<string | null | undefined>();
  protected readonly key = computed(() => statusKey(this.status()));
  protected readonly tone = computed(() => statusTone(this.status()));
}
