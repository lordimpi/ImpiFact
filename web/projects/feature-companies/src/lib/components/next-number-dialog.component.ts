import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  effect,
  input,
  model,
  output,
  signal,
  untracked,
} from '@angular/core';
import {
  ButtonComponent,
  DialogComponent,
  DigitsOnlyDirective,
  FieldComponent,
  InputDirective,
} from '@impifact/ui';

/** Textos del diálogo, ya traducidos. */
export interface NextNumberLabels {
  header: string;
  help: string;
  field: string;
  range: string;
  /** Example inside the empty field: a number of the range. */
  placeholder: string;
  save: string;
  cancel: string;
  close: string;
}

/**
 * Cambio del próximo número de una resolución (componente de presentación). Al abrirse muestra el próximo número
 * actual; emite `saved` con el texto escrito y quien lo usa lo revisa, llama al Api y pasa el motivo de un rechazo en
 * `error`, que queda junto al campo.
 */
@Component({
  selector: 'feat-next-number-dialog',
  imports: [ButtonComponent, DialogComponent, DigitsOnlyDirective, FieldComponent, InputDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-dialog [header]="labels().header" [closeLabel]="labels().close" [(visible)]="visible">
      <form novalidate (submit)="onSubmit($event)">
        <p class="next-number-help">{{ labels().help }}</p>
        <ui-field [label]="labels().field" [hint]="labels().range" [error]="error()" required>
          <input
            uiInput
            uiDigitsOnly
            type="text"
            autocomplete="off"
            [placeholder]="labels().placeholder"
            [value]="value()"
            (input)="value.set($any($event.target).value)"
          />
        </ui-field>
      </form>
      <div uiDialogFooter class="next-number-actions">
        <ui-button variant="secondary" [label]="labels().cancel" (pressed)="visible.set(false)" />
        <ui-button [label]="labels().save" [loading]="busy()" (pressed)="saved.emit(value())" />
      </div>
    </ui-dialog>
  `,
  styles: `
    .next-number-help {
      margin: 0 0 var(--if-space-3);
      color: var(--if-color-text-muted);
    }
    .next-number-actions {
      display: flex;
      justify-content: flex-end;
      gap: var(--if-space-2);
    }
  `,
})
export class NextNumberDialogComponent {
  readonly labels = input.required<NextNumberLabels>();
  readonly visible = model(false);
  /** Próximo número actual: el valor con el que se abre el campo. */
  readonly initial = input('');
  /** Motivo por el que no se guardó (del cliente o del Api), junto al campo. */
  readonly error = input<string | null>(null);
  readonly busy = input(false, { transform: booleanAttribute });
  readonly saved = output<string>();

  protected readonly value = signal('');

  constructor() {
    // Cada vez que se abre, el campo empieza con el próximo número actual.
    effect(() => {
      if (this.visible()) {
        this.value.set(untracked(this.initial));
      }
    });
  }

  protected onSubmit(event: Event): void {
    event.preventDefault();
    this.saved.emit(this.value());
  }
}
