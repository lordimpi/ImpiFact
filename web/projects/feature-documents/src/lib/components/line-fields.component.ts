import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { FieldTree, FormField } from '@angular/forms/signals';
import {
  ButtonComponent,
  FieldComponent,
  InputDirective,
  NonNegativeDirective,
  SelectComponent,
} from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { DOCUMENTS_I18N_SCOPE } from '../documents-i18n';
import { IVA_PERCENTS, InvoiceLineModel } from '../forms/invoice.form';
import { CopCurrencyPipe } from './cop-currency.pipe';

type LineField = 'code' | 'description' | 'quantity' | 'unitPrice';

const ERROR_KEYS: Record<LineField, string> = {
  code: 'issue.line.codeRequired',
  description: 'issue.line.descriptionRequired',
  quantity: 'issue.line.quantityInvalid',
  unitPrice: 'issue.line.priceInvalid',
};

/**
 * Campos de una línea de factura o nota (código, descripción, cantidad, precio e IVA) con el valor que calculó el
 * servidor. No calcula nada: `amount` llega del cálculo del Api y se muestra tal cual.
 */
@Component({
  selector: 'feat-line-fields',
  imports: [
    FormField,
    TranslocoPipe,
    ButtonComponent,
    FieldComponent,
    InputDirective,
    NonNegativeDirective,
    SelectComponent,
    CopCurrencyPipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let field = line();
    <fieldset class="line">
      <legend class="line__legend">
        {{ 'documents.issue.line.title' | transloco: { number: index() + 1 } }}
      </legend>
      <div class="line__header">
        <p class="line__amount">
          <span class="line__muted">{{ 'documents.issue.line.amount' | transloco }}</span>
          <span class="line__value">{{ amount() | cop }}</span>
        </p>
        <ui-button
          variant="ghost"
          size="small"
          [label]="'documents.issue.line.remove' | transloco: { number: index() + 1 }"
          (pressed)="remove.emit()"
        />
      </div>
      <div class="line__grid">
        <ui-field
          class="line__description"
          [label]="'documents.issue.line.description' | transloco"
          [error]="errors().description"
          required
        >
          <input
            uiInput
            type="text"
            [placeholder]="'documents.issue.line.descriptionPlaceholder' | transloco"
            [formField]="field.description"
          />
        </ui-field>
        <ui-field
          [label]="'documents.issue.line.code' | transloco"
          [error]="errors().code"
          required
        >
          <input
            uiInput
            type="text"
            [placeholder]="'documents.issue.line.codePlaceholder' | transloco"
            [formField]="field.code"
          />
        </ui-field>
        <ui-field
          [label]="'documents.issue.line.quantity' | transloco"
          [error]="errors().quantity"
          required
        >
          <input
            uiInput
            uiNonNegative
            type="number"
            step="any"
            [placeholder]="'documents.issue.line.quantityPlaceholder' | transloco"
            [formField]="field.quantity"
          />
        </ui-field>
        <ui-field
          [label]="'documents.issue.line.unitPrice' | transloco"
          [error]="errors().unitPrice"
          required
        >
          <input
            uiInput
            uiNonNegative
            type="number"
            step="any"
            [placeholder]="'documents.issue.line.unitPricePlaceholder' | transloco"
            [formField]="field.unitPrice"
          />
        </ui-field>
        <ui-field [label]="'documents.issue.line.taxPercent' | transloco">
          <ui-select [options]="taxOptions()" [formField]="field.taxPercent" />
        </ui-field>
      </div>
    </fieldset>
  `,
  styles: `
    :host {
      display: block;
      container-type: inline-size;
    }
    .line {
      margin: 0;
      padding: var(--if-space-3);
      min-inline-size: 0;
      background: var(--if-color-bg);
      border: 1px solid var(--if-color-border);
      border-radius: var(--if-radius-md);
    }
    .line__legend {
      float: inline-start;
      padding: 0;
      margin-block-end: var(--if-space-2);
      font-size: var(--if-font-size-sm);
      font-weight: var(--if-font-weight-bold);
      line-height: var(--if-control-min-height);
    }
    .line__header {
      display: flex;
      flex-wrap: wrap;
      gap: var(--if-space-1) var(--if-space-3);
      align-items: center;
      justify-content: flex-end;
      min-block-size: var(--if-control-min-height);
      margin-block-end: var(--if-space-2);
    }
    .line__amount {
      display: flex;
      gap: var(--if-space-2);
      align-items: baseline;
      margin: 0;
    }
    .line__muted {
      font-size: var(--if-font-size-sm);
      color: var(--if-color-text-muted);
    }
    .line__value {
      font-weight: var(--if-font-weight-bold);
      font-variant-numeric: tabular-nums;
    }
    .line__grid {
      clear: both;
      display: grid;
      gap: 0 var(--if-space-3);
      grid-template-columns: repeat(auto-fill, minmax(min(100%, 9rem), 1fr));
    }
    .line__description {
      grid-column: 1 / -1;
    }
    /* Tablet and the issue form beside the totals: description on top, the numbers in one row. */
    @container (min-width: 32rem) {
      .line__grid {
        grid-template-columns: 7rem 5rem minmax(8rem, 1fr) 7rem;
      }
    }
    /* Wide: the whole line in one row. */
    @container (min-width: 56rem) {
      .line__grid {
        grid-template-columns: minmax(12rem, 1fr) 7rem 5rem 10rem 7rem;
      }
      .line__description {
        grid-column: auto;
      }
    }
  `,
})
export class LineFieldsComponent {
  private readonly transloco = inject(TranslocoService);

  readonly line = input.required<FieldTree<InvoiceLineModel>>();
  /** Posición de la línea (desde 0); el texto la muestra desde 1. */
  readonly index = input.required<number>();
  /** Valor de la línea que calculó el servidor, si el cálculo está al día. */
  readonly amount = input<number | string | undefined>();
  readonly remove = output<void>();

  private readonly taxPercent = computed(() => this.line()().value().taxPercent);
  /** IVA rates of the select; a rate that came from a product or an invoice and is not one of them is kept. */
  protected readonly taxOptions = computed(() => {
    const options = IVA_PERCENTS.map((percent) => ({
      id: percent,
      name: this.transloco.translate(`${DOCUMENTS_I18N_SCOPE}.issue.line.taxOptions.${percent}`),
    }));
    const current = this.taxPercent();
    return current === '' || (IVA_PERCENTS as readonly string[]).includes(current)
      ? options
      : [...options, { id: current, name: `${current} %` }];
  });

  /** Mensaje bajo cada campo, solo después de que la persona lo tocó. */
  protected readonly errors = computed(() => {
    const line = this.line();
    const entries = (Object.keys(ERROR_KEYS) as LineField[]).map((name) => {
      const state = line[name]();
      const show = state.touched() && state.errors().length > 0;
      return [
        name,
        show ? this.transloco.translate(`${DOCUMENTS_I18N_SCOPE}.${ERROR_KEYS[name]}`) : null,
      ];
    });
    return Object.fromEntries(entries) as Record<LineField, string | null>;
  });
}
