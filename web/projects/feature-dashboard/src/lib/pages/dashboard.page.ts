import { HttpContext } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import {
  Api,
  DashboardCertificateResponse,
  DashboardMonthResponse,
  DashboardQuotaResponse,
  DashboardResolutionResponse,
  getDashboardSummary,
} from '@impifact/api';
import { SKIP_ERROR_TOAST, SessionStore } from '@impifact/core';
import {
  ErrorStateComponent,
  LoadingComponent,
  MeterComponent,
  PageTitleComponent,
} from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import {
  MonthBar,
  MonthChartComponent,
  MonthChartLabels,
} from '../components/month-chart.component';
import { dashboardText, provideDashboardI18n } from '../dashboard-i18n';

const KNOWN_KINDS = ['invoice', 'payroll', 'equivalent_document', 'support_document', 'events'];
const KNOWN_STATUSES = [
  'Received',
  'Queued',
  'Sent',
  'Accepted',
  'AcceptedWithWarnings',
  'Rejected',
  'Unconfirmed',
  'Failed',
  'ContingencyPending',
];
/** Días desde los que el certificado se marca para renovar. */
const CERTIFICATE_WARNING_DAYS = 30;

const NUMBER = new Intl.NumberFormat('es-CO');
const LONG_DATE = new Intl.DateTimeFormat('es-CO', {
  day: 'numeric',
  month: 'long',
  year: 'numeric',
  timeZone: 'America/Bogota',
});
// Los meses llegan como `YYYY-MM`: se leen en UTC para que el día 1 no caiga en el mes anterior.
const MONTH_LONG = new Intl.DateTimeFormat('es-CO', {
  month: 'long',
  year: 'numeric',
  timeZone: 'UTC',
});
const MONTH_SHORT = new Intl.DateTimeFormat('es-CO', { month: 'short', timeZone: 'UTC' });

const toNumber = (value: number | string | null | undefined) => Number(value ?? 0);

function monthDate(month: string | undefined): Date | null {
  const match = /^(\d{4})-(\d{2})$/.exec(month ?? '');
  return match ? new Date(Date.UTC(Number(match[1]), Number(match[2]) - 1, 1)) : null;
}

/**
 * Tablero de `/` para el propietario y el vendedor: documentos del mes por estado, los últimos 12 meses, consumo del
 * plan, días que le quedan al certificado y resoluciones por agotarse. Todo sale de `getDashboardSummary`, armado en
 * el servidor; la página solo da formato. Son datos de una sola pantalla: `rxResource`, sin store (sección 4).
 */
@Component({
  selector: 'feat-dashboard-page',
  imports: [
    RouterLink,
    TranslocoPipe,
    ErrorStateComponent,
    LoadingComponent,
    MeterComponent,
    PageTitleComponent,
    MonthChartComponent,
  ],
  providers: [provideDashboardI18n()],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dashboard.page.html',
  styleUrl: './dashboard.page.css',
})
export class DashboardPage {
  private readonly api = inject(Api);
  private readonly transloco = inject(TranslocoService);
  private readonly session = inject(SessionStore);

  /** El error lo muestra la página con su reintento; core no abre un aviso. */
  protected readonly summary = rxResource({
    stream: () =>
      this.api.invoke(getDashboardSummary, {}, new HttpContext().set(SKIP_ERROR_TOAST, true)),
  });
  private readonly data = computed(() => (this.summary.hasValue() ? this.summary.value() : null));

  protected readonly canIssue = computed(() => this.session.hasScope('documents:write'));
  protected readonly isOwner = computed(() => this.session.hasRole('owner'));

  protected readonly monthTitle = computed(() => {
    const date = monthDate(this.data()?.current_month?.month);
    return this.text('month.title', { month: date ? MONTH_LONG.format(date) : '' });
  });
  protected readonly monthTotal = computed(() =>
    NUMBER.format(toNumber(this.data()?.current_month?.total)),
  );
  protected readonly statuses = computed(() =>
    (this.data()?.current_month?.by_status ?? []).map((item) => ({
      status: item.status ?? '',
      label: KNOWN_STATUSES.includes(item.status ?? '')
        ? this.text(`statuses.${item.status}`)
        : this.text('statuses.unknown', { status: item.status ?? '' }),
      count: NUMBER.format(toNumber(item.count)),
    })),
  );

  protected readonly bars = computed<MonthBar[]>(() =>
    (this.data()?.months ?? []).map((month) => this.bar(month)),
  );
  protected readonly hasHistory = computed(() => this.bars().some((bar) => bar.total > 0));
  protected readonly chartLabels = computed<MonthChartLabels>(() => {
    const top = this.bars().reduce<MonthBar | null>(
      (best, bar) => (best === null || bar.total > best.total ? bar : best),
      null,
    );
    return {
      chart:
        top && top.total > 0
          ? this.text('history.chartLabel', { month: top.label, total: NUMBER.format(top.total) })
          : this.text('history.chartEmpty'),
      showTable: this.text('history.showTable'),
      tableCaption: this.text('history.tableCaption'),
      month: this.text('history.monthColumn'),
      invoice: this.text('history.invoice'),
      creditNote: this.text('history.creditNote'),
      debitNote: this.text('history.debitNote'),
      other: this.text('history.other'),
      total: this.text('history.total'),
    };
  });

  protected readonly plan = computed(() => this.data()?.plan ?? null);
  protected readonly quotas = computed(() =>
    (this.plan()?.quotas ?? []).map((quota) => this.quota(quota)),
  );
  protected readonly planPeriod = computed(() => {
    const plan = this.plan();
    if (!plan) {
      return '';
    }
    if (plan.expired) {
      return this.text('plan.expired');
    }
    return plan.period_end
      ? this.text('plan.periodEnd', { date: this.date(plan.period_end) })
      : this.text('plan.noPeriodEnd');
  });

  protected readonly certificate = computed(() => this.data()?.certificate ?? null);
  protected readonly certificateText = computed(() => {
    const certificate = this.certificate();
    return certificate ? this.certificateLine(certificate) : '';
  });
  protected readonly certificateWarning = computed(() => {
    const certificate = this.certificate();
    return (
      !certificate ||
      certificate.expired === true ||
      toNumber(certificate.days_left) <= CERTIFICATE_WARNING_DAYS
    );
  });

  protected readonly resolutions = computed(() =>
    (this.data()?.resolutions ?? []).map((resolution) => this.resolution(resolution)),
  );

  private bar(month: DashboardMonthResponse): MonthBar {
    const date = monthDate(month.month);
    const label = date
      ? `${MONTH_SHORT.format(date)} ${date.getUTCFullYear()}`
      : (month.month ?? '');
    const values = {
      invoice: toNumber(month.invoice),
      creditNote: toNumber(month.credit_note),
      debitNote: toNumber(month.debit_note),
      other: toNumber(month.other),
      total: toNumber(month.total),
    };
    return {
      key: month.month ?? label,
      label,
      ...values,
      title: this.text('history.barTitle', {
        month: label,
        total: values.total,
        invoice: values.invoice,
        credit: values.creditNote,
        debit: values.debitNote,
        other: values.other,
      }),
    };
  }

  private quota(quota: DashboardQuotaResponse) {
    const kind = quota.kind ?? '';
    const used = toNumber(quota.used);
    const limit = quota.limit === null || quota.limit === undefined ? null : Number(quota.limit);
    return {
      kind,
      label: KNOWN_KINDS.includes(kind)
        ? this.text(`kinds.${kind}`)
        : this.text('kinds.other', { kind }),
      used,
      limit,
      text:
        limit === null
          ? this.text('plan.usedNoLimit', { used: NUMBER.format(used) })
          : this.text('plan.used', {
              used: NUMBER.format(used),
              limit: NUMBER.format(limit),
              remaining: NUMBER.format(toNumber(quota.remaining)),
            }),
    };
  }

  private certificateLine(certificate: DashboardCertificateResponse): string {
    const date = this.date(certificate.not_after);
    const days = toNumber(certificate.days_left);
    if (certificate.expired || days < 0) {
      return this.text('certificate.expired', { date });
    }
    if (days === 0) {
      return this.text('certificate.today', { date });
    }
    if (days === 1) {
      return this.text('certificate.oneDayLeft', { date });
    }
    return this.text('certificate.daysLeft', { days: NUMBER.format(days), date });
  }

  private resolution(resolution: DashboardResolutionResponse) {
    const kind = resolution.kind ?? '';
    const notes: string[] = [];
    if (resolution.numbers_low) {
      notes.push(
        this.text('resolutions.remaining', {
          count: NUMBER.format(toNumber(resolution.remaining_numbers)),
        }),
      );
    }
    if (resolution.expired) {
      notes.push(this.text('resolutions.expired'));
    } else if (resolution.expiring && resolution.days_left !== null) {
      notes.push(
        this.text('resolutions.daysLeft', {
          days: NUMBER.format(toNumber(resolution.days_left)),
        }),
      );
    }
    return {
      id: resolution.id ?? `${resolution.prefix}-${resolution.resolution_number}`,
      title: this.text('resolutions.item', {
        kind: KNOWN_KINDS.includes(kind) ? this.text(`kinds.${kind}`) : kind,
        prefix: resolution.prefix ?? '',
        number: resolution.resolution_number ?? this.text('resolutions.noNumber'),
      }),
      notes,
    };
  }

  private date(value: string | null | undefined): string {
    if (!value) {
      return '';
    }
    const parsed = new Date(value);
    return Number.isNaN(parsed.getTime()) ? value : LONG_DATE.format(parsed);
  }

  protected text(key: string, params?: Record<string, unknown>): string {
    return dashboardText(this.transloco, key, params);
  }
}
