import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/** Un mes del gráfico, con los textos ya traducidos. */
export interface MonthBar {
  key: string;
  /** Etiqueta corta bajo la barra («oct. 2026»). */
  label: string;
  total: number;
  /** Detalle del mes para el cursor y la tabla («oct. 2026: 55 documentos (…)»). */
  title: string;
  invoice: number;
  creditNote: number;
  debitNote: number;
  other: number;
}

export interface MonthChartLabels {
  /** Resumen del gráfico para el lector de pantalla. */
  chart: string;
  showTable: string;
  tableCaption: string;
  month: string;
  invoice: string;
  creditNote: string;
  debitNote: string;
  other: string;
  total: string;
}

/**
 * Barras de documentos por mes (componente de presentación). Una sola serie, así que no lleva leyenda: el título de
 * la tarjeta la nombra. Son barras de CSS (sin librería de gráficos, para no pesar en el bundle); el gráfico tiene
 * un nombre accesible con el resumen y los números exactos van en una tabla que se despliega.
 */
@Component({
  selector: 'feat-month-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="chart" role="img" [attr.aria-label]="labels().chart">
      @for (bar of bars(); track bar.key) {
        <div class="chart-column" [title]="bar.title">
          <span class="chart-value" aria-hidden="true">{{ bar.total > 0 ? bar.total : '' }}</span>
          <div class="chart-track" aria-hidden="true">
            <div class="chart-bar" [style.block-size.%]="height(bar.total)"></div>
          </div>
          <span class="chart-label" aria-hidden="true">{{ bar.label }}</span>
        </div>
      }
    </div>
    <details class="chart-details">
      <summary>{{ labels().showTable }}</summary>
      <div class="chart-table-scroll">
        <table class="chart-table">
          <caption>
            {{
              labels().tableCaption
            }}
          </caption>
          <thead>
            <tr>
              <th scope="col">{{ labels().month }}</th>
              <th scope="col">{{ labels().invoice }}</th>
              <th scope="col">{{ labels().creditNote }}</th>
              <th scope="col">{{ labels().debitNote }}</th>
              <th scope="col">{{ labels().other }}</th>
              <th scope="col">{{ labels().total }}</th>
            </tr>
          </thead>
          <tbody>
            @for (bar of bars(); track bar.key) {
              <tr>
                <th scope="row">{{ bar.label }}</th>
                <td>{{ bar.invoice }}</td>
                <td>{{ bar.creditNote }}</td>
                <td>{{ bar.debitNote }}</td>
                <td>{{ bar.other }}</td>
                <td>{{ bar.total }}</td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    </details>
  `,
  styles: `
    .chart {
      display: grid;
      grid-template-columns: repeat(12, minmax(0, 1fr));
      gap: 2px;
      align-items: end;
      block-size: 11rem;
    }
    .chart-column {
      display: flex;
      flex-direction: column;
      align-items: stretch;
      block-size: 100%;
      min-inline-size: 0;
    }
    .chart-value,
    .chart-label {
      font-size: var(--if-font-size-sm);
      color: var(--if-color-text-muted);
      text-align: center;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }
    .chart-value {
      min-block-size: 1.25em;
      color: var(--if-color-text);
    }
    .chart-track {
      display: flex;
      flex: 1;
      align-items: flex-end;
      border-block-end: 1px solid var(--if-color-border);
    }
    .chart-bar {
      inline-size: 100%;
      max-inline-size: 2.5rem;
      margin-inline: auto;
      background: var(--if-color-primary);
      border-radius: 4px 4px 0 0;
    }
    .chart-column:hover .chart-bar {
      background: var(--if-color-primary-hover);
    }
    .chart-details {
      margin-block-start: var(--if-space-2);
    }
    .chart-table-scroll {
      overflow-x: auto;
    }
    .chart-table {
      inline-size: 100%;
      border-collapse: collapse;
      font-size: var(--if-font-size-sm);
    }
    .chart-table caption {
      text-align: start;
      font-weight: var(--if-font-weight-medium);
    }
    .chart-table th,
    .chart-table td {
      padding: var(--if-space-1) var(--if-space-2);
      border-block-end: 1px solid var(--if-color-border);
      text-align: end;
    }
    .chart-table th:first-child {
      text-align: start;
    }
  `,
})
export class MonthChartComponent {
  readonly bars = input.required<readonly MonthBar[]>();
  readonly labels = input.required<MonthChartLabels>();

  private readonly max = computed(() => Math.max(0, ...this.bars().map((bar) => bar.total)));

  /** Alto de la barra en porcentaje del mes más alto; un mes con documentos nunca queda invisible. */
  protected height(total: number): number {
    const max = this.max();
    if (max === 0 || total <= 0) {
      return 0;
    }
    return Math.max(2, (total / max) * 100);
  }
}
