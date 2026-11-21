import {
  ChangeDetectionStrategy,
  Component,
  ViewEncapsulation,
  afterRenderEffect,
  booleanAttribute,
  computed,
  input,
  model,
  output,
  viewChild,
} from '@angular/core';
import type { FormCheckboxControl } from '@angular/forms/signals';
import { Checkbox, CheckboxChangeEvent } from '@openng/optimus-ui/checkbox';

let nextId = 0;

/**
 * Checkbox of Optimus UI (`p-checkbox`) with its label on the same row. The box is centred on the first line of the
 * label and a long label wraps under its own text, never under the box; clicking the label toggles the box. Optimus
 * renders a real checkbox input inside, so it keeps the checkbox role and takes its accessible name from the label.
 *
 * A host styled as a card or a chip sets its padding with `--ui-checkbox-padding`, so the whole card toggles the box.
 *
 * It is a Signal Forms checkbox control (`[formField]` binds `checked` and `disabled`) and also works with
 * `[checked]`/`(checkedChange)` or `[(checked)]`. The label is the projected content or the `label` input.
 *
 * Usage:
 * ```html
 * <ui-checkbox [formField]="planForm.state" ariaDescribedBy="plan-state-help">
 *   {{ 'platform.planForm.state' | transloco }}
 * </ui-checkbox>
 * <ui-checkbox [checked]="accepted()" (checkedChange)="accepted.set($event)" [label]="labels().accept" />
 * ```
 */
@Component({
  selector: 'ui-checkbox',
  imports: [Checkbox],
  changeDetection: ChangeDetectionStrategy.OnPush,
  // No encapsulation: the Optimus checkbox keeps its own classes.
  encapsulation: ViewEncapsulation.None,
  host: { class: 'ui-checkbox', '[class.ui-checkbox--disabled]': 'disabled()' },
  template: `
    <label class="ui-checkbox__row" [for]="ownId">
      <span class="ui-checkbox__control">
        <p-checkbox
          [binary]="true"
          [inputId]="ownId"
          [disabled]="disabled()"
          [pt]="passThrough()"
          (onChange)="change($event)"
          (onBlur)="touch.emit()"
        />
      </span>
      <span class="ui-checkbox__label">{{ label() }}<ng-content /></span>
    </label>
  `,
  styles: `
    .ui-checkbox {
      display: inline-flex;
      max-inline-size: 100%;
    }
    /* The row fills the host, so a host styled as a card or a chip is clickable all over: it sets its padding
       with --ui-checkbox-padding instead of its own padding. */
    .ui-checkbox__row {
      display: inline-flex;
      flex: 1 1 auto;
      align-items: flex-start;
      gap: var(--if-space-2);
      min-inline-size: 0;
      padding: var(--ui-checkbox-padding, 0);
      cursor: pointer;
    }
    /* One line of the label high: the box sits on the centre of the first text line, whatever the font size. */
    .ui-checkbox__control {
      display: inline-flex;
      flex: none;
      align-items: center;
      block-size: 1lh;
    }
    .ui-checkbox__label {
      min-inline-size: 0;
    }
    .ui-checkbox--disabled .ui-checkbox__row {
      cursor: not-allowed;
      color: var(--if-color-text-muted);
    }
  `,
})
export class CheckboxComponent implements FormCheckboxControl {
  /** Whether the box is checked. `[formField]` binds it. */
  readonly checked = model(false);
  /** Visible label, already translated; projected content works too. */
  readonly label = input<string>();
  readonly disabled = input(false, { transform: booleanAttribute });
  /** Ids of the elements that describe the checkbox (a hint, a help tooltip), set on the real input. */
  readonly ariaDescribedBy = input<string | null>();
  /** The checkbox lost the focus (Signal Forms marks it touched). */
  readonly touch = output<void>();

  protected readonly ownId = `ui-checkbox-${nextId++}`;
  private readonly checkbox = viewChild.required(Checkbox);
  protected readonly passThrough = computed(() => ({
    input: { 'aria-describedby': this.ariaDescribedBy() || null },
  }));

  constructor() {
    // Optimus keeps the value in its own model: the checked state goes in through its ControlValueAccessor.
    afterRenderEffect({
      write: () => this.checkbox().writeValue(this.checked()),
    });
  }

  protected change(event: CheckboxChangeEvent): void {
    this.checked.set(event.checked === true);
  }

  /** For Signal Forms: `focusBoundControl()` focuses the checkbox. */
  focus(): void {
    this.checkbox().focus();
  }
}
