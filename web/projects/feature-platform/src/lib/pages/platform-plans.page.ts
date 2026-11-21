import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormField, submit } from '@angular/forms/signals';
import { RouterLink } from '@angular/router';
import { AppError, ToastStore } from '@impifact/core';
import {
  BadgeComponent,
  ButtonComponent,
  CardComponent,
  DialogComponent,
  DigitsOnlyDirective,
  FieldComponent,
  HelpComponent,
  InputDirective,
  PageTitleComponent,
  SelectComponent,
  TableCellDirective,
  TableComponent,
  UiTableColumn,
  UiTableLabels,
} from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { PlatformNavComponent } from '../components/platform-nav.component';
import {
  PLAN_FIELDS,
  PLAN_PERIODS,
  PlanModel,
  QUANTITY_FIELDS,
  createPlanForm,
  emptyPlan,
  fieldMessage,
  newPlanCommand,
  planCommand,
  planModel,
  serverFieldErrors,
} from '../forms/platform.forms';
import { platformText, providePlatformI18n } from '../platform-i18n';
import { PlatformPlan, PlatformPlansStore } from '../state/platform-plans.store';

const NUMBER = new Intl.NumberFormat('es-CO');

/**
 * Página `/plataforma/planes`: los planes de la plataforma (el Api los da todos juntos, sin paginar) y un diálogo
 * para crear uno con `createPlan` (`POST /plans`; el Api le asigna el id, no se pide) o editarlo con `savePlan`
 * (`PUT /plans/{id}`; el id se muestra sin poder cambiarlo). Cada campo tiene un ejemplo y un tooltip que explica qué
 * poner. Los errores por campo del Api (por ejemplo, el nombre repetido) aparecen junto a su campo.
 */
@Component({
  selector: 'feat-platform-plans-page',
  imports: [
    BadgeComponent,
    CardComponent,
    FormField,
    RouterLink,
    TranslocoPipe,
    ButtonComponent,
    DialogComponent,
    DigitsOnlyDirective,
    FieldComponent,
    HelpComponent,
    InputDirective,
    PageTitleComponent,
    SelectComponent,
    TableComponent,
    TableCellDirective,
    PlatformNavComponent,
  ],
  providers: [providePlatformI18n()],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './platform-plans.page.html',
  styleUrl: './platform-page.css',
})
export class PlatformPlansPage {
  protected readonly store = inject(PlatformPlansStore);
  private readonly transloco = inject(TranslocoService);
  private readonly toasts = inject(ToastStore);

  protected readonly periodOptions = computed(() =>
    PLAN_PERIODS.map((id) => ({ id, name: this.periodLabel(id) })),
  );
  protected readonly quantityFields = QUANTITY_FIELDS;

  /** Plan que se edita; `null` al crear uno nuevo. */
  protected readonly editing = signal<PlatformPlan | null>(null);
  protected readonly dialogOpen = signal(false);
  private readonly model = signal<PlanModel>(emptyPlan());
  protected readonly planForm = createPlanForm(this.model, () => this.editing() !== null);
  protected readonly submitting = computed(() => this.planForm().submitting());
  protected readonly generalError = signal<string | null>(null);

  protected readonly dialogTitle = computed(() => {
    const plan = this.editing();
    return plan
      ? this.text('planForm.editTitle', { name: plan.name })
      : this.text('planForm.newTitle');
  });

  protected readonly errors = computed(() => {
    const translate = (key: string, params?: Record<string, unknown>) => this.text(key, params);
    return Object.fromEntries(
      PLAN_FIELDS.map((name) => [name, fieldMessage(this.planForm[name](), translate)]),
    ) as Record<(typeof PLAN_FIELDS)[number], string | null>;
  });

  protected readonly columns = computed<UiTableColumn[]>(() => [
    { key: 'id', header: this.text('plans.id'), align: 'end' },
    { key: 'name', header: this.text('plans.name') },
    { key: 'period', header: this.text('plans.period') },
    { key: 'qtyInvoice', header: this.text('plans.invoice'), align: 'end' },
    { key: 'qtyPayroll', header: this.text('plans.payroll'), align: 'end' },
    { key: 'qtyRadian', header: this.text('plans.radian'), align: 'end' },
    { key: 'qtyDs', header: this.text('plans.ds'), align: 'end' },
    { key: 'active', header: this.text('plans.state') },
    { key: 'actions', header: this.text('common.actions') },
  ]);
  protected readonly tableLabels = computed<UiTableLabels>(() => ({
    loading: this.text('plans.loading'),
    emptyTitle: this.text('plans.emptyTitle'),
    emptyDescription: this.text('plans.emptyDescription'),
    errorTitle: this.text('plans.errorTitle'),
    errorDescription: this.text('plans.errorDescription'),
    retry: this.text('common.retry'),
    paginator: {
      navigation: this.text('common.paginator.navigation'),
      previous: this.text('common.paginator.previous'),
      next: this.text('common.paginator.next'),
      page: this.text('common.paginator.page'),
    },
  }));

  protected readonly trackById = (_: number, row: PlatformPlan) => row.id;

  constructor() {
    void this.store.load();
  }

  protected periodLabel(period: number): string {
    return PLAN_PERIODS.includes(period)
      ? this.text(`periods.${period}`)
      : this.text('periods.unknown', { period });
  }

  protected quantity(value: number | null): string {
    return value === null ? '' : NUMBER.format(value);
  }

  protected rowText(key: string, row: PlatformPlan): string {
    return this.text(key, { name: row.name });
  }

  protected openNew(): void {
    this.open(null, emptyPlan());
  }

  protected openEdit(plan: PlatformPlan): void {
    this.open(plan, planModel(plan));
  }

  private open(plan: PlatformPlan | null, model: PlanModel): void {
    this.editing.set(plan);
    this.generalError.set(null);
    this.planForm().reset(model);
    this.dialogOpen.set(true);
  }

  protected async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    this.generalError.set(null);
    await submit(this.planForm, {
      action: async () => {
        const model = this.model();
        try {
          if (this.editing()) {
            await this.store.save(Number(model.id), planCommand(model));
          } else {
            await this.store.create(newPlanCommand(model));
          }
        } catch (error) {
          return this.showError(error);
        }
        this.dialogOpen.set(false);
        this.toasts.show({ kind: 'success', text: this.text('plans.saved') });
        void this.store.load();
        return undefined;
      },
    });
  }

  private showError(error: unknown) {
    if (!(error instanceof AppError)) {
      this.generalError.set(this.text('common.unexpected'));
      return undefined;
    }
    const onFields = serverFieldErrors(this.planForm, PLAN_FIELDS, error);
    if (onFields.length === 0) {
      this.generalError.set(error.message || this.text('common.unexpected'));
    }
    return onFields;
  }

  protected text(key: string, params?: Record<string, unknown>): string {
    return platformText(this.transloco, key, params);
  }
}
