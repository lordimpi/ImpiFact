import { HttpContext } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormField, submit } from '@angular/forms/signals';
import { Api, assignCompanyPlan } from '@impifact/api';
import { AppError, SKIP_ERROR_TOAST } from '@impifact/core';
import {
  ButtonComponent,
  FieldComponent,
  InputDirective,
  PageTitleComponent,
  SelectDirective,
} from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { PlatformNavComponent } from '../components/platform-nav.component';
import {
  AssignModel,
  createAssignForm,
  emptyAssign,
  fieldMessage,
  serverFieldErrors,
} from '../forms/platform.forms';
import { platformText, providePlatformI18n } from '../platform-i18n';
import { PlatformPlansStore } from '../state/platform-plans.store';

const DATE = new Intl.DateTimeFormat('es-CO', {
  day: 'numeric',
  month: 'long',
  year: 'numeric',
  timeZone: 'America/Bogota',
});

const ASSIGN_FIELDS: readonly (keyof AssignModel & string)[] = ['nit', 'plan_id'];

/**
 * Página `/plataforma/empresas`: asigna un plan a una empresa por su NIT (`assignCompanyPlan`). El Api aún no lista
 * las empresas de la plataforma, así que se escribe el NIT; los planes salen del store del feature.
 */
@Component({
  selector: 'feat-assign-plan-page',
  imports: [
    FormField,
    TranslocoPipe,
    ButtonComponent,
    FieldComponent,
    InputDirective,
    PageTitleComponent,
    SelectDirective,
    PlatformNavComponent,
  ],
  providers: [providePlatformI18n()],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './assign-plan.page.html',
  styleUrl: './platform-page.css',
})
export class AssignPlanPage {
  protected readonly store = inject(PlatformPlansStore);
  private readonly api = inject(Api);
  private readonly transloco = inject(TranslocoService);

  private readonly model = signal<AssignModel>(emptyAssign());
  protected readonly assignForm = createAssignForm(this.model);
  protected readonly submitting = computed(() => this.assignForm().submitting());
  protected readonly generalError = signal<string | null>(null);
  protected readonly done = signal<string | null>(null);

  protected readonly errors = computed(() => {
    const translate = (key: string, params?: Record<string, unknown>) => this.text(key, params);
    return {
      nit: fieldMessage(this.assignForm.nit(), translate),
      plan_id: fieldMessage(this.assignForm.plan_id(), translate),
    };
  });

  constructor() {
    if (!this.store.loaded()) {
      void this.store.load();
    }
  }

  protected async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    this.generalError.set(null);
    this.done.set(null);
    await submit(this.assignForm, {
      action: async () => {
        const { nit, plan_id } = this.model();
        try {
          const result = await firstValueFrom(
            this.api.invoke(
              assignCompanyPlan,
              { nit, body: { plan_id: Number(plan_id) } },
              new HttpContext().set(SKIP_ERROR_TOAST, true),
            ),
          );
          const plan =
            result.plan?.name ??
            this.store.plans().find((item) => String(item.id) === plan_id)?.name;
          const start = result.start_plan_date ? DATE.format(new Date(result.start_plan_date)) : '';
          this.done.set(this.text('assign.done', { plan: plan ?? plan_id, nit, date: start }));
        } catch (error) {
          return this.showError(error);
        }
        return undefined;
      },
    });
  }

  private showError(error: unknown) {
    if (!(error instanceof AppError)) {
      this.generalError.set(this.text('common.unexpected'));
      return undefined;
    }
    if (error.code === 'company.not_found') {
      this.generalError.set(this.text('assign.companyNotFound'));
      return undefined;
    }
    if (error.code === 'plan.not_found') {
      this.generalError.set(this.text('assign.planNotFound'));
      return undefined;
    }
    const onFields = serverFieldErrors(this.assignForm, ASSIGN_FIELDS, error);
    if (onFields.length === 0) {
      this.generalError.set(error.message || this.text('common.unexpected'));
    }
    return onFields;
  }

  protected text(key: string, params?: Record<string, unknown>): string {
    return platformText(this.transloco, key, params);
  }
}
