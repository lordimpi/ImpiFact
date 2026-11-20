import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { ButtonComponent, LoadingComponent } from '@impifact/ui';
import { TranslocoPipe } from '@jsverse/transloco';
import { CopCurrencyPipe, formatPercent } from './cop-currency.pipe';

/** Totales del documento como los da el Api (`legal_monetary_totals` o `totals`). */
export interface TotalsView {
  line_extension_amount?: number | string;
  allowance_total_amount?: number | string;
  charge_total_amount?: number | string;
  tax_exclusive_amount?: number | string;
  tax_inclusive_amount?: number | string;
  pre_paid_amount?: number | string;
  payable_amount?: number | string;
}

/** Un impuesto agrupado por tipo y porcentaje. */
export interface TaxView {
  tax_id?: number | string | null;
  percent?: number | string | null;
  tax_amount?: number | string;
}

export type TotalsState = 'ready' | 'loading' | 'error' | 'empty';

/**
 * Panel de totales (presentación). Solo muestra valores que calculó el servidor; nunca suma ni redondea. Los
 * cambios de estado se anuncian por `aria-live`.
 */
@Component({
  selector: 'feat-totals-panel',
  imports: [TranslocoPipe, ButtonComponent, LoadingComponent, CopCurrencyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div aria-live="polite">
      @switch (state()) {
        @case ('loading') {
          <ui-loading [label]="'documents.totals.calculating' | transloco" [lines]="3" />
        }
        @case ('error') {
          <p class="totals__error" role="alert">{{ 'documents.totals.error' | transloco }}</p>
          <ui-button
            variant="secondary"
            size="small"
            [label]="'documents.totals.retry' | transloco"
            (pressed)="retry.emit()"
          />
        }
        @case ('empty') {
          <p class="totals__muted">{{ 'documents.totals.empty' | transloco }}</p>
        }
        @default {
          @if (totals(); as totals) {
            <dl class="totals">
              <dt>{{ 'documents.totals.lineExtension' | transloco }}</dt>
              <dd>{{ totals.line_extension_amount | cop }}</dd>
              @if (nonZero(totals.allowance_total_amount)) {
                <dt>{{ 'documents.totals.allowances' | transloco }}</dt>
                <dd>{{ totals.allowance_total_amount | cop }}</dd>
              }
              @if (nonZero(totals.charge_total_amount)) {
                <dt>{{ 'documents.totals.charges' | transloco }}</dt>
                <dd>{{ totals.charge_total_amount | cop }}</dd>
              }
              <dt>{{ 'documents.totals.taxExclusive' | transloco }}</dt>
              <dd>{{ totals.tax_exclusive_amount | cop }}</dd>
              @for (tax of taxes(); track $index) {
                <dt>
                  {{
                    'documents.totals.tax'
                      | transloco
                        : {
                            name: 'documents.taxes.' + taxKey(tax.tax_id) | transloco,
                            percent: percent(tax.percent),
                          }
                  }}
                </dt>
                <dd>{{ tax.tax_amount | cop }}</dd>
              }
              <dt>{{ 'documents.totals.taxInclusive' | transloco }}</dt>
              <dd>{{ totals.tax_inclusive_amount | cop }}</dd>
              @if (nonZero(totals.pre_paid_amount)) {
                <dt>{{ 'documents.totals.prepaid' | transloco }}</dt>
                <dd>{{ totals.pre_paid_amount | cop }}</dd>
              }
              <dt class="totals__payable">{{ 'documents.totals.payable' | transloco }}</dt>
              <dd class="totals__payable" data-testid="payable">
                {{ totals.payable_amount | cop }}
              </dd>
            </dl>
          }
        }
      }
    </div>
  `,
  styles: `
    .totals {
      display: grid;
      grid-template-columns: 1fr auto;
      gap: var(--if-space-2) 0;
      margin: 0;
      font-size: var(--if-font-size-sm);
    }
    .totals dt {
      color: var(--if-color-text-muted);
    }
    .totals dd {
      margin: 0;
      padding-inline-start: var(--if-space-3);
      text-align: end;
      font-variant-numeric: tabular-nums;
      white-space: nowrap;
    }
    .totals__payable {
      padding-block-start: var(--if-space-3);
      border-block-start: 1px solid var(--if-color-border);
      font-weight: var(--if-font-weight-bold);
    }
    dt.totals__payable {
      display: flex;
      align-items: flex-end;
      color: var(--if-color-text);
      font-size: var(--if-font-size-base);
    }
    dd.totals__payable {
      font-size: var(--if-font-size-lg);
      color: var(--if-color-text);
    }
    .totals__muted {
      margin: 0;
      color: var(--if-color-text-muted);
    }
    .totals__error {
      margin: 0 0 var(--if-space-2);
      color: var(--if-color-danger);
    }
  `,
})
export class TotalsPanelComponent {
  readonly totals = input<TotalsView | null | undefined>();
  readonly taxes = input<readonly TaxView[]>([]);
  readonly state = input<TotalsState>('ready');
  readonly retry = output<void>();

  /** Solo compara con cero para decidir si se muestra la fila; no opera con el valor. */
  protected readonly percent = formatPercent;

  protected nonZero(value: number | string | undefined): boolean {
    return value !== undefined && Number(value) !== 0;
  }

  protected taxKey(id: number | string | null | undefined): string {
    return id === 1 || id === '1' || id === 4 || id === '4' ? String(id) : 'unknown';
  }
}
