import { HttpContext } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormField, submit } from '@angular/forms/signals';
import { Router } from '@angular/router';
import {
  Api,
  CompanyActivationResponse,
  PlatformCompanyItem,
  assignCompanyPlan,
  deactivatePlatformCompany,
  reactivatePlatformCompany,
} from '@impifact/api';
import { AppError, SKIP_ERROR_TOAST, ToastStore } from '@impifact/core';
import {
  BadgeComponent,
  ButtonComponent,
  CardComponent,
  CardFooterDirective,
  DialogComponent,
  FieldComponent,
  InputDirective,
  MenuButtonComponent,
  PageTitleComponent,
  SelectComponent,
  TableCellDirective,
  TableComponent,
  UiConfirm,
  UiMenuItem,
  UiTableColumn,
  UiTableLabels,
} from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import {
  CompanyUserDialogComponent,
  UserCompany,
} from '../components/company-user-dialog.component';
import { PlatformNavComponent } from '../components/platform-nav.component';
import {
  AssignModel,
  createAssignForm,
  emptyAssign,
  fieldMessage,
  serverFieldErrors,
} from '../forms/platform.forms';
import { platformText, providePlatformI18n } from '../platform-i18n';
import { PlatformCompaniesStore } from '../state/platform-companies.store';
import { PlatformPlansStore } from '../state/platform-plans.store';

const DATE = new Intl.DateTimeFormat('es-CO', {
  day: 'numeric',
  month: 'long',
  year: 'numeric',
  timeZone: 'America/Bogota',
});

const ASSIGN_FIELDS: readonly (keyof AssignModel & string)[] = ['plan_id'];

/** Ids of the options of the «Acciones» menu of every row. */
type RowAction = 'assign' | 'user' | 'users' | 'deactivate' | 'reactivate';

const FORBIDDEN = 403;
const NOT_FOUND = 404;
const CONFLICT = 409;

/**
 * Página `/plataforma/empresas`: las empresas de la plataforma con su plan actual (`listPlatformCompanies`), con
 * búsqueda en el servidor por nombre o NIT y «Cargar más» por cursor. Desde cada fila se asigna un plan
 * (`assignCompanyPlan`) en un diálogo: la empresa se elige en la lista, ya no se escribe su NIT. Los planes salen del
 * store del feature. También desde cada fila se crea el primer dueño o un vendedor de la empresa, «Usuarios» abre
 * la página de sus usuarios (`/plataforma/empresas/:nit/usuarios`, de feature-companies), y «Nueva empresa»
 * lleva al alta (`/plataforma/empresas/nueva`); al volver, la lista se pide de nuevo.
 *
 * «Desactivar» (`deactivatePlatformCompany`) y «Reactivar» (`reactivatePlatformCompany`) preguntan antes con el diálogo
 * de confirmación de ui (`UiConfirm`, cargado al primer uso), que explica los efectos: no emite, sus API keys se revocan,
 * sus usuarios pierden el acceso y los documentos se conservan. La fila muestra el estado que devuelve el Api, sin
 * recargar a mano; un 404 o un 409 vuelven a pedir la lista. Los errores salen en un aviso (toast).
 *
 * The row actions live in one «Acciones» menu per row (`ui-menu-button`), so the table fits on one line per row at
 * 1366 px; the NIT column never wraps.
 */
@Component({
  selector: 'feat-platform-companies-page',
  imports: [
    BadgeComponent,
    CardComponent,
    CardFooterDirective,
    FormField,
    TranslocoPipe,
    ButtonComponent,
    DialogComponent,
    FieldComponent,
    InputDirective,
    MenuButtonComponent,
    PageTitleComponent,
    SelectComponent,
    TableComponent,
    TableCellDirective,
    CompanyUserDialogComponent,
    PlatformNavComponent,
  ],
  providers: [providePlatformI18n(), PlatformCompaniesStore],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './platform-companies.page.html',
  styleUrl: './platform-page.css',
})
export class PlatformCompaniesPage {
  protected readonly store = inject(PlatformPlansStore);
  protected readonly companies = inject(PlatformCompaniesStore);
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  private readonly transloco = inject(TranslocoService);
  private readonly confirm = inject(UiConfirm);
  private readonly toasts = inject(ToastStore);

  /** Empresa para la que se crea un usuario (diálogo abierto). */
  protected readonly userCompany = signal<UserCompany | null>(null);
  protected readonly userOpen = signal(false);

  /** Texto escrito en la búsqueda (la consulta al Api sale tras una pausa). */
  protected readonly query = signal('');
  /** Empresa a la que se le asigna un plan (diálogo abierto). */
  protected readonly selected = signal<PlatformCompanyItem | null>(null);
  protected readonly dialogOpen = signal(false);

  private readonly model = signal<AssignModel>(emptyAssign());
  protected readonly assignForm = createAssignForm(this.model);
  protected readonly submitting = computed(() => this.assignForm().submitting());
  protected readonly generalError = signal<string | null>(null);
  protected readonly done = signal<string | null>(null);

  protected readonly errors = computed(() => {
    const translate = (key: string, params?: Record<string, unknown>) => this.text(key, params);
    return { plan_id: fieldMessage(this.assignForm.plan_id(), translate) };
  });

  protected readonly columns = computed<UiTableColumn[]>(() => [
    { key: 'name', header: this.text('companies.name') },
    { key: 'nit', header: this.text('companies.nit'), nowrap: true },
    { key: 'email', header: this.text('companies.email') },
    { key: 'plan', header: this.text('companies.plan') },
    { key: 'active', header: this.text('companies.status') },
    { key: 'actions', header: this.text('common.actions') },
  ]);
  private readonly commonActions = computed<UiMenuItem[]>(() => [
    { id: 'assign' satisfies RowAction, label: this.text('assign.submit') },
    { id: 'user' satisfies RowAction, label: this.text('user.open') },
    { id: 'users' satisfies RowAction, label: this.text('companies.users') },
  ]);
  /** Menu of an active company: it can be deactivated. */
  protected readonly activeRowActions = computed<UiMenuItem[]>(() => [
    ...this.commonActions(),
    { id: 'deactivate' satisfies RowAction, label: this.text('activation.deactivate') },
  ]);
  /** Menu of an inactive company: it can be reactivated. */
  protected readonly inactiveRowActions = computed<UiMenuItem[]>(() => [
    ...this.commonActions(),
    { id: 'reactivate' satisfies RowAction, label: this.text('activation.reactivate') },
  ]);
  /** Company whose deactivation or reactivation is on its way; its menu shows a spinner meanwhile. */
  protected readonly changing = signal<string | null>(null);
  protected readonly tableLabels = computed<UiTableLabels>(() => ({
    loading: this.text('common.loading'),
    emptyTitle: this.text(this.companies.term() ? 'companies.noMatch' : 'companies.empty'),
    errorTitle: this.text('companies.loadError'),
    retry: this.text('common.retry'),
    paginator: {
      navigation: this.text('common.paginator.navigation'),
      previous: this.text('common.paginator.previous'),
      next: this.text('common.paginator.next'),
      page: this.text('common.paginator.page'),
    },
  }));
  protected readonly dialogTitle = computed(() =>
    this.text('assign.title', { name: this.selected()?.name ?? '' }),
  );

  protected readonly trackById = (_: number, row: PlatformCompanyItem) => row.id;

  constructor() {
    if (!this.store.loaded()) {
      void this.store.load();
    }
    this.companies.load();
  }

  protected onSearch(term: string): void {
    this.query.set(term);
    this.companies.search(term);
  }

  protected rowText(key: string, row: PlatformCompanyItem): string {
    return this.text(key, { name: row.name ?? '' });
  }

  protected onRowAction(action: string, row: PlatformCompanyItem): void {
    switch (action as RowAction) {
      case 'assign':
        this.openAssign(row);
        break;
      case 'user':
        this.openUser(row);
        break;
      case 'users':
        void this.router.navigate(['/plataforma/empresas', row.nit, 'usuarios']);
        break;
      case 'deactivate':
        void this.changeActivation(row, false);
        break;
      case 'reactivate':
        void this.changeActivation(row, true);
        break;
    }
  }

  /** Asks with the confirm dialog and, on yes, deactivates or reactivates the company of the row. */
  protected async changeActivation(row: PlatformCompanyItem, activate: boolean): Promise<void> {
    const name = row.name ?? '';
    const nit = row.nit ?? '';
    const kind = activate ? 'reactivate' : 'deactivate';
    const go = await this.confirm.ask({
      header: this.text(`activation.${kind}Header`, { name }),
      message: this.text(`activation.${kind}Message`, { name, nit }),
      acceptLabel: this.text(`activation.${kind}Accept`),
      rejectLabel: this.text('common.cancel'),
    });
    if (!go) {
      return;
    }
    this.done.set(null);
    this.changing.set(row.id ?? null);
    const context = new HttpContext().set(SKIP_ERROR_TOAST, true);
    try {
      const result: CompanyActivationResponse = await firstValueFrom(
        activate
          ? this.api.invoke(reactivatePlatformCompany, { nit }, context)
          : this.api.invoke(deactivatePlatformCompany, { nit }, context),
      );
      this.companies.setActive(row.id ?? '', result.is_active ?? activate);
      this.done.set(this.activationText(result, activate, name, nit));
    } catch (error) {
      this.activationFailed(error);
    } finally {
      this.changing.set(null);
    }
  }

  private activationText(
    result: CompanyActivationResponse,
    activate: boolean,
    name: string,
    nit: string,
  ): string {
    if (result.changed === false) {
      return this.text(activate ? 'activation.alreadyActive' : 'activation.alreadyInactive', {
        name,
      });
    }
    return activate
      ? this.text('activation.reactivated', { name, nit })
      : this.text('activation.deactivated', {
          name,
          nit,
          keys: Number(result.revoked_api_keys ?? 0),
          users: Number(result.signed_out_users ?? 0),
        });
  }

  private activationFailed(error: unknown): void {
    const status = error instanceof AppError ? error.status : null;
    if (status === CONFLICT || status === NOT_FOUND) {
      // The list no longer tells the truth: ask for it again.
      this.companies.retry();
    }
    this.toasts.show({
      kind: 'error',
      text: this.activationError(error, status),
      correlationId: error instanceof AppError ? error.correlationId : null,
    });
  }

  private activationError(error: unknown, status: number | null): string {
    switch (status) {
      case FORBIDDEN:
        return this.text('activation.forbidden');
      case NOT_FOUND:
        return this.text('activation.notFound');
      case CONFLICT:
        return this.text('activation.conflict');
      default:
        return error instanceof AppError && error.message
          ? error.message
          : this.text('common.unexpected');
    }
  }

  protected newCompany(): void {
    void this.router.navigateByUrl('/plataforma/empresas/nueva');
  }

  protected openUser(row: PlatformCompanyItem): void {
    this.done.set(null);
    this.userCompany.set({ id: row.id ?? '', name: row.name ?? '' });
    this.userOpen.set(true);
  }

  protected openAssign(row: PlatformCompanyItem): void {
    this.selected.set(row);
    // El plan empieza vacío: elegir otra vez el actual lo renueva, y eso debe ser una decisión explícita.
    this.model.set({ nit: row.nit ?? '', plan_id: '' });
    this.assignForm().reset();
    this.generalError.set(null);
    this.dialogOpen.set(true);
  }

  protected async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    const company = this.selected();
    if (!company) {
      return;
    }
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
            this.store.plans().find((item) => String(item.id) === plan_id)?.name ??
            plan_id;
          const start = result.start_plan_date ? DATE.format(new Date(result.start_plan_date)) : '';
          this.companies.setPlan(company.id ?? '', Number(plan_id), plan);
          this.done.set(
            this.text('assign.done', { plan, name: company.name ?? '', nit, date: start }),
          );
          this.dialogOpen.set(false);
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
