import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { BadgeComponent, UiBadgeTone } from '@impifact/ui';
import { TranslocoPipe } from '@jsverse/transloco';
import { statusKey } from '../models/document-status';

/** Badge tone of each DIAN status: final answers in green or red, waits in blue, cases to review in amber. */
const STATUS_TONES: Record<string, UiBadgeTone> = {
  Accepted: 'success',
  AcceptedWithWarnings: 'warning',
  Rejected: 'danger',
  Failed: 'danger',
  Received: 'info',
  Queued: 'info',
  Sent: 'info',
  Unconfirmed: 'warning',
  ContingencyPending: 'warning',
};

/** Estado del documento en la DIAN, con texto y color (el color nunca es la única señal). */
@Component({
  selector: 'feat-status-badge',
  imports: [TranslocoPipe, BadgeComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<ui-badge [tone]="tone()">{{ 'documents.' + key() | transloco }}</ui-badge>`,
})
export class StatusBadgeComponent {
  readonly status = input<string | null | undefined>();
  protected readonly key = computed(() => statusKey(this.status()));
  protected readonly tone = computed<UiBadgeTone>(
    () => STATUS_TONES[this.status() ?? ''] ?? 'neutral',
  );
}
