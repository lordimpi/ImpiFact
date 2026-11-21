import { ChangeDetectionStrategy, Component, ViewEncapsulation, inject } from '@angular/core';
import { ConfirmationService } from '@openng/optimus-ui/api';
import { ConfirmDialog } from '@openng/optimus-ui/confirmdialog';
import type { UiConfirmDialog, UiConfirmOptions } from './confirm';

/**
 * Optimus UI confirm dialog behind `UiConfirm`. Not exported from the public API: `UiConfirm` loads this file with an
 * `import()` the first time it asks, so the dialog stays out of the initial bundle.
 *
 * The safe choice («rechazar» in Optimus terms) comes first, so the dialog focuses it when it opens; Escape also
 * rejects. There is no close button: both choices are explicit, and Optimus would render it without a name.
 */
@Component({
  selector: 'ui-confirm-dialog',
  imports: [ConfirmDialog],
  providers: [ConfirmationService],
  changeDetection: ChangeDetectionStrategy.OnPush,
  // The Optimus dialog renders its own elements; the classes carry the ui-confirm-dialog prefix.
  encapsulation: ViewEncapsulation.None,
  // Optimus also puts role="alertdialog" on the p-dialog host element, an empty and unnamed second dialog for
  // assistive technology; the pass-through turns that host into a plain element. The real dialog is its container.
  template: `<p-confirmdialog styleClass="ui-confirm-dialog" [pt]="passThrough" />`,
  styles: `
    .ui-confirm-dialog {
      inline-size: min(28rem, 95vw);
    }
    .ui-confirm-dialog .p-dialog-close-button {
      display: none;
    }
    .ui-confirm-dialog .p-confirmdialog-message {
      margin: 0;
      line-height: var(--if-line-height);
    }
  `,
})
export class ConfirmDialogComponent implements UiConfirmDialog {
  protected readonly passThrough = { host: { role: 'none' } };
  private readonly confirmation = inject(ConfirmationService);
  /** Answer of the open question; a new question answers it with `false` (the safe choice). */
  private pending: ((answer: boolean) => void) | null = null;

  ask(options: UiConfirmOptions): Promise<boolean> {
    this.pending?.(false);
    return new Promise<boolean>((resolve) => {
      const answer = (value: boolean) => {
        if (this.pending === answer) this.pending = null;
        resolve(value);
      };
      this.pending = answer;
      this.confirmation.confirm({
        header: options.header,
        message: options.message,
        closable: true,
        closeOnEscape: true,
        dismissableMask: false,
        defaultFocus: 'reject',
        acceptLabel: options.acceptLabel,
        rejectLabel: options.rejectLabel,
        acceptButtonProps: { severity: 'danger' },
        rejectButtonProps: { severity: 'secondary' },
        accept: () => answer(true),
        reject: () => answer(false),
      });
    });
  }
}
