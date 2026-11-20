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
import { UserMenuComponent } from './user-menu.component';

/**
 * Top bar of the panel: menu button (narrow screens only, it opens the sidebar drawer), the active company (a link
 * to the company selection when the user has several), the notification bell and the user menu.
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
  private readonly menuToggle = viewChild.required<ElementRef<HTMLButtonElement>>('menuToggle');

  readonly menuOpen = input(false);
  readonly toggleMenu = output<void>();

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
