import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { SessionStore } from '@impifact/core';
import { TranslocoPipe } from '@jsverse/transloco';

/**
 * User menu of the top bar, only with a session: avatar with the initial and the email (the accessible name also
 * says «Mi cuenta»), company switch (with several companies) and log out. The active company is in the top bar.
 * It is a button that shows or hides a panel (*disclosure* pattern, with `aria-expanded`); Escape closes it and returns
 * the focus to the button. It uses native elements so Optimus UI stays out of the initial bundle.
 */
@Component({
  selector: 'app-user-menu',
  imports: [RouterLink, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(keydown.escape)': 'close(true)' },
  template: `
    @if (session.isAuthenticated()) {
      <button
        #toggle
        type="button"
        class="user-menu__toggle"
        aria-controls="user-menu-panel"
        [attr.aria-expanded]="open()"
        (click)="open.set(!open())"
      >
        <span class="user-menu__avatar" aria-hidden="true">{{ initial() }}</span>
        <span class="user-menu__hidden">{{ 'userMenu.account' | transloco }}</span>
        @if (email(); as email) {
          <span class="user-menu__email">{{ email }}</span>
        }
        <svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">
          <path d="M6 9l6 6 6-6" />
        </svg>
      </button>
      @if (open()) {
        <div id="user-menu-panel" class="user-menu__panel">
          @if (canSwitchCompany()) {
            <a routerLink="/empresas/seleccionar" (click)="close(false)">{{
              'userMenu.switchCompany' | transloco
            }}</a>
          }
          <button type="button" (click)="logout()">{{ 'userMenu.logout' | transloco }}</button>
        </div>
      }
    }
  `,
  styles: `
    :host {
      position: relative;
    }
    .user-menu__toggle {
      display: inline-flex;
      align-items: center;
      gap: var(--if-space-2);
      min-block-size: 2.5rem;
      padding: var(--if-space-1) var(--if-space-2);
      font: inherit;
      color: var(--if-color-text);
      background: transparent;
      border: 0;
      border-radius: var(--if-radius-sm);
      cursor: pointer;
    }
    .user-menu__toggle:hover,
    .user-menu__panel :is(a, button):hover {
      background: var(--if-sidebar-hover-bg);
    }
    .user-menu__avatar {
      display: inline-grid;
      place-items: center;
      inline-size: 2rem;
      block-size: 2rem;
      font-weight: var(--if-font-weight-bold);
      color: var(--if-sidebar-active-fg);
      background: var(--if-sidebar-active-bg);
      border-radius: 50%;
    }
    .user-menu__hidden {
      position: absolute;
      inline-size: 1px;
      block-size: 1px;
      overflow: hidden;
      clip-path: inset(50%);
      white-space: nowrap;
    }
    .user-menu__email {
      font-size: var(--if-font-size-sm);
      max-inline-size: 16rem;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    svg {
      inline-size: 1rem;
      block-size: 1rem;
      fill: none;
      stroke: currentColor;
      stroke-width: 2;
    }
    @media (max-width: 40rem) {
      .user-menu__email {
        display: none;
      }
    }
    .user-menu__panel {
      position: absolute;
      inset-inline-end: 0;
      z-index: 10;
      display: flex;
      flex-direction: column;
      min-inline-size: 12rem;
      margin-block-start: var(--if-space-1);
      padding: var(--if-space-1);
      background: var(--if-color-surface-raised);
      border: 1px solid var(--if-color-border);
      border-radius: var(--if-radius-md);
      box-shadow: var(--if-shadow-overlay);
    }
    .user-menu__panel :is(a, button) {
      padding: var(--if-space-2) var(--if-space-3);
      font: inherit;
      text-align: start;
      text-decoration: none;
      color: var(--if-color-text);
      background: transparent;
      border: 0;
      border-radius: var(--if-radius-sm);
      cursor: pointer;
    }
  `,
})
export class UserMenuComponent {
  protected readonly session = inject(SessionStore);
  private readonly toggle = viewChild<ElementRef<HTMLButtonElement>>('toggle');

  protected readonly open = signal(false);
  protected readonly email = computed(() => this.session.user()?.email ?? null);
  protected readonly initial = computed(() => (this.email()?.charAt(0) ?? '?').toUpperCase());
  protected readonly canSwitchCompany = computed(() => this.session.companies().length > 1);

  protected close(returnFocus: boolean): void {
    if (!this.open()) {
      return;
    }
    this.open.set(false);
    if (returnFocus) {
      this.toggle()?.nativeElement.focus();
    }
  }

  protected logout(): void {
    this.open.set(false);
    void this.session.logout();
  }
}
