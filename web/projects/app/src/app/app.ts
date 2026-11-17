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
import { InactivityMonitor, ToastStore } from '@impifact/core';
import { loadAuthTexts, loadInactivityWarning } from '@impifact/feature-auth';
import { ToastRegionComponent, UiToast } from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { UserMenuComponent } from './shell/user-menu.component';

/**
 * Shell del panel: encabezado con el menú de usuario, zona de contenido, avisos (*toasts*) de core y el diálogo de
 * inactividad. El menú por rol llega con los features.
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
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly transloco = inject(TranslocoService);
  protected readonly toastStore = inject(ToastStore);
  private readonly inactivity = inject(InactivityMonitor);

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
