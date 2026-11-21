import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  model,
  output,
  signal,
  untracked,
} from '@angular/core';
import { FormField, submit } from '@angular/forms/signals';
import { AppError } from '@impifact/core';
import {
  ButtonComponent,
  DialogComponent,
  FieldComponent,
  InputDirective,
  PasswordToggleDirective,
  SelectComponent,
} from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { companiesText } from '../companies-i18n';
import {
  COMPANY_USER_FIELDS,
  COMPANY_USER_ROLES,
  CompanyUserModel,
  PASSWORD_MIN_LENGTH,
  companyUserBody,
  createCompanyUserForm,
  emptyCompanyUser,
} from '../forms/company-user.form';
import { fieldMessage, serverFieldErrors } from '../forms/form-messages';
import { CompanyUsersStore } from '../state/company-users.store';

/**
 * Dialog where the owner creates another owner or a seller of the active company (`createUser` with `company_id`).
 * It mirrors the platform «Crear usuario» dialog of feature-platform, which cannot be imported here (features never
 * import each other). The password only lives in the form model and is cleared as soon as it is sent; every time the
 * dialog opens the form starts empty. On success it emits the confirmation text and closes.
 */
@Component({
  selector: 'feat-company-user-dialog',
  imports: [
    FormField,
    TranslocoPipe,
    ButtonComponent,
    DialogComponent,
    FieldComponent,
    InputDirective,
    PasswordToggleDirective,
    SelectComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-dialog
      [header]="'companies.users.form.title' | transloco"
      [closeLabel]="'companies.common.close' | transloco"
      [(visible)]="visible"
    >
      <form
        class="users-form"
        novalidate
        (submit)="onSubmit($event)"
        [attr.aria-busy]="submitting() || null"
      >
        <p class="users-lead">{{ 'companies.users.form.description' | transloco }}</p>
        @if (generalError(); as message) {
          <p class="users-error" role="alert">{{ message }}</p>
        }
        <ui-field
          [label]="'companies.users.form.email' | transloco"
          [error]="errors().email"
          required
        >
          <input
            uiInput
            type="email"
            autocomplete="off"
            [placeholder]="'companies.users.form.placeholders.email' | transloco"
            [formField]="userForm.email"
          />
        </ui-field>
        <ui-field
          [label]="'companies.users.form.name' | transloco"
          [error]="errors().name"
          required
        >
          <input
            uiInput
            type="text"
            autocomplete="off"
            [placeholder]="'companies.users.form.placeholders.name' | transloco"
            [formField]="userForm.name"
          />
        </ui-field>
        <ui-field [label]="'companies.users.form.phone' | transloco" [error]="errors().phone">
          <input
            uiInput
            type="tel"
            autocomplete="off"
            [placeholder]="'companies.users.form.placeholders.phone' | transloco"
            [formField]="userForm.phone"
          />
        </ui-field>
        <ui-field
          [label]="'companies.users.form.password' | transloco"
          [hint]="'companies.users.form.passwordHint' | transloco: { min: minLength }"
          [error]="errors().password"
          required
        >
          <input
            uiInput
            uiPasswordToggle
            type="password"
            autocomplete="new-password"
            [formField]="userForm.password"
          />
        </ui-field>
        <ui-field
          [label]="'companies.users.form.role' | transloco"
          [error]="errors().role"
          required
        >
          <ui-select [options]="roleOptions()" [formField]="userForm.role" />
        </ui-field>
        <div class="users-actions">
          <ui-button
            variant="secondary"
            [label]="'companies.common.cancel' | transloco"
            (pressed)="visible.set(false)"
          />
          <ui-button
            type="submit"
            [label]="'companies.users.form.submit' | transloco"
            [loading]="submitting()"
          />
        </div>
      </form>
    </ui-dialog>
  `,
  styles: `
    .users-form {
      display: flex;
      flex-direction: column;
      gap: var(--if-space-3);
    }
    .users-lead {
      margin: 0;
      color: var(--if-color-text-muted);
    }
    .users-error {
      margin: 0;
      padding: var(--if-space-2) var(--if-space-3);
      color: var(--if-color-danger);
      background: var(--if-color-danger-subtle);
      border-inline-start: 4px solid currentColor;
      border-radius: var(--if-radius-sm);
    }
    .users-actions {
      display: flex;
      flex-wrap: wrap;
      justify-content: flex-end;
      gap: var(--if-space-2);
    }
  `,
})
export class CompanyUserDialogComponent {
  private readonly store = inject(CompanyUsersStore);
  private readonly transloco = inject(TranslocoService);

  /** Id of the active company the user is created in. */
  readonly companyId = input<string | null>(null);
  readonly visible = model(false);
  /** Confirmation text for the page, once the user exists. */
  readonly created = output<string>();

  protected readonly minLength = PASSWORD_MIN_LENGTH;
  protected readonly roleOptions = computed(() =>
    COMPANY_USER_ROLES.map((role) => ({ id: role, name: this.text(`users.roles.${role}`) })),
  );
  private readonly model = signal<CompanyUserModel>(emptyCompanyUser());
  protected readonly userForm = createCompanyUserForm(this.model);
  protected readonly submitting = computed(() => this.userForm().submitting());
  protected readonly generalError = signal<string | null>(null);

  protected readonly errors = computed(() => {
    const translate = (key: string, params?: Record<string, unknown>) => this.text(key, params);
    return Object.fromEntries(
      COMPANY_USER_FIELDS.map((name) => [name, fieldMessage(this.userForm[name](), translate)]),
    ) as Record<keyof CompanyUserModel, string | null>;
  });

  constructor() {
    // Every time it opens, the form starts empty (no data nor password from the previous time).
    effect(() => {
      if (this.visible()) {
        untracked(() => this.reset());
      }
    });
  }

  protected async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    const companyId = this.companyId();
    if (!companyId) {
      return;
    }
    this.generalError.set(null);
    await submit(this.userForm, {
      action: async () => {
        const body = companyUserBody(this.model(), companyId);
        // The password leaves the state before waiting for the API, so it does not stay in memory either way.
        this.model.update((current) => ({ ...current, password: '' }));
        try {
          const user = await this.store.create(body);
          this.created.emit(
            this.text('users.form.done', {
              email: user.email,
              role: this.text(`users.roleNames.${user.role}`),
            }),
          );
          this.visible.set(false);
        } catch (error) {
          return this.showError(error);
        }
        return undefined;
      },
    });
  }

  private reset(): void {
    this.model.set(emptyCompanyUser());
    this.userForm().reset();
    this.generalError.set(null);
  }

  private showError(error: unknown) {
    if (!(error instanceof AppError)) {
      this.generalError.set(this.text('common.errors.unexpected'));
      return undefined;
    }
    if (error.code === 'user.email_taken') {
      return [{ kind: 'server', message: error.message, fieldTree: this.userForm.email }];
    }
    if (error.code === 'user.role_not_allowed') {
      this.generalError.set(this.text('users.form.roleNotAllowed'));
      return undefined;
    }
    const onFields = serverFieldErrors(this.userForm, COMPANY_USER_FIELDS, error);
    if (onFields.length === 0) {
      this.generalError.set(error.message || this.text('common.errors.unexpected'));
    }
    return onFields;
  }

  private text(key: string, params?: Record<string, unknown>): string {
    return companiesText(this.transloco, key, params);
  }
}
