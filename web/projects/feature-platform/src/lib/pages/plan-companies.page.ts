import { HttpContext } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { Api, PlanCompanyResponse, PlanResponse, getPlan, listPlanCompanies } from '@impifact/api';
import { AppError, SKIP_ERROR_TOAST } from '@impifact/core';
import {
  BadgeComponent,
  CardComponent,
  PageTitleComponent,
  TableCellDirective,
  TableComponent,
  UiTableColumn,
  UiTableLabels,
} from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { map } from 'rxjs';
import { PlatformNavComponent } from '../components/platform-nav.component';
import { platformText, providePlatformI18n } from '../platform-i18n';

const DATE = new Intl.DateTimeFormat('es-CO', {
  day: 'numeric',
  month: 'long',
  year: 'numeric',
  timeZone: 'America/Bogota',
});

const silent = () => new HttpContext().set(SKIP_ERROR_TOAST, true);

/**
 * Página `/plataforma/planes/:id`: las empresas que tienen activo el plan (`listPlanCompanies`). El Api las da todas
 * juntas, sin paginar. Son datos de una sola pantalla: `rxResource`, sin store; el error se muestra en la tabla.
 */
@Component({
  selector: 'feat-plan-companies-page',
  imports: [
    BadgeComponent,
    CardComponent,
    RouterLink,
    TranslocoPipe,
    PageTitleComponent,
    TableComponent,
    TableCellDirective,
    PlatformNavComponent,
  ],
  providers: [providePlatformI18n()],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './plan-companies.page.html',
  styleUrl: './platform-page.css',
})
export class PlanCompaniesPage {
  private readonly api = inject(Api);
  private readonly transloco = inject(TranslocoService);

  /** Id del plan en la URL. */
  readonly id = input.required<string>();

  protected readonly plan = rxResource({
    params: () => Number(this.id()),
    stream: ({ params }) =>
      this.api
        .invoke(getPlan, { id: params }, silent())
        .pipe(map((response) => response.data as PlanResponse)),
  });
  protected readonly companies = rxResource({
    params: () => Number(this.id()),
    stream: ({ params }) =>
      this.api
        .invoke(listPlanCompanies, { id: params }, silent())
        .pipe(map((response) => response.user ?? [])),
  });

  protected readonly title = computed(() =>
    this.plan.hasValue()
      ? this.text('planCompanies.title', { name: this.plan.value().name ?? '' })
      : this.text('planCompanies.titleLoading'),
  );
  protected readonly notFound = computed(() => {
    const error = this.plan.error() ?? this.companies.error();
    return error instanceof AppError && error.status === 404;
  });
  protected readonly rows = computed(() =>
    this.companies.hasValue() ? this.companies.value() : [],
  );

  protected readonly columns = computed<UiTableColumn[]>(() => [
    { key: 'name', header: this.text('planCompanies.name') },
    { key: 'identification_number', header: this.text('planCompanies.nit') },
    { key: 'email', header: this.text('planCompanies.email') },
    { key: 'start_plan_date', header: this.text('planCompanies.startDate') },
    { key: 'state', header: this.text('planCompanies.state') },
  ]);
  protected readonly tableLabels = computed<UiTableLabels>(() => ({
    loading: this.text('planCompanies.loading'),
    emptyTitle: this.text('planCompanies.emptyTitle'),
    emptyDescription: this.text('planCompanies.emptyDescription'),
    errorTitle: this.text('planCompanies.errorTitle'),
    errorDescription: this.text('planCompanies.errorDescription'),
    retry: this.text('common.retry'),
    paginator: {
      navigation: this.text('common.paginator.navigation'),
      previous: this.text('common.paginator.previous'),
      next: this.text('common.paginator.next'),
      page: this.text('common.paginator.page'),
    },
  }));

  protected readonly trackByNit = (_: number, row: PlanCompanyResponse) =>
    row.identification_number;

  protected nit(row: PlanCompanyResponse): string {
    return row.dv ? `${row.identification_number}-${row.dv}` : (row.identification_number ?? '');
  }

  protected date(value: string | undefined): string {
    if (!value) {
      return '';
    }
    const parsed = new Date(value);
    return Number.isNaN(parsed.getTime()) ? value : DATE.format(parsed);
  }

  protected reload(): void {
    this.plan.reload();
    this.companies.reload();
  }

  protected text(key: string, params?: Record<string, unknown>): string {
    return platformText(this.transloco, key, params);
  }
}
