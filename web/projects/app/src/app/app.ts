import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Title } from '@angular/platform-browser';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { ToastStore } from '@impifact/core';
import { ToastRegionComponent, UiToast } from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';

/**
 * Shell del panel: encabezado, menú, zona de contenido y avisos (*toasts*) de core.
 * El menú por rol llega con los features.
 */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslocoPipe, ToastRegionComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly transloco = inject(TranslocoService);
  protected readonly toastStore = inject(ToastStore);

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
