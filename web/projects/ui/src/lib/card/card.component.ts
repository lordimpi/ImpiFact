import {
  ChangeDetectionStrategy,
  Component,
  Directive,
  computed,
  contentChild,
  input,
  numberAttribute,
} from '@angular/core';

/** Marks the element projected into the card header, next to the title (buttons, links, filters). */
@Directive({ selector: '[uiCardActions]' })
export class CardActionsDirective {}

/** Marks the element projected into the card footer (totals, secondary links, form buttons). */
@Directive({ selector: '[uiCardFooter]' })
export class CardFooterDirective {}

/** Body padding: `none` lets a table or list touch the card edges (use it with `ui-table flush`). */
export type UiCardPadding = 'normal' | 'compact' | 'none';

/**
 * Card: white surface with a border, a radius and a soft shadow on the page background. Presentational.
 * Optional header with a title (translated text, heading level 2 by default), a subtitle and an actions slot;
 * the content goes in the body; an optional footer.
 *
 * Usage:
 * `<ui-card [title]="..." [subtitle]="..."><ui-button uiCardActions ... /> ...content... <div uiCardFooter>…</div></ui-card>`
 */
@Component({
  selector: 'ui-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="ui-card" [class]="'ui-card--padding-' + padding()">
      @if (hasHeader()) {
        <div class="ui-card__header">
          @if (title() || subtitle()) {
            <div class="ui-card__titles">
              @if (title(); as text) {
                @switch (headingLevel()) {
                  @case (3) {
                    <h3 class="ui-card__title">{{ text }}</h3>
                  }
                  @case (4) {
                    <h4 class="ui-card__title">{{ text }}</h4>
                  }
                  @default {
                    <h2 class="ui-card__title">{{ text }}</h2>
                  }
                }
              }
              @if (subtitle()) {
                <p class="ui-card__subtitle">{{ subtitle() }}</p>
              }
            </div>
          }
          @if (actions()) {
            <div class="ui-card__actions"><ng-content select="[uiCardActions]" /></div>
          }
        </div>
      }
      <div class="ui-card__body"><ng-content /></div>
      @if (footer()) {
        <div class="ui-card__footer"><ng-content select="[uiCardFooter]" /></div>
      }
    </div>
  `,
  styles: `
    :host {
      display: block;
      min-inline-size: 0;
    }
    .ui-card {
      block-size: 100%;
      overflow: hidden;
      color: var(--if-color-text);
      background: var(--if-color-bg);
      border: 1px solid var(--if-color-border);
      border-radius: var(--if-radius-lg);
      box-shadow: var(--if-shadow-card);
    }
    .ui-card__header {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: var(--if-space-2) var(--if-space-3);
      padding: var(--if-space-3) var(--if-space-4);
      border-block-end: 1px solid var(--if-color-border);
    }
    .ui-card__titles {
      display: flex;
      flex-direction: column;
      gap: 0.125rem;
      min-inline-size: 0;
    }
    .ui-card__title {
      margin: 0;
      font-size: var(--if-font-size-base);
      font-weight: var(--if-font-weight-bold);
      line-height: var(--if-line-height-tight);
    }
    .ui-card__subtitle {
      margin: 0;
      font-size: var(--if-font-size-sm);
      color: var(--if-color-text-muted);
    }
    .ui-card__actions {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--if-space-2);
      margin-inline-start: auto;
    }
    .ui-card__body {
      padding: var(--if-space-4);
    }
    .ui-card--padding-compact .ui-card__body {
      padding: var(--if-space-3);
    }
    .ui-card--padding-none .ui-card__body {
      padding: 0;
    }
    .ui-card__footer {
      padding: var(--if-space-3) var(--if-space-4);
      background: var(--if-color-surface);
      border-block-start: 1px solid var(--if-color-border);
    }
    @media (max-width: 40rem) {
      .ui-card__header,
      .ui-card__body,
      .ui-card__footer {
        padding-inline: var(--if-space-3);
      }
    }
  `,
})
export class CardComponent {
  /** Card title (already translated); without it the header shows only the actions, if any. */
  readonly title = input<string>();
  /** Secondary line under the title (already translated). */
  readonly subtitle = input<string>();
  /** Heading level of the title, to keep the page outline: 2 by default, 3 or 4 inside sections. */
  readonly headingLevel = input(2, { transform: numberAttribute });
  readonly padding = input<UiCardPadding>('normal');

  protected readonly actions = contentChild(CardActionsDirective);
  protected readonly footer = contentChild(CardFooterDirective);

  protected readonly hasHeader = computed(
    () => !!this.title() || !!this.subtitle() || !!this.actions(),
  );
}
