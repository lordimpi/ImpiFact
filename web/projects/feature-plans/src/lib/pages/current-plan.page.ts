import { HttpContext } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import {
  Api,
  DashboardPlanResponse,
  DashboardQuotaResponse,
  getDashboardSummary,
} from '@impifact/api';
import { SKIP_ERROR_TOAST } from '@impifact/core';
import {
  EmptyStateComponent,
  ErrorStateComponent,
  LoadingComponent,
  MeterComponent,
  PageTitleComponent,
} from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { plansText, providePlansI18n } from '../plans-i18n';

const KNOWN_KINDS = ['invoice', 'payroll', 'equivalent_document', 'support_document', 'events'];
const KNOWN_PERIODS = ['monthly', 'yearly', 'package', 'unlimited'];
const NUMBER = new Intl.NumberFormat('es-CO');
const DATE = new Intl.DateTimeFormat('es-CO', {
  day: 'numeric',
  month: 'long',
  year: 'numeric',
  timeZone: 'America/Bogota',
});

/** Cupo de una familia de documentos listo para mostrar. */
interface QuotaView {
  kind: string;
  label: string;
  used: number;
  limit: number | null;
  text: string;
}

/**
 * Página `/plan`: el plan vigente de la empresa activa y lo que queda por familia de documentos en el periodo. Lee
 * el `plan` de `getDashboardSummary` (usado, límite, restante y fin del periodo ya calculados por el Api); no hace
 * cuentas en el navegador. Son datos de una sola pantalla: `rxResource` en el componente, sin store (sección 4).
 */
@Component({
  selector: 'feat-current-plan-page',
  imports: [
    TranslocoPipe,
    EmptyStateComponent,
    ErrorStateComponent,
    LoadingComponent,
    MeterComponent,
    PageTitleComponent,
  ],
  providers: [providePlansI18n()],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './current-plan.page.html',
  styleUrl: './current-plan.page.css',
})
export class CurrentPlanPage {
  private readonly api = inject(Api);
  private readonly transloco = inject(TranslocoService);

  /** El error lo muestra la página con su reintento; core no abre un aviso. */
  protected readonly summary = rxResource({
    stream: () =>
      this.api.invoke(getDashboardSummary, {}, new HttpContext().set(SKIP_ERROR_TOAST, true)),
  });

  protected readonly plan = computed<DashboardPlanResponse | null>(() =>
    this.summary.hasValue() ? (this.summary.value().plan ?? null) : null,
  );

  protected readonly quotas = computed<QuotaView[]>(() =>
    (this.plan()?.quotas ?? []).map((quota) => this.quotaView(quota)),
  );

  protected readonly status = computed(() => {
    const plan = this.plan();
    if (!plan) {
      return '';
    }
    if (plan.expired) {
      return this.text('summary.expired');
    }
    return this.text(plan.is_active ? 'summary.active' : 'summary.inactive');
  });

  protected periodLabel(period: string | undefined): string {
    return KNOWN_PERIODS.includes(period ?? '') ? this.text(`periods.${period}`) : (period ?? '');
  }

  protected date(value: string | null | undefined): string {
    if (!value) {
      return this.text('summary.noPeriodEnd');
    }
    const parsed = new Date(value);
    return Number.isNaN(parsed.getTime()) ? value : DATE.format(parsed);
  }

  private quotaView(quota: DashboardQuotaResponse): QuotaView {
    const kind = quota.kind ?? '';
    const used = Number(quota.used ?? 0);
    const limit = quota.limit === null || quota.limit === undefined ? null : Number(quota.limit);
    const text =
      limit === null
        ? this.text('quotas.usedNoLimit', { used: NUMBER.format(used) })
        : this.text('quotas.used', {
            used: NUMBER.format(used),
            limit: NUMBER.format(limit),
            remaining: NUMBER.format(Number(quota.remaining ?? 0)),
          });
    const label = KNOWN_KINDS.includes(kind)
      ? this.text(`kinds.${kind}`)
      : this.text('kinds.other', { kind });
    return { kind, label, used, limit, text };
  }

  protected text(key: string, params?: Record<string, unknown>): string {
    return plansText(this.transloco, key, params);
  }
}
