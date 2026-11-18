import { NgComponentOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Type,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { NotificationsStore, SessionStore } from '@impifact/core';
import { loadNotificationsDropdown, loadNotificationsTexts } from '@impifact/feature-notifications';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { filter } from 'rxjs';

/** Alcance que pide el Api para leer los avisos. */
const READ_SCOPE = 'documents:read';
/** Desde aquí el número de la campana se abrevia («99+»); el nombre accesible lleva el número completo. */
const BADGE_MAX = 99;

/**
 * Campana de avisos del encabezado (sección 9): muestra cuántos avisos sin leer tiene la empresa activa, que el
 * store de core consulta cada 60 s. Es un botón con un panel (patrón *disclosure*, con `aria-expanded`); la lista
 * del panel vive en feature-notifications y se carga la primera vez que se abre, así no pesa en el bundle inicial.
 * Escape la cierra y devuelve el foco al botón; también se cierra al hacer clic fuera o al navegar.
 */
@Component({
  selector: 'app-notification-bell',
  imports: [NgComponentOutlet, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '(keydown.escape)': 'close(true)',
    '(document:click)': 'closeOnOutsideClick($event)',
  },
  template: `
    @if (visible()) {
      <button
        #toggle
        type="button"
        class="bell__toggle"
        aria-controls="notifications-panel"
        [attr.aria-expanded]="open()"
        [attr.aria-label]="
          store.unreadCount() > 0
            ? ('bell.labelUnread' | transloco: { count: store.unreadCount() })
            : ('bell.label' | transloco)
        "
        (click)="toggleOpen()"
      >
        <svg class="bell__icon" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
          <path
            d="M12 22a2.5 2.5 0 0 0 2.45-2h-4.9A2.5 2.5 0 0 0 12 22Zm7-6V11a7 7 0 0 0-5.5-6.84V3.5a1.5 1.5 0 0 0-3 0v.66A7 7 0 0 0 5 11v5l-2 2v1h18v-1l-2-2Z"
          />
        </svg>
        @if (store.unreadCount() > 0) {
          <span class="bell__badge" aria-hidden="true">{{ badge() }}</span>
        }
      </button>
      @if (open()) {
        <div
          id="notifications-panel"
          class="bell__panel"
          role="region"
          [attr.aria-label]="'bell.label' | transloco"
        >
          @if (dropdown(); as component) {
            <ng-container *ngComponentOutlet="component" />
          } @else {
            <p class="bell__loading" role="status">{{ 'bell.loading' | transloco }}</p>
          }
        </div>
      }
    }
  `,
  styles: `
    :host {
      position: relative;
    }
    .bell__toggle {
      position: relative;
      display: inline-flex;
      align-items: center;
      justify-content: center;
      min-inline-size: 2.5rem;
      min-block-size: 2.5rem;
      color: var(--if-color-text);
      background: transparent;
      border: 1px solid var(--if-color-border);
      border-radius: var(--if-radius-sm);
      cursor: pointer;
    }
    .bell__icon {
      inline-size: 1.25rem;
      block-size: 1.25rem;
      fill: currentColor;
    }
    .bell__badge {
      position: absolute;
      inset-block-start: -0.4rem;
      inset-inline-end: -0.4rem;
      min-inline-size: 1.25rem;
      padding-inline: 0.25rem;
      font-size: var(--if-font-size-sm);
      font-weight: var(--if-font-weight-bold);
      line-height: 1.25rem;
      color: var(--if-color-on-danger);
      background: var(--if-color-danger);
      border-radius: 999px;
    }
    .bell__panel {
      position: absolute;
      inset-inline-end: 0;
      z-index: 10;
      inline-size: min(24rem, calc(100vw - 2rem));
      margin-block-start: var(--if-space-1);
      background: var(--if-color-surface-raised);
      border: 1px solid var(--if-color-border);
      border-radius: var(--if-radius-md);
      box-shadow: var(--if-shadow-overlay);
    }
    .bell__loading {
      margin: 0;
      padding: var(--if-space-3);
    }
  `,
})
export class NotificationBellComponent {
  protected readonly store = inject(NotificationsStore);
  private readonly session = inject(SessionStore);
  private readonly transloco = inject(TranslocoService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly toggle = viewChild<ElementRef<HTMLButtonElement>>('toggle');

  protected readonly visible = computed(
    () =>
      this.session.isAuthenticated() &&
      this.session.hasScope(READ_SCOPE) &&
      this.session.companyId() !== null,
  );
  protected readonly badge = computed(() =>
    this.store.unreadCount() > BADGE_MAX ? `${BADGE_MAX}+` : String(this.store.unreadCount()),
  );
  protected readonly open = signal(false);
  protected readonly dropdown = signal<Type<unknown> | null>(null);
  private loading = false;

  constructor() {
    inject(Router)
      .events.pipe(
        filter((event) => event instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe(() => this.open.set(false));
  }

  protected toggleOpen(): void {
    this.open.set(!this.open());
    if (this.open() && !this.loading) {
      this.loading = true;
      void this.loadDropdown();
    }
  }

  protected close(returnFocus: boolean): void {
    if (!this.open()) {
      return;
    }
    this.open.set(false);
    if (returnFocus) {
      this.toggle()?.nativeElement.focus();
    }
  }

  protected closeOnOutsideClick(event: MouseEvent): void {
    if (this.open() && !this.host.nativeElement.contains(event.target as Node | null)) {
      this.open.set(false);
    }
  }

  private async loadDropdown(): Promise<void> {
    try {
      const [component] = await Promise.all([
        loadNotificationsDropdown(),
        loadNotificationsTexts(this.transloco),
      ]);
      this.dropdown.set(component);
    } catch {
      // Sin red el chunk no llega: se intenta de nuevo la próxima vez que se abra.
      this.loading = false;
    }
  }
}
