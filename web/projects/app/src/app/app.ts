import { NgComponentOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  Type,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Title } from '@angular/platform-browser';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { InactivityMonitor, SessionStore, ToastStore } from '@impifact/core';
import { loadAuthTexts, loadInactivityWarning } from '@impifact/feature-auth';
import { API_KEYS_ROLES } from '@impifact/feature-api-keys';
import { MASTERS_ROLES } from '@impifact/feature-masters';
import { NOTIFICATIONS_ROLES } from '@impifact/feature-notifications';
import { PLANS_ROLES } from '@impifact/feature-plans';
import { PLATFORM_ROLES, PLATFORM_SCOPE } from '@impifact/feature-platform';
import { ToastRegionComponent, UiToast } from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NotificationBellComponent } from './shell/notification-bell.component';
import { UserMenuComponent } from './shell/user-menu.component';

/** Entrada del menú principal; sin `roles` la ve cualquiera, con `roles` solo quien tiene alguno (sección 7). */
interface NavItem {
  path: string;
  /** Clave del texto en `nav.*`. */
  label: string;
  exact?: boolean;
  roles?: readonly string[];
  /** Alcance del token que además exige la entrada (por ejemplo, `platform:*`, que solo va sin empresa activa). */
  scope?: string;
}

/** Menú principal, filtrado por rol. Cada feature agrega aquí sus entradas. */
const NAV_ITEMS: readonly NavItem[] = [
  { path: '/', label: 'nav.home', exact: true },
  { path: '/clientes', label: 'nav.customers', roles: MASTERS_ROLES },
  { path: '/productos', label: 'nav.products', roles: MASTERS_ROLES },
  { path: '/documentos', label: 'nav.documents', roles: ['owner', 'seller'] },
  { path: '/notificaciones', label: 'nav.notifications', roles: NOTIFICATIONS_ROLES },
  { path: '/llaves-api', label: 'nav.apiKeys', roles: API_KEYS_ROLES },
  { path: '/plan', label: 'nav.plan', roles: PLANS_ROLES },
  {
    path: '/plataforma/planes',
    label: 'nav.platformPlans',
    roles: PLATFORM_ROLES,
    scope: PLATFORM_SCOPE,
  },
  {
    path: '/plataforma/empresas',
    label: 'nav.platformCompanies',
    roles: PLATFORM_ROLES,
    scope: PLATFORM_SCOPE,
  },
];

/**
 * Shell del panel: encabezado con el menú principal (filtrado por rol), el de usuario y la campana de avisos, zona
 * de contenido, avisos (*toasts*) de core y el diálogo de inactividad.
 */
@Component({
  selector: 'app-root',
  imports: [
    NgComponentOutlet,
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    TranslocoPipe,
    ToastRegionComponent,
    UserMenuComponent,
    NotificationBellComponent,
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

  /** Entradas del menú que corresponden a la sesión: las que piden rol solo aparecen con sesión y ese rol. */
  protected readonly navItems = computed(() =>
    NAV_ITEMS.filter(
      (item) =>
        (!item.roles || (this.session.isAuthenticated() && this.session.hasRole(...item.roles))) &&
        (!item.scope || this.session.hasScope(item.scope)),
    ),
  );

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
      if (this.inactivity.warning() && !this.loadingWarning) {
        this.loadingWarning = true;
        void this.loadWarning();
      }
    });
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
