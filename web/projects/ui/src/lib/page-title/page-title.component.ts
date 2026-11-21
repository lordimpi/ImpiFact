import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MenuItem } from '@openng/optimus-ui/api';
import { Breadcrumb } from '@openng/optimus-ui/breadcrumb';
import { UI_TEXTS } from '../i18n/ui-texts';
import { UiBreadcrumbItem } from './breadcrumb';

/**
 * Título principal de una página. Componente de presentación: recibe el texto ya traducido por proyección.
 *
 * Page header layout: an optional breadcrumb on top (the Optimus UI `p-breadcrumb`, in a navigation named by
 * `UI_TEXTS.breadcrumb`), then the level 1 heading with an optional subtitle and, on the same row, the page actions;
 * on narrow screens the actions wrap under the title.
 *
 * Usage:
 * `<ui-page-title [subtitle]="..." [breadcrumb]="[{ label: 'Productos', link: '/productos' }, { label: 'Nuevo' }]">`
 * `{{ title }}<ui-button uiPageTitleActions ... /></ui-page-title>`
 */
@Component({
  selector: 'ui-page-title',
  imports: [Breadcrumb, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="ui-page-title">
      @if (model().length) {
        <p-breadcrumb class="ui-page-title__breadcrumb" [model]="model()" [pt]="breadcrumbPt()">
          <ng-template #item let-item>
            @if (item.routerLink) {
              <a class="ui-page-title__crumb-link" [routerLink]="item.routerLink">{{
                item.label
              }}</a>
            } @else {
              <span class="ui-page-title__crumb-current" aria-current="page">{{ item.label }}</span>
            }
          </ng-template>
        </p-breadcrumb>
      }
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
    .ui-page-title__actions:empty {
      display: none;
    }
    .ui-page-title__breadcrumb {
      display: block;
      font-size: var(--if-font-size-base);
    }
    .ui-page-title__crumb-link {
      color: var(--if-color-primary);
      font-weight: var(--if-font-weight-medium);
      text-decoration: underline;
      text-decoration-thickness: 1px;
      text-underline-offset: 0.2em;
      border-radius: var(--if-radius-sm);
    }
    .ui-page-title__crumb-link:hover {
      color: var(--if-color-primary-hover);
      text-decoration-thickness: 2px;
    }
    .ui-page-title__crumb-link:focus-visible {
      outline: var(--if-focus-ring-width) solid var(--if-color-focus);
      outline-offset: var(--if-focus-ring-offset);
    }
    .ui-page-title__crumb-current {
      color: var(--if-color-text);
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
  /** Steps from a section to this page; the last one is the current page. Without steps there is no breadcrumb. */
  readonly breadcrumb = input<readonly UiBreadcrumbItem[]>([]);

  private readonly texts = inject(UI_TEXTS);

  protected readonly model = computed<MenuItem[]>(() =>
    this.breadcrumb().map((step, index, steps) => ({
      label: step.label,
      // The last step is the current page: never a link, even if one was given.
      routerLink: index < steps.length - 1 ? step.link : undefined,
    })),
  );

  /** Names the navigation landmark Optimus renders. */
  protected readonly breadcrumbPt = computed(() => ({
    root: { 'aria-label': this.texts.breadcrumb },
  }));
}
