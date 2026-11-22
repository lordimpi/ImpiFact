import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { DocumentEventResponse } from '@impifact/api';
import { BadgeComponent, UiBadgeTone } from '@impifact/ui';
import { TranslocoPipe } from '@jsverse/transloco';
import { eventNameKey } from '../models/radian-events';
import { ColombiaTimePipe } from './colombia-time.pipe';

const STATUS_TONES: Record<string, UiBadgeTone> = {
  accepted: 'success',
  rejected: 'danger',
  unconfirmed: 'warning',
};

const STATUSES = ['accepted', 'rejected', 'unconfirmed'];

/**
 * History of the RADIAN events of an invoice, oldest first (presentation): name and code, status in the DIAN (text and
 * color), date in Colombia, the DIAN message and, for a rejected event, the rules the DIAN gave.
 */
@Component({
  selector: 'feat-radian-timeline',
  imports: [TranslocoPipe, BadgeComponent, ColombiaTimePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (events().length === 0) {
      <p class="timeline__empty">{{ emptyKey() | transloco }}</p>
    } @else {
      <ol class="timeline" [attr.aria-label]="'documents.radian.timelineLabel' | transloco">
        @for (event of events(); track event.id) {
          <li class="timeline__item" [class]="'timeline__item--' + statusOf(event.status)">
            <div class="timeline__head">
              <span class="timeline__name">
                {{
                  'documents.' + nameKey(event.event_code) | transloco: { code: event.event_code }
                }}
                <span class="timeline__code">({{ event.event_code }})</span>
              </span>
              <ui-badge [tone]="toneOf(event.status)">{{
                'documents.radian.statuses.' + statusOf(event.status) | transloco
              }}</ui-badge>
            </div>
            <p class="timeline__meta">
              <time [attr.datetime]="event.issued_at">{{ event.issued_at | colombiaTime }}</time>
              @if (event.number) {
                <span>· {{ 'documents.radian.number' | transloco: { number: event.number } }}</span>
              }
              @if (event.claim_reason) {
                <span
                  >·
                  {{
                    'documents.radian.claimReason'
                      | transloco
                        : {
                            reason:
                              ('documents.radian.claimReasons.' + event.claim_reason | transloco),
                          }
                  }}</span
                >
              }
            </p>
            @if (event.dian_status_description) {
              <p class="timeline__message">{{ event.dian_status_description }}</p>
            }
            @if (event.error_messages?.length) {
              <div class="timeline__errors">
                <p>{{ 'documents.radian.errors' | transloco }}</p>
                <ul>
                  @for (message of event.error_messages; track $index) {
                    <li>{{ message }}</li>
                  }
                </ul>
              </div>
            }
          </li>
        }
      </ol>
    }
  `,
  styles: `
    :host {
      display: block;
    }
    .timeline {
      display: flex;
      flex-direction: column;
      gap: var(--if-space-3);
      margin: 0;
      padding: 0;
      list-style: none;
    }
    .timeline__item {
      position: relative;
      padding-inline-start: var(--if-space-4);
      border-inline-start: 3px solid var(--if-color-border-strong);
    }
    .timeline__item--accepted {
      border-inline-start-color: var(--if-color-success);
    }
    .timeline__item--rejected {
      border-inline-start-color: var(--if-color-danger);
    }
    .timeline__item--unconfirmed {
      border-inline-start-color: var(--if-color-warning);
    }
    .timeline__head {
      display: flex;
      flex-wrap: wrap;
      gap: var(--if-space-1) var(--if-space-2);
      align-items: center;
    }
    .timeline__name {
      font-weight: var(--if-font-weight-medium);
    }
    .timeline__code,
    .timeline__meta,
    .timeline__empty {
      color: var(--if-color-text-muted);
    }
    .timeline__meta {
      display: flex;
      flex-wrap: wrap;
      gap: 0 var(--if-space-1);
      margin: var(--if-space-1) 0 0;
      font-size: var(--if-font-size-sm);
    }
    .timeline__empty,
    .timeline__message {
      margin: var(--if-space-1) 0 0;
    }
    .timeline__empty {
      margin: 0;
    }
    .timeline__errors {
      margin-block-start: var(--if-space-2);
      padding: var(--if-space-2) var(--if-space-3);
      color: var(--if-color-danger);
      background: var(--if-color-danger-subtle);
      border-radius: var(--if-radius-sm);
      font-size: var(--if-font-size-sm);
    }
    .timeline__errors p,
    .timeline__errors ul {
      margin: 0;
    }
    .timeline__errors ul {
      padding-inline-start: var(--if-space-4);
    }
  `,
})
export class RadianTimelineComponent {
  readonly events = input<readonly DocumentEventResponse[]>([]);
  /** Text key shown when there are no events. */
  readonly emptyKey = input('documents.radian.noEvents');

  protected readonly nameKey = eventNameKey;

  protected statusOf(status: string | null | undefined): string {
    return STATUSES.includes(status ?? '') ? (status as string) : 'unknown';
  }

  protected toneOf(status: string | null | undefined): UiBadgeTone {
    return STATUS_TONES[status ?? ''] ?? 'neutral';
  }
}
