import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  inject,
  input,
  output,
  viewChild,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { MyCompaniesStore, SessionStore } from '@impifact/core';
import { PLATFORM_SCOPE } from '@impifact/feature-platform';
import { TranslocoPipe } from '@jsverse/transloco';
import { NotificationBellComponent } from './notification-bell.component';
import { ThemeStore } from './theme.store';
import { UserMenuComponent } from './user-menu.component';

/**
 * Top bar of the panel: menu button (below 1024px it opens the sidebar drawer; from 1024px a second one hides and
 * shows the fixed sidebar), the active company (a link to the company selection when the user has several), the
 * light/dark theme switch, the manual, the notification bell and the user menu.
 */
@Component({
  selector: 'app-topbar',
  imports: [RouterLink, TranslocoPipe, NotificationBellComponent, UserMenuComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { role: 'banner' },
  template: `
    <button
      #menuToggle
      type="button"
      class="topbar__menu"
      aria-controls="app-sidebar"
      [attr.aria-expanded]="menuOpen()"
      [attr.aria-label]="'app.openMenu' | transloco"
      (click)="toggleMenu.emit()"
    >
      <svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">
        <path d="M4 6h16M4 12h16M4 18h16" />
      </svg>
    </button>
    <button
      type="button"
      class="topbar__menu topbar__menu--wide"
      aria-controls="app-sidebar"
      [attr.aria-expanded]="!sidebarHidden()"
      [attr.aria-label]="(sidebarHidden() ? 'app.showMenu' : 'app.hideMenu') | transloco"
      (click)="toggleSidebar.emit()"
    >
      <svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">
        <path d="M4 6h16M4 12h16M4 18h16" />
      </svg>
    </button>
    @if (companyName(); as name) {
      @if (canSwitchCompany()) {
        <a
          class="topbar__company"
          routerLink="/empresas/seleccionar"
          [attr.aria-label]="'topbar.switchCompany' | transloco: { name }"
        >
          <span class="topbar__company-name">{{ name }}</span>
          <svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">
            <path d="M6 9l6 6 6-6" />
          </svg>
        </a>
      } @else {
        <span class="topbar__company">
          <span class="topbar__company-name">{{ name }}</span>
        </span>
      }
    } @else if (platform()) {
      <span class="topbar__company">{{ 'topbar.platform' | transloco }}</span>
    }
    <div class="topbar__actions">
      <!-- The name says what a press does; the icon shows the theme it switches to. -->
      <button
        type="button"
        class="topbar__icon-button"
        [attr.aria-label]="(dark() ? 'topbar.lightMode' : 'topbar.darkMode') | transloco"
        (click)="themeStore.toggle()"
      >
        <svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">
          @if (dark()) {
            <circle cx="12" cy="12" r="4" />
            <path
              d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"
            />
          } @else {
            <path d="M20 14.5A8 8 0 1 1 9.5 4a6.5 6.5 0 0 0 10.5 10.5z" />
          }
        </svg>
      </button>
      <!-- The manual is a static PDF built by \`npm run manual\`; it opens in a new tab so the panel keeps its state. -->
      <a
        class="topbar__help"
        href="/ayuda/manual-de-uso.pdf"
        target="_blank"
        rel="noopener"
        [attr.aria-label]="'topbar.helpLabel' | transloco"
      >
        <svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">
          <circle cx="12" cy="12" r="9" />
          <path d="M9.5 9.5a2.5 2.5 0 1 1 3.5 2.3c-.6.3-1 .9-1 1.6v.3M12 17h.01" />
        </svg>
        <span class="topbar__help-text">{{ 'topbar.help' | transloco }}</span>
      </a>
      <app-notification-bell />
      <app-user-menu />
    </div>
  `,
  styleUrl: './topbar.component.scss',
})
export class TopbarComponent {
  private readonly session = inject(SessionStore);
  private readonly directory = inject(MyCompaniesStore);
  protected readonly themeStore = inject(ThemeStore);
  private readonly menuToggle = viewChild.required<ElementRef<HTMLButtonElement>>('menuToggle');

  readonly menuOpen = input(false);
  readonly toggleMenu = output<void>();
  /** Fixed sidebar hidden by the user (1024px and wider). */
  readonly sidebarHidden = input(false);
  readonly toggleSidebar = output<void>();

  protected readonly dark = computed(() => this.themeStore.theme() === 'dark');

  protected readonly companyName = computed(() => this.directory.active()?.name ?? null);
  protected readonly canSwitchCompany = computed(() => this.session.companies().length > 1);
  protected readonly platform = computed(
    () => this.session.companyId() === null && this.session.hasScope(PLATFORM_SCOPE),
  );

  /** Returns the focus to the menu button after the drawer closes. */
  focusMenuToggle(): void {
    this.menuToggle().nativeElement.focus();
  }
}
