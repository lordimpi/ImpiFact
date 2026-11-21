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
import { Select } from '@openng/optimus-ui/select';
import { connectAccessor, selectPassThrough } from './field-control';
import { FieldComponent } from './field.component';
import { applyOptimusTexts } from './optimus-texts';

let nextId = 0;

/** An option of `ui-select` as the select shows it: the value as text, its label and whether it can be chosen. */
interface SelectItem {
  value: string;
  label: string;
  disabled: boolean;
}

/**
 * Drop-down list of Optimus UI (`p-select`) inside `ui-field`: it takes the id, the label, the description
 * (explanation, hint and error) and `aria-invalid` from the field, like `uiInput`. It is a Signal Forms control
 * (`[formField]`): its value is the chosen value as text, '' when there is none, so it works with the same models as
 * a native `<select>` (catalog ids as text). It also works with `[value]`/`(valueChange)` or `[(value)]`. The list
 * opens over the page (it never gets cut by a card) and the control takes the whole width of the field. It is red
 * only while `ui-field` shows an error, never before the field is touched.
 *
 * Usage:
 * ```html
 * <ui-field [label]="'companies.data.regime' | transloco" [error]="errors().type_regime_id">
 *   <ui-select [options]="regimes()" [placeholder]="'...' | transloco" [formField]="companyForm.type_regime_id" />
 * </ui-field>
 * ```
 * The options are any objects: `optionValue` (default `id`) and `optionLabel` (default `name`) name their keys, so
 * the catalog options (`{ id, name }`) need no mapping. Outside `ui-field`, give it an `ariaLabel`.
 */
@Component({
  selector: 'ui-select',
  imports: [Select],
  changeDetection: ChangeDetectionStrategy.OnPush,
  // No encapsulation: the Optimus select keeps its own classes.
  encapsulation: ViewEncapsulation.None,
  host: { class: 'ui-select-host' },
  template: `
    <p-select
      [options]="items()"
      optionLabel="label"
      optionValue="value"
      optionDisabled="disabled"
      [placeholder]="placeholder()"
      [inputId]="field?.controlId ?? ownId"
      [ariaLabelledBy]="field?.labelId"
      [ariaLabel]="field ? undefined : ariaLabel()"
      [disabled]="disabled()"
      [required]="required() || (field?.required() ?? false)"
      [invalid]="field?.invalid() ?? false"
      [filter]="filter()"
      [filterPlaceholder]="filterPlaceholder()"
      scrollHeight="16rem"
      [pt]="passThrough()"
    />
  `,
  styles: `
    .ui-select-host {
      display: block;
    }
  `,
})
export class SelectComponent {
  /** Chosen value as text; '' when there is none. `[formField]` binds it. */
  readonly value = model('');
  readonly options = input<readonly object[]>([]);
  /** Key of the option that holds its value (compared as text). */
  readonly optionValue = input('id');
  /** Key of the option that holds its visible label. */
  readonly optionLabel = input('name');
  /** Key of the option that says it cannot be chosen. */
  readonly optionDisabled = input('disabled');
  /** Text inside the empty control («Seleccione…»), already translated. */
  readonly placeholder = input<string>();
  /** Accessible name when the select is not inside `ui-field`. */
  readonly ariaLabel = input<string>();
  /** Shows a box to filter the options in the browser (long lists). */
  readonly filter = input(false, { transform: booleanAttribute });
  readonly filterPlaceholder = input<string>();
  readonly disabled = input(false, { transform: booleanAttribute });
  readonly required = input(false, { transform: booleanAttribute });
  /** The control lost the focus with the list closed (Signal Forms marks it touched). */
  readonly touch = output<void>();

  protected readonly field = inject(FieldComponent, { optional: true });
  protected readonly ownId = `ui-select-${nextId++}`;
  private readonly select = viewChild.required(Select);
  protected readonly passThrough = selectPassThrough(this.field);

  protected readonly items = computed<SelectItem[]>(() => {
    const [valueKey, labelKey, disabledKey] = [
      this.optionValue(),
      this.optionLabel(),
      this.optionDisabled(),
    ];
    return this.options().map((option) => {
      const record = option as Record<string, unknown>;
      return {
        value: String(record[valueKey] ?? ''),
        label: String(record[labelKey] ?? ''),
        disabled: record[disabledKey] === true,
      };
    });
  });

  constructor() {
    applyOptimusTexts();
    connectAccessor(this.select, this.value, () => this.touch.emit());
  }

  /** For Signal Forms: `focusBoundControl()` focuses the select. */
  focus(): void {
    this.select().focus();
  }
}
