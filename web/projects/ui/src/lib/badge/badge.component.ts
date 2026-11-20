import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** Badge color: `neutral` for drafts or plain labels, the rest for statuses (accepted, pending, rejected…). */
export type UiBadgeTone = 'neutral' | 'info' | 'success' | 'warning' | 'danger';

/**
 * Small pill for a status, environment or plan. Presentational: the text comes projected and translated.
 * The color is never the only cue: the text names the status. Every tone keeps its text ≥ 4.5:1 on its background.
 *
 * Usage: `<ui-badge tone="success">{{ 'documents.status.accepted' | transloco }}</ui-badge>`
 */
@Component({
  selector: 'ui-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[attr.data-tone]': 'tone()' },
  template: `<span class="ui-badge__dot" aria-hidden="true"></span><ng-content />`,
  styles: `
    :host {
      --ui-badge-fg: var(--if-color-text);
      --ui-badge-bg: var(--if-color-neutral-subtle);
      --ui-badge-dot: var(--if-color-text-muted);
      display: inline-flex;
      align-items: center;
      gap: 0.375rem;
      max-inline-size: 100%;
      padding: 0.125rem var(--if-space-2);
      font-size: var(--if-font-size-xs);
      font-weight: var(--if-font-weight-medium);
      line-height: 1.25rem;
      white-space: nowrap;
      color: var(--ui-badge-fg);
      background: var(--ui-badge-bg);
      border-radius: var(--if-radius-pill);
    }
    :host([data-tone='info']) {
      --ui-badge-fg: var(--if-color-info);
      --ui-badge-bg: var(--if-color-info-subtle);
      --ui-badge-dot: var(--if-color-info);
    }
    :host([data-tone='success']) {
      --ui-badge-fg: var(--if-color-success);
      --ui-badge-bg: var(--if-color-success-subtle);
      --ui-badge-dot: var(--if-color-success);
    }
    :host([data-tone='warning']) {
      --ui-badge-fg: var(--if-color-warning);
      --ui-badge-bg: var(--if-color-warning-subtle);
      --ui-badge-dot: var(--if-color-warning);
    }
    :host([data-tone='danger']) {
      --ui-badge-fg: var(--if-color-danger);
      --ui-badge-bg: var(--if-color-danger-subtle);
      --ui-badge-dot: var(--if-color-danger);
    }
    .ui-badge__dot {
      flex: none;
      inline-size: 0.375rem;
      block-size: 0.375rem;
      background: var(--ui-badge-dot);
      border-radius: 50%;
    }
  `,
})
export class BadgeComponent {
  readonly tone = input<UiBadgeTone>('neutral');
}
