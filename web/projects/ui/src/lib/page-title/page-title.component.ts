import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Título principal de una página. Componente de presentación: recibe el texto ya traducido por proyección.
 *
 * Page header layout: an optional breadcrumb on top, then the level 1 heading with an optional subtitle and, on the
 * same row, the page actions; on narrow screens the actions wrap under the title.
 *
 * Usage:
 * `<ui-page-title [subtitle]="...">{{ title }}<ui-button uiPageTitleActions ... /></ui-page-title>`
 * (`uiPageTitleBreadcrumb` marks the breadcrumb element).
 */
@Component({
  selector: 'ui-page-title',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="ui-page-title">
      <div class="ui-page-title__breadcrumb">
        <ng-content select="[uiPageTitleBreadcrumb]" />
      </div>
      <div class="ui-page-title__row">
        <div class="ui-page-title__text">
          <h1 class="ui-page-title__heading"><ng-content /></h1>
          @if (subtitle()) {
            <p class="ui-page-title__subtitle">{{ subtitle() }}</p>
          }
        </div>
        <div class="ui-page-title__actions">
          <ng-content select="[uiPageTitleActions]" />
        </div>
      </div>
    </div>
  `,
  styles: `
    :host {
      display: block;
    }
    .ui-page-title {
      display: flex;
      flex-direction: column;
      gap: var(--if-space-1);
      margin-block-end: var(--if-space-4);
    }
    .ui-page-title__breadcrumb:empty,
    .ui-page-title__actions:empty {
      display: none;
    }
    .ui-page-title__breadcrumb {
      font-size: var(--if-font-size-sm);
      color: var(--if-color-text-muted);
    }
    .ui-page-title__row {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-end;
      justify-content: space-between;
      gap: var(--if-space-3);
    }
    .ui-page-title__text {
      display: flex;
      flex-direction: column;
      gap: var(--if-space-1);
      min-inline-size: min(100%, 16rem);
    }
    .ui-page-title__heading {
      margin: 0;
      font-size: var(--if-font-size-lg);
      font-weight: var(--if-font-weight-bold);
      line-height: var(--if-line-height-tight);
      letter-spacing: -0.01em;
      color: var(--if-color-text);
    }
    .ui-page-title__subtitle {
      margin: 0;
      color: var(--if-color-text-muted);
    }
    .ui-page-title__actions {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--if-space-2);
    }
  `,
})
export class PageTitleComponent {
  /** Line under the title (already translated) that explains the page. */
  readonly subtitle = input<string>();
}
