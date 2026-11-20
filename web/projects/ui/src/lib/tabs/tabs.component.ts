import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { IsActiveMatchOptions, Params, RouterLink, RouterLinkActive } from '@angular/router';

/** One routed tab: its translated label and where it navigates. */
export interface UiTab {
  /** Visible text (already translated). */
  label: string;
  /** `routerLink` value: a URL or a commands array. */
  link: string | readonly unknown[];
  /** Query parameters of the link; tabs that share a path are told apart by them. */
  queryParams?: Params;
  /** Active only on this exact path (default); `false` keeps it active on child routes too. */
  exact?: boolean;
}

/**
 * Routed tabs: a labelled `<nav>` with one link per section of a page (it is navigation, not the ARIA tab widget,
 * so each tab has its own URL and works with the browser history). The active link gets `aria-current="page"`.
 * On narrow screens the row scrolls horizontally instead of wrapping.
 *
 * Usage: `<ui-tabs [label]="'company.sections' | transloco" [tabs]="tabs()" />`, with a `<router-outlet />` below.
 */
@Component({
  selector: 'ui-tabs',
  imports: [RouterLink, RouterLinkActive],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nav class="ui-tabs" [attr.aria-label]="label()">
      <ul class="ui-tabs__list">
        @for (tab of items(); track tab.label) {
          <li>
            <a
              class="ui-tabs__link"
              [routerLink]="tab.link"
              [queryParams]="tab.queryParams ?? null"
              routerLinkActive="ui-tabs__link--active"
              ariaCurrentWhenActive="page"
              [routerLinkActiveOptions]="tab.matchOptions"
              >{{ tab.label }}</a
            >
          </li>
        }
      </ul>
    </nav>
  `,
  styles: `
    :host {
      display: block;
      min-inline-size: 0;
    }
    .ui-tabs {
      overflow-x: auto;
      border-block-end: 1px solid var(--if-color-border);
      scrollbar-width: thin;
    }
    .ui-tabs__list {
      display: flex;
      gap: var(--if-space-1);
      margin: 0;
      padding: 0 var(--if-space-1);
      list-style: none;
    }
    .ui-tabs__link {
      position: relative;
      display: inline-flex;
      align-items: center;
      min-block-size: 2.75rem;
      padding: 0 var(--if-space-3);
      font-size: var(--if-font-size-sm);
      font-weight: var(--if-font-weight-medium);
      white-space: nowrap;
      color: var(--if-color-text-muted);
      text-decoration: none;
      border-radius: var(--if-radius-sm) var(--if-radius-sm) 0 0;
    }
    .ui-tabs__link:hover {
      color: var(--if-color-text);
      background: var(--if-color-surface-raised);
    }
    .ui-tabs__link:focus-visible {
      outline: var(--if-focus-ring-width) solid var(--if-color-focus);
      outline-offset: calc(-1 * var(--if-focus-ring-width));
    }
    .ui-tabs__link--active {
      color: var(--if-color-primary);
    }
    .ui-tabs__link--active::after {
      content: '';
      position: absolute;
      inset-inline: var(--if-space-2);
      inset-block-end: -1px;
      block-size: 2px;
      background: var(--if-color-primary);
      border-radius: 2px;
    }
    .ui-tabs__link--active:hover {
      color: var(--if-color-primary);
    }
  `,
})
export class TabsComponent {
  /** Accessible name of the navigation (already translated), for example «Secciones de la empresa». */
  readonly label = input.required<string>();
  readonly tabs = input.required<readonly UiTab[]>();

  /** Tabs with their match options, built once per input change (not on every change detection). */
  protected readonly items = computed(() =>
    this.tabs().map((tab) => ({ ...tab, matchOptions: matchOptions(tab) })),
  );
}

function matchOptions(tab: UiTab): IsActiveMatchOptions {
  return {
    paths: tab.exact === false ? 'subset' : 'exact',
    queryParams: tab.queryParams ? 'subset' : 'ignored',
    fragment: 'ignored',
    matrixParams: 'ignored',
  };
}
