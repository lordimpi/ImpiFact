import { NgComponentOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  Type,
  afterNextRender,
  computed,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Title } from '@angular/platform-browser';
import { RouterOutlet } from '@angular/router';
import { InactivityMonitor, SessionStore, ToastStore } from '@impifact/core';
import { loadAuthTexts, loadInactivityWarning } from '@impifact/feature-auth';
import { ToastRegionComponent, UiToast } from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { injectCurrentPath } from './shell/current-path';
import { SidebarComponent } from './shell/sidebar.component';
import { TopbarComponent } from './shell/topbar.component';

/** From this width the sidebar is fixed and the drawer does not exist; it matches the stylesheets. */
const WIDE_LAYOUT = '(min-width: 64rem)';

/** Pages shown without the sidebar and the top bar: access pages and the company selection. */
const BARE_PATHS: readonly string[] = [
  '/login',
  '/recuperar',
  '/restablecer',
  '/empresas/seleccionar',
];

/** Pages that draw their own full-screen layout: the content area loses its padding and width limit. */
const FULL_BLEED_PATHS: readonly string[] = ['/login'];

/**
 * Panel shell: with a session, sidebar (menu grouped by section and filtered by role) and top bar (company, notices
 * and user menu) around the content; access pages and the company selection show only the content. It also hosts
 * the core toasts and the inactivity dialog. The sidebar and the top bar load in deferred chunks the first time a
 * page needs them, so the access pages keep them out of the initial bundle. Below 1024px the open drawer is modal:
 * the skip link, the top bar and the content become inert until it closes.
 */
@Component({
  selector: 'app-root',
  imports: [
    NgComponentOutlet,
    RouterOutlet,
    TranslocoPipe,
    ToastRegionComponent,
    SidebarComponent,
    TopbarComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly transloco = inject(TranslocoService);
  protected readonly toastStore = inject(ToastStore);
  private readonly inactivity = inject(InactivityMonitor);
  private readonly session = inject(SessionStore);
  private readonly injector = inject(Injector);
  private readonly path = injectCurrentPath();
  // Queried by template reference: a class predicate would import the deferred components eagerly.
  private readonly sidebar = viewChild<unknown, ElementRef<HTMLElement>>('sidebar', {
    read: ElementRef,
  });
  private readonly topbar = viewChild<Pick<TopbarComponent, 'focusMenuToggle'>>('topbar');

  /** Sidebar and top bar only with a session and outside the access pages and the company selection. */
  protected readonly layout = computed(
    () => this.session.isAuthenticated() && !BARE_PATHS.includes(this.path()),
  );

  protected readonly fullBleed = computed(() => FULL_BLEED_PATHS.includes(this.path()));

  /** Sidebar drawer on narrow screens; it closes after every navigation. */
  protected readonly drawerOpen = signal(false);

  /**
   * Diálogo de inactividad de feature-auth. Se carga (con sus textos) la primera vez que el monitor avisa, así no
   * pesa en el bundle inicial; después queda montado y se abre y cierra solo con `InactivityMonitor.warning()`.
   */
  protected readonly inactivityWarning = signal<Type<unknown> | null>(null);
  private loadingWarning = false;

  protected readonly toasts = computed<UiToast[]>(() =>
    this.toastStore.toasts().map((toast) => ({
      id: toast.id,
      kind: toast.kind,
      text: toast.text,
      reference: toast.correlationId,
    })),
  );

  constructor() {
    const title = inject(Title);
    this.transloco
      .selectTranslate<string>('app.documentTitle')
      .pipe(takeUntilDestroyed())
      .subscribe((text) => title.setTitle(text));

    effect(() => {
      this.path();
      this.drawerOpen.set(false);
    });

    // Widening the window past the drawer breakpoint closes it, so the fixed sidebar never leaves the page inert.
    if (typeof window.matchMedia === 'function') {
      const wide = window.matchMedia(WIDE_LAYOUT);
      const onChange = (event: MediaQueryListEvent) => event.matches && this.drawerOpen.set(false);
      wide.addEventListener('change', onChange);
      inject(DestroyRef).onDestroy(() => wide.removeEventListener('change', onChange));
    }

    effect(() => {
      if (this.inactivity.warning() && !this.loadingWarning) {
        this.loadingWarning = true;
        void this.loadWarning();
      }
    });
  }

  protected toggleDrawer(): void {
    if (this.drawerOpen()) {
      this.closeDrawer(true);
      return;
    }
    this.drawerOpen.set(true);
    // Move the focus into the drawer once it is visible.
    afterNextRender(
      () => this.sidebar()?.nativeElement.querySelector<HTMLElement>('nav a')?.focus(),
      { injector: this.injector },
    );
  }

  /** Closes the drawer; after Escape or the close button the focus goes back to the menu button. */
  protected closeDrawer(returnFocus: boolean): void {
    if (!this.drawerOpen()) {
      return;
    }
    this.drawerOpen.set(false);
    if (returnFocus) {
      // The top bar stays inert until the next render; an inert button refuses the focus.
      afterNextRender(() => this.topbar()?.focusMenuToggle(), { injector: this.injector });
    }
  }

  private async loadWarning(): Promise<void> {
    const [component] = await Promise.all([loadInactivityWarning(), loadAuthTexts(this.transloco)]);
    this.inactivityWarning.set(component);
  }

  /** Copia el código de soporte de un aviso de error y lo confirma con otro aviso. */
  protected async copyReference(reference: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(reference);
      this.toastStore.show({ kind: 'success', text: this.transloco.translate('ui.toasts.copied') });
    } catch {
      // Sin permiso de portapapeles el código sigue visible y seleccionable en el aviso.
    }
  }
}
