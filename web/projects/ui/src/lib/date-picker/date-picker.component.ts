import {
  ChangeDetectionStrategy,
  Component,
  ViewEncapsulation,
  booleanAttribute,
  computed,
  inject,
  input,
  model,
  output,
  viewChild,
} from '@angular/core';
import { DatePicker } from '@openng/optimus-ui/datepicker';
import { connectAccessor, linkToField } from '../field/field-control';
import { FieldComponent } from '../field/field.component';
import { applyOptimusTexts } from '../field/optimus-texts';

let nextId = 0;

const ISO_DATE = /^(\d{4})-(\d{2})-(\d{2})$/;

/** `YYYY-MM-DD` of a local date; '' for none. */
export function isoDate(date: Date | null | undefined): string {
  if (!(date instanceof Date) || Number.isNaN(date.getTime())) {
    return '';
  }
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${date.getFullYear()}-${month}-${day}`;
}

/** Local date of a `YYYY-MM-DD` text; `null` when it is empty or not a real date (2026-02-30). */
export function parseIsoDate(text: string | null | undefined): Date | null {
  const match = ISO_DATE.exec(text ?? '');
  if (!match) {
    return null;
  }
  const [year, month, day] = [Number(match[1]), Number(match[2]), Number(match[3])];
  const date = new Date(year, month - 1, day);
  return date.getMonth() === month - 1 && date.getDate() === day ? date : null;
}

/**
 * Date picker of Optimus UI (`p-datepicker`) with the calendar button. It shows the date as `dd/mm/aaaa` and the
 * calendar in Spanish (texts of `UI_TEXTS.optimus`), and its value is the ISO date `YYYY-MM-DD` ('' when empty), the
 * text the forms and the Api use. It is a Signal Forms control (`[formField]`) and also works with
 * `[value]`/`(valueChange)`. Inside `ui-field` it takes the id, the label, the description (explanation, hint and error)
 * and `aria-invalid` from the field; it is red only while the field shows an error. The person can type the date or
 * choose it in the calendar; `minDate` and `maxDate` (ISO dates) leave the days out of range disabled.
 *
 * Usage:
 * ```html
 * <ui-field [label]="'apiKeys.form.expiresAt' | transloco" [error]="errors().expires_at">
 *   <ui-date-picker [formField]="keyForm.expires_at" [minDate]="today" />
 * </ui-field>
 * ```
 */
@Component({
  selector: 'ui-date-picker',
  imports: [DatePicker],
  changeDetection: ChangeDetectionStrategy.OnPush,
  // No encapsulation: the Optimus date picker keeps its own classes.
  encapsulation: ViewEncapsulation.None,
  host: { class: 'ui-date-picker' },
  template: `
    <p-datepicker
      [inputId]="field?.controlId ?? ownId"
      [ariaLabelledBy]="field?.labelId"
      [ariaLabel]="field ? undefined : ariaLabel()"
      [showIcon]="true"
      iconDisplay="button"
      [placeholder]="placeholder()"
      [minDate]="min()"
      [maxDate]="max()"
      [disabled]="disabled()"
      [required]="required() || (field?.required() ?? false)"
      [invalid]="field?.invalid() ?? false"
      [fluid]="true"
    />
  `,
  styles: `
    .ui-date-picker {
      display: block;
    }
  `,
})
export class DatePickerComponent {
  /** ISO date `YYYY-MM-DD`; '' when empty. `[formField]` binds it. */
  readonly value = model('');
  /** First day that can be chosen (ISO date). */
  readonly minDate = input<string>();
  /** Last day that can be chosen (ISO date). */
  readonly maxDate = input<string>();
  /** Text inside the empty control (for example «dd/mm/aaaa»), already translated. */
  readonly placeholder = input<string>();
  /** Accessible name when the date picker is not inside `ui-field`. */
  readonly ariaLabel = input<string>();
  readonly disabled = input(false, { transform: booleanAttribute });
  readonly required = input(false, { transform: booleanAttribute });
  /** The input lost the focus (Signal Forms marks it touched). */
  readonly touch = output<void>();

  protected readonly field = inject(FieldComponent, { optional: true });
  protected readonly ownId = `ui-date-picker-${nextId++}`;
  protected readonly min = computed(() => parseIsoDate(this.minDate()));
  protected readonly max = computed(() => parseIsoDate(this.maxDate()));
  private readonly picker = viewChild.required(DatePicker);

  constructor() {
    applyOptimusTexts();
    connectAccessor(this.picker, this.value, () => this.touch.emit(), {
      toText: (date) => isoDate(date as Date | null),
      fromText: parseIsoDate,
    });
    linkToField(this.field, { control: 'input' });
  }

  /** For Signal Forms: `focusBoundControl()` focuses the input. */
  focus(): void {
    this.picker().inputfieldViewChild?.nativeElement.focus();
  }
}
