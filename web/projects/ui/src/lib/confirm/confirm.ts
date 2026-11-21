import { DOCUMENT } from '@angular/common';
import {
  ApplicationRef,
  DestroyRef,
  EnvironmentInjector,
  Injectable,
  createComponent,
  inject,
} from '@angular/core';

/** Texts of a confirmation question, already translated. */
export interface UiConfirmOptions {
  /** Title of the dialog; it is also its accessible name. */
  header: string;
  message: string;
  /** The action that goes ahead (for example «Salir sin guardar»); it looks like a dangerous action. */
  acceptLabel: string;
  /** The safe choice (for example «Seguir editando»); it gets the focus, and Escape picks it. */
  rejectLabel: string;
}

/** What `UiConfirm` needs from the lazily loaded dialog component. */
export interface UiConfirmDialog {
  ask(options: UiConfirmOptions): Promise<boolean>;
}

/**
 * Asks a yes/no question with the Optimus UI confirm dialog and resolves `true` when the person accepts. Escape and
 * the safe choice resolve `false`.
 *
 * The dialog (Optimus `p-confirmdialog`, about 50 kB with the dialog and the button) is not part of the initial
 * bundle: the first question loads `confirm-dialog.component.ts` with an `import()` and mounts it once under
 * `<body>`; later questions reuse it. That file is not in the public API on purpose: exporting it would pull the
 * dialog into every bundle that imports `@impifact/ui`.
 *
 * Usage: `const leave = await inject(UiConfirm).ask({ header, message, acceptLabel, rejectLabel });`
 */
@Injectable({ providedIn: 'root' })
export class UiConfirm {
  private readonly appRef = inject(ApplicationRef);
  private readonly injector = inject(EnvironmentInjector);
  private readonly document = inject(DOCUMENT);
  private readonly destroyRef = inject(DestroyRef);
  private dialog: Promise<UiConfirmDialog> | null = null;

  async ask(options: UiConfirmOptions): Promise<boolean> {
    this.dialog ??= this.mount();
    try {
      return await (await this.dialog).ask(options);
    } catch {
      // The dialog chunk could not load (for example, offline): stay put, and try loading it again next time.
      this.dialog = null;
      return false;
    }
  }

  private async mount(): Promise<UiConfirmDialog> {
    const { ConfirmDialogComponent } = await import('./confirm-dialog.component');
    const ref = createComponent(ConfirmDialogComponent, { environmentInjector: this.injector });
    this.appRef.attachView(ref.hostView);
    this.document.body.appendChild(ref.location.nativeElement);
    this.destroyRef.onDestroy(() => ref.destroy());
    ref.changeDetectorRef.detectChanges();
    return ref.instance;
  }
}
