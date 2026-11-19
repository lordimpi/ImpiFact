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
import { MyCompaniesStore, SessionStore } from '@impifact/core';
import { TranslocoPipe } from '@jsverse/transloco';

/**
 * Menú de usuario del encabezado, solo con sesión: correo, empresa activa (su razón social, de
 * `MyCompaniesStore`), cambio de empresa (si tiene varias) y cierre de sesión.
 * Es un botón que muestra u oculta un panel (patrón *disclosure*, con `aria-expanded`); Escape lo cierra y devuelve
 * el foco al botón. Usa elementos nativos para no cargar Optimus UI en el bundle inicial.
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
        <span>{{ 'userMenu.account' | transloco }}</span>
        @if (email(); as email) {
          <span class="user-menu__email">{{ email }}</span>
        }
        @if (companyName(); as name) {
          <span class="user-menu__email">{{ name }}</span>
        }
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
      margin-inline-start: auto;
    }
    .user-menu__toggle,
    .user-menu__panel button {
      font: inherit;
      color: var(--if-color-text);
      background: transparent;
      border: 1px solid var(--if-color-border);
      border-radius: var(--if-radius-sm);
      padding: var(--if-space-1) var(--if-space-3);
      cursor: pointer;
    }
    .user-menu__toggle {
      display: flex;
      flex-direction: column;
      align-items: flex-end;
    }
    .user-menu__email {
      font-size: var(--if-font-size-sm);
      color: var(--if-color-text-muted);
    }
    .user-menu__panel {
      position: absolute;
      inset-inline-end: 0;
      z-index: 10;
      display: flex;
      flex-direction: column;
      align-items: stretch;
      gap: var(--if-space-2);
      min-inline-size: 12rem;
      margin-block-start: var(--if-space-1);
      padding: var(--if-space-2);
      background: var(--if-color-surface-raised);
      border: 1px solid var(--if-color-border);
      border-radius: var(--if-radius-md);
      box-shadow: var(--if-shadow-overlay);
    }
    .user-menu__panel a {
      color: var(--if-color-primary);
      padding: var(--if-space-1) var(--if-space-3);
    }
  `,
})
export class UserMenuComponent {
  protected readonly session = inject(SessionStore);
  private readonly directory = inject(MyCompaniesStore);
  private readonly toggle = viewChild<ElementRef<HTMLButtonElement>>('toggle');

  protected readonly open = signal(false);
  protected readonly email = computed(() => this.session.user()?.email ?? null);
  protected readonly companyName = computed(() => this.directory.active()?.name ?? null);
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
