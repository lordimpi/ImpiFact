import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  inject,
  input,
  output,
} from '@angular/core';
import { DianEventListResponse, DianEventResponse } from '@impifact/api';
import {
  ButtonComponent,
  TableCellDirective,
  TableComponent,
  UiTableColumn,
  UiTableLabels,
} from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { DOCUMENTS_I18N_SCOPE } from '../documents-i18n';
import { eventNameKey } from '../models/radian-events';

/**
 * «Consultar en la DIAN» and what the DIAN answers (presentation): the events it has registered on the invoice, from
 * every participant. The page asks the API when `consult` is emitted and passes the answer in `result`.
 */
@Component({
  selector: 'feat-dian-events',
  imports: [TranslocoPipe, ButtonComponent, TableComponent, TableCellDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="dian-events">
      <div class="dian-events__head">
        <p class="dian-events__help">{{ 'documents.radian.dian.help' | transloco }}</p>
        <ui-button
          variant="secondary"
          size="small"
          [label]="'documents.radian.dian.consult' | transloco"
          [loading]="busy()"
          (pressed)="consult.emit()"
        />
      </div>
      @if (result(); as answer) {
        <div role="status" class="dian-events__result">
          @if (answer.events.length === 0) {
            <p class="dian-events__none">{{ 'documents.radian.dian.none' | transloco }}</p>
          } @else {
            <ui-table
              flush
              [caption]="'documents.radian.dian.caption' | transloco"
              [columns]="columns()"
              [rows]="answer.events"
              [labels]="labels()"
              [trackBy]="trackEvent"
            >
              <ng-template uiTableCell="dian_code" let-event>
                <span class="dian-events__code">{{ event.dian_code }}</span>
              </ng-template>
              <ng-template uiTableCell="description" let-event>
                {{
                  event.description ||
                    ('documents.' + nameKey(event.dian_code) | transloco: { code: event.dian_code })
                }}
              </ng-template>
              <ng-template uiTableCell="date" let-event>
                <span class="dian-events__nowrap"
                  >{{ event.date }} {{ event.time?.slice(0, 5) }}</span
                >
              </ng-template>
            </ui-table>
          }
        </div>
      }
    </div>
  `,
  styles: `
    .dian-events__head {
      display: flex;
      flex-wrap: wrap;
      gap: var(--if-space-2) var(--if-space-3);
      align-items: center;
      justify-content: space-between;
    }
    .dian-events__help,
    .dian-events__none {
      margin: 0;
      color: var(--if-color-text-muted);
      font-size: var(--if-font-size-sm);
    }
    .dian-events__help {
      flex: 1 1 16rem;
    }
    .dian-events__result {
      margin-block-start: var(--if-space-3);
    }
    .dian-events__code {
      font-family: var(--if-font-family-mono);
    }
    .dian-events__nowrap {
      white-space: nowrap;
    }
  `,
})
export class DianEventsComponent {
  private readonly transloco = inject(TranslocoService);

  /** Last answer of the DIAN; `null` until the person asks. */
  readonly result = input<DianEventListResponse | null>(null);
  readonly busy = input(false, { transform: booleanAttribute });
  readonly consult = output<void>();

  protected readonly nameKey = eventNameKey;
  protected readonly trackEvent = (index: number, event: DianEventResponse) =>
    event.cude ?? `${event.dian_code}-${index}`;

  protected readonly columns = computed<UiTableColumn[]>(() => [
    { key: 'dian_code', header: this.text('radian.dian.columns.code') },
    { key: 'description', header: this.text('radian.dian.columns.description') },
    { key: 'date', header: this.text('radian.dian.columns.date') },
    { key: 'event_number', header: this.text('radian.dian.columns.number') },
  ]);

  protected readonly labels = computed<UiTableLabels>(() => ({
    loading: this.text('radian.dian.consult'),
    emptyTitle: this.text('radian.dian.none'),
    errorTitle: this.text('radian.dian.unavailable'),
    retry: this.transloco.translate('ui.retry'),
    paginator: { navigation: '', previous: '', next: '', page: '' },
  }));

  private text(key: string): string {
    return this.transloco.translate(`${DOCUMENTS_I18N_SCOPE}.${key}`);
  }
}
