import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
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
  UiBreadcrumbItem,
  UiTableColumn,
  UiTableLabels,
} from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { companiesText, provideCompaniesI18n } from '../companies-i18n';
import { CompanyUserDialogComponent } from '../components/company-user-dialog.component';
import { CompanyUsersStore, UsersCompany } from '../state/company-users.store';

/** API error codes of `updateCompanyUser` → text key of the toast that explains them. */
const UPDATE_ERROR_KEYS: Partial<Record<string, string>> = {
  'user.last_owner': 'users.errors.lastOwner',
  'user.cannot_deactivate_self': 'users.errors.cannotDeactivateSelf',
  'user.cannot_remove_own_owner_role': 'users.errors.cannotRemoveOwnOwnerRole',
  'user.not_found': 'users.errors.notFound',
  'user.company_not_found': 'users.errors.companyNotFound',
};

/** Route of the platform list of companies (feature-platform), where the platform admin comes from. */
const PLATFORM_COMPANIES_PATH = '/plataforma/empresas';

/**
 * Page `/empresa/usuarios`, only for the owner: the users of the active company (`listCompanyUsers`) with their role
 * and status. The platform admin opens the same page for any company at `/plataforma/empresas/:nit/usuarios` (the
 * `nit` input): the page finds that company among the platform companies for its name and id, says so when no
 * company has that NIT, and shows a breadcrumb back to «Empresas de la plataforma».
 * The owner creates another owner or a seller, swaps a user between owner and seller and deactivates
 * (after a confirmation) or reactivates them. Deactivating keeps the user, so what they issued keeps its author, and
 * the API closes their sessions. The current user's own row has no actions: the API refuses to demote or deactivate
 * oneself, and the page says so instead of offering them. The platform admin is not a member, so every row has them.
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
    RouterLink,
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

  /** NIT of the URL on the platform route; empty on the owner's `/empresa/usuarios`. */
  readonly nit = input<string>();
  protected readonly platform = computed(() => !!this.nit());

  /** On the platform route, the company of the URL: `null` when no platform company has that NIT. */
  protected readonly platformCompany = rxResource({
    params: () => this.nit() || undefined,
    stream: ({ params }) => this.store.findCompany(params),
  });

  /**
   * The company whose users are shown, with its NIT (the API routes need it): the active one for the owner, the one
   * of the URL for the platform admin; `null` until it is known.
   */
  protected readonly company = computed<UsersCompany | null>(() => {
    if (this.platform()) {
      return this.platformCompany.status() === 'resolved'
        ? (this.platformCompany.value() ?? null)
        : null;
    }
    const active = this.directory.byId(this.session.companyId());
    return active ? { id: active.id, nit: active.nit, name: active.name } : null;
  });
  protected readonly userId = computed(() => this.session.userId());

  /** The company is still being looked up (the directory of the owner or the platform search). */
  protected readonly companyLoading = computed(() =>
    this.platform()
      ? this.platformCompany.isLoading()
      : this.directory.status() === 'loading' || this.directory.status() === 'idle',
  );
  /** Platform route with a NIT that no company has (the search or the users API says so). */
  protected readonly notFound = computed(
    () =>
      this.platform() &&
      ((this.platformCompany.status() === 'resolved' && this.platformCompany.value() === null) ||
        this.store.missing()),
  );

  protected readonly title = computed(() => {
    const company = this.company();
    return this.platform() && company
      ? this.text('users.platform.title', { name: company.name })
      : this.text('users.title');
  });
  protected readonly breadcrumb = computed<UiBreadcrumbItem[]>(() =>
    this.platform()
      ? [
          { label: this.text('users.platform.companies'), link: PLATFORM_COMPANIES_PATH },
          { label: this.title() },
        ]
      : [],
  );
  protected readonly platformCompaniesPath = PLATFORM_COMPANIES_PATH;

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
    // The first read waits for the company: the NIT comes from the directory (owner) or the URL lookup (platform).
    effect(() => {
      const company = this.company();
      if (company && untracked(() => this.store.status()) === 'idle') {
        untracked(() => void this.store.load(company.nit));
      }
    });
  }

  /** The platform admin is not a member of the company, so no row is theirs. */
  protected isMe(row: CompanyUser): boolean {
    return !this.platform() && row.id === this.userId();
  }

  /** Retries the step that failed before the list: the directory (owner) or the company lookup (platform). */
  protected retryCompany(): void {
    if (this.platform()) {
      this.platformCompany.reload();
    } else {
      void this.directory.reload();
    }
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
