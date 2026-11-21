import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { CompanyUser } from '@impifact/api';
import { AppError, MyCompaniesStore, SessionStore, ToastStore } from '@impifact/core';
import {
  BadgeComponent,
  ButtonComponent,
  CardComponent,
  DialogComponent,
  ErrorStateComponent,
  LoadingComponent,
  PageTitleComponent,
  TableCellDirective,
  TableComponent,
  UiTableColumn,
  UiTableLabels,
} from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { companiesText, provideCompaniesI18n } from '../companies-i18n';
import { CompanyUserDialogComponent } from '../components/company-user-dialog.component';
import { CompanyUsersStore } from '../state/company-users.store';

/** API error codes of `updateCompanyUser` → text key of the toast that explains them. */
const UPDATE_ERROR_KEYS: Partial<Record<string, string>> = {
  'user.last_owner': 'users.errors.lastOwner',
  'user.cannot_deactivate_self': 'users.errors.cannotDeactivateSelf',
  'user.cannot_remove_own_owner_role': 'users.errors.cannotRemoveOwnOwnerRole',
  'user.not_found': 'users.errors.notFound',
};

/**
 * Page `/empresa/usuarios`, only for the owner: the users of the active company (`listCompanyUsers`) with their role
 * and status. The owner creates another owner or a seller, swaps a user between owner and seller and deactivates
 * (after a confirmation) or reactivates them. Deactivating keeps the user, so what they issued keeps its author, and
 * the API closes their sessions. The current user's own row has no actions: the API refuses to demote or deactivate
 * oneself, and the page says so instead of offering them.
 */
@Component({
  selector: 'feat-company-users-page',
  imports: [
    TranslocoPipe,
    BadgeComponent,
    ButtonComponent,
    CardComponent,
    DialogComponent,
    ErrorStateComponent,
    LoadingComponent,
    PageTitleComponent,
    TableComponent,
    TableCellDirective,
    CompanyUserDialogComponent,
  ],
  providers: [provideCompaniesI18n(), CompanyUsersStore],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './company-users.page.html',
  styleUrl: './company-users.page.css',
})
export class CompanyUsersPage {
  protected readonly store = inject(CompanyUsersStore);
  protected readonly directory = inject(MyCompaniesStore);
  private readonly session = inject(SessionStore);
  private readonly toasts = inject(ToastStore);
  private readonly transloco = inject(TranslocoService);

  /** The active company with its NIT (the API routes need it); `null` until the directory knows it. */
  protected readonly company = computed(() => this.directory.byId(this.session.companyId()));
  protected readonly userId = computed(() => this.session.userId());

  protected readonly createOpen = signal(false);
  /** User waiting for the deactivation confirmation (dialog open). */
  protected readonly toDeactivate = signal<CompanyUser | null>(null);
  protected readonly deactivateOpen = signal(false);

  protected readonly columns = computed<UiTableColumn[]>(() => [
    { key: 'name', header: this.text('users.list.name') },
    { key: 'email', header: this.text('users.list.email') },
    { key: 'phone', header: this.text('users.list.phone') },
    { key: 'role', header: this.text('users.list.role') },
    { key: 'is_active', header: this.text('users.list.status') },
    { key: 'actions', header: this.text('users.list.actions') },
  ]);
  protected readonly tableLabels = computed<UiTableLabels>(() => ({
    loading: this.text('users.list.loading'),
    emptyTitle: this.text('users.list.emptyTitle'),
    emptyDescription: this.text('users.list.emptyDescription'),
    errorTitle: this.text('users.list.errorTitle'),
    errorDescription: this.text('users.list.errorDescription'),
    retry: this.text('common.retry'),
    paginator: {
      navigation: this.transloco.translate('ui.paginator.navigation'),
      previous: this.transloco.translate('ui.paginator.previous'),
      next: this.transloco.translate('ui.paginator.next'),
      page: this.transloco.translate('ui.paginator.page'),
    },
  }));
  protected readonly deactivateLabels = computed(() => {
    const name = this.toDeactivate()?.name ?? '';
    return {
      header: this.text('users.deactivate.title', { name }),
      body: this.text('users.deactivate.body', { name }),
    };
  });

  protected readonly trackById = (_: number, row: CompanyUser) => row.id;

  constructor() {
    // The first read waits for the directory: the NIT of the active company comes from there.
    effect(() => {
      const company = this.company();
      if (company && untracked(() => this.store.status()) === 'idle') {
        untracked(() => void this.store.load(company.nit));
      }
    });
  }

  protected isMe(row: CompanyUser): boolean {
    return row.id === this.userId();
  }

  protected rowText(key: string, row: CompanyUser): string {
    return this.text(key, { name: row.name });
  }

  protected roleLabel(row: CompanyUser): string {
    return this.text(`users.roles.${row.role}`);
  }

  protected reload(): void {
    const company = this.company();
    if (company) {
      void this.store.load(company.nit);
    }
  }

  protected onCreated(text: string): void {
    this.toasts.show({ kind: 'success', text });
    this.reload();
  }

  protected async toggleRole(row: CompanyUser): Promise<void> {
    const role = row.role === 'owner' ? 'seller' : 'owner';
    const user = await this.change(row, { role });
    if (user) {
      this.success('users.done.role', {
        name: user.name,
        role: this.text(`users.roleNames.${user.role}`),
      });
    }
  }

  protected askDeactivate(row: CompanyUser): void {
    this.toDeactivate.set(row);
    this.deactivateOpen.set(true);
  }

  protected async deactivate(): Promise<void> {
    const row = this.toDeactivate();
    if (!row) {
      return;
    }
    const user = await this.change(row, { is_active: false });
    this.deactivateOpen.set(false);
    this.toDeactivate.set(null);
    if (user) {
      this.success('users.done.deactivated', { name: user.name });
    }
  }

  protected async reactivate(row: CompanyUser): Promise<void> {
    const user = await this.change(row, { is_active: true });
    if (user) {
      this.success('users.done.reactivated', { name: user.name });
    }
  }

  /** Sends the change; on an error explains it in a toast and returns `null` (the row stays as it was). */
  private async change(
    row: CompanyUser,
    body: { role?: string; is_active?: boolean },
  ): Promise<CompanyUser | null> {
    const company = this.company();
    if (!company) {
      return null;
    }
    try {
      return await this.store.update(company.nit, row.id, body);
    } catch (error) {
      this.toasts.show({
        kind: 'error',
        text: this.errorText(error),
        correlationId: error instanceof AppError ? error.correlationId : null,
      });
      return null;
    }
  }

  private errorText(error: unknown): string {
    if (!(error instanceof AppError)) {
      return this.text('common.errors.unexpected');
    }
    const key = UPDATE_ERROR_KEYS[error.code];
    if (key) {
      return this.text(key);
    }
    if (error.status === 409) {
      return this.text('users.errors.conflict');
    }
    return error.message || this.text('common.errors.unexpected');
  }

  private success(key: string, params: Record<string, unknown>): void {
    this.toasts.show({ kind: 'success', text: this.text(key, params) });
  }

  private text(key: string, params?: Record<string, unknown>): string {
    return companiesText(this.transloco, key, params);
  }
}
