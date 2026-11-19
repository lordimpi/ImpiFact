import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { FieldTree, FormField } from '@angular/forms/signals';
import {
  ButtonComponent,
  FieldComponent,
  InputDirective,
  NonNegativeDirective,
  SelectDirective,
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
    SelectDirective,
    CopCurrencyPipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let field = line();
    <fieldset class="issue-line">
      <legend>{{ 'documents.issue.line.title' | transloco: { number: index() + 1 } }}</legend>
      <div class="issue-grid">
        <ui-field
          [label]="'documents.issue.line.code' | transloco"
          [error]="errors().code"
          required
        >
          <input uiInput type="text" [formField]="field.code" />
        </ui-field>
        <ui-field
          class="issue-wide"
          [label]="'documents.issue.line.description' | transloco"
          [error]="errors().description"
          required
        >
          <input uiInput type="text" [formField]="field.description" />
        </ui-field>
        <ui-field
          [label]="'documents.issue.line.quantity' | transloco"
          [error]="errors().quantity"
          required
        >
          <input uiInput uiNonNegative type="number" step="any" [formField]="field.quantity" />
        </ui-field>
        <ui-field
          [label]="'documents.issue.line.unitPrice' | transloco"
          [error]="errors().unitPrice"
          required
        >
          <input uiInput uiNonNegative type="number" step="any" [formField]="field.unitPrice" />
        </ui-field>
        <ui-field [label]="'documents.issue.line.taxPercent' | transloco">
          <select uiSelect [formField]="field.taxPercent">
            @for (option of ivaPercents; track option) {
              <option [value]="option">
                {{ 'documents.issue.line.taxOptions.' + option | transloco }}
              </option>
            }
            @if (!ivaPercents.includes($any(taxPercent()))) {
              <option [value]="taxPercent()">{{ taxPercent() }} %</option>
            }
          </select>
        </ui-field>
      </div>
      <p class="issue-line__amount">
        <span class="doc-muted">{{ 'documents.issue.line.amount' | transloco }}</span>
        <span class="doc-amount">{{ amount() | cop }}</span>
      </p>
      <ui-button
        variant="text"
        size="small"
        [label]="'documents.issue.line.remove' | transloco: { number: index() + 1 }"
        (pressed)="remove.emit()"
      />
    </fieldset>
  `,
  styles: `
    .issue-line {
      margin: var(--if-space-3) 0 0;
      padding: var(--if-space-3);
      border: 1px solid var(--if-color-border);
      border-radius: var(--if-radius-sm);
      min-inline-size: 0;
    }
    .issue-line legend {
      font-weight: var(--if-font-weight-bold);
      padding-inline: var(--if-space-1);
    }
    .issue-grid {
      display: grid;
      gap: 0 var(--if-space-3);
      grid-template-columns: repeat(auto-fill, minmax(10rem, 1fr));
    }
    .issue-wide {
      grid-column: 1 / -1;
    }
    .issue-line__amount {
      display: flex;
      gap: var(--if-space-2);
      justify-content: space-between;
      margin: 0 0 var(--if-space-2);
    }
    .doc-muted {
      color: var(--if-color-text-muted);
    }
    .doc-amount {
      text-align: end;
      font-variant-numeric: tabular-nums;
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

  protected readonly ivaPercents = IVA_PERCENTS;
  protected readonly taxPercent = computed(() => this.line()().value().taxPercent);

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
