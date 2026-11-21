import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** Whether the trend is good, bad or neither for the business (more rejections up is `negative`). */
export type UiStatTrendTone = 'positive' | 'negative' | 'neutral';

/**
 * KPI tile for the dashboard: a label, a big value and an optional hint and trend. Presentational: every text
 * comes already translated and formatted (numbers and currency with the es-CO locale). It is a description list,
 * so a screen reader reads the label with its value. Place it inside a grid or a `ui-card`.
 *
 * Usage: `<ui-stat [label]="..." [value]="total | number" [hint]="..." [trend]="..." trendTone="positive" />`
 */
@Component({
  selector: 'ui-stat',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <dl class="ui-stat">
      <dt class="ui-stat__label">{{ label() }}</dt>
      <dd class="ui-stat__value">{{ value() }}</dd>
      @if (trend(); as text) {
        <dd class="ui-stat__trend" [class]="'ui-stat__trend--' + trendTone()">
          <span aria-hidden="true">{{ arrow() }}</span> {{ text }}
        </dd>
      }
      @if (hint()) {
        <dd class="ui-stat__hint">{{ hint() }}</dd>
      }
    </dl>
  `,
  styles: `
    :host {
      display: block;
      min-inline-size: 0;
    }
    .ui-stat {
      display: flex;
      flex-direction: column;
      gap: var(--if-space-1);
      block-size: 100%;
      margin: 0;
      padding: var(--if-space-3) var(--if-space-4);
      background: var(--if-color-bg);
      border: 1px solid var(--if-color-border);
      border-radius: var(--if-radius-lg);
      box-shadow: var(--if-shadow-card);
    }
    dd {
      margin: 0;
    }
    .ui-stat__label {
      font-size: var(--if-font-size-sm);
      font-weight: var(--if-font-weight-medium);
      color: var(--if-color-text-muted);
    }
    .ui-stat__value {
      font-size: var(--if-font-size-xl);
      font-weight: var(--if-font-weight-bold);
      line-height: var(--if-line-height-tight);
      font-variant-numeric: tabular-nums;
      color: var(--if-color-text);
      overflow-wrap: anywhere;
    }
    .ui-stat__trend,
    .ui-stat__hint {
      font-size: var(--if-font-size-sm);
      color: var(--if-color-text-muted);
    }
    .ui-stat__trend--positive {
      color: var(--if-color-success);
    }
    .ui-stat__trend--negative {
      color: var(--if-color-danger);
    }
  `,
})
export class StatComponent {
  /** What the value measures (already translated). */
  readonly label = input.required<string>();
  /** Value already formatted for display. */
  readonly value = input.required<string>();
  /** Context under the value, for example the period («Este mes»). */
  readonly hint = input<string>();
  /** Change compared with a previous period, already written («+12 % frente a octubre»). */
  readonly trend = input<string>();
  readonly trendTone = input<UiStatTrendTone>('neutral');
  /** Direction of the arrow before the trend; the text itself carries the meaning. */
  readonly trendDirection = input<'up' | 'down' | 'flat'>('flat');

  protected arrow(): string {
    const direction = this.trendDirection();
    return direction === 'up' ? '↑' : direction === 'down' ? '↓' : '→';
  }
}
