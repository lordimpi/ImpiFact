import { HttpContext } from '@angular/common/http';
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
import { Api, createUser } from '@impifact/api';
import { AppError, SKIP_ERROR_TOAST } from '@impifact/core';
import {
  ButtonComponent,
  DialogComponent,
  FieldComponent,
  InputDirective,
  PasswordToggleDirective,
  SelectDirective,
} from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import {
  COMPANY_USER_FIELDS,
  COMPANY_USER_ROLES,
  CompanyUserModel,
  companyUserBody,
  createCompanyUserForm,
  emptyCompanyUser,
} from '../forms/company-user.form';
import { fieldMessage, serverFieldErrors } from '../forms/platform.forms';
import { platformText } from '../platform-i18n';

/** Empresa para la que se crea el usuario. */
export interface UserCompany {
  id: string;
  name: string;
}

/**
 * Diálogo para crear el primer dueño o un vendedor de una empresa desde la plataforma (`createUser` con
 * `company_id`). La contraseña solo vive en el modelo del formulario y se borra apenas se envía, con o sin éxito;
 * al abrir el diálogo el formulario empieza vacío. Al crear avisa con el texto que muestra la página.
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
    SelectDirective,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-dialog
      [header]="header()"
      [closeLabel]="'platform.common.close' | transloco"
      [(visible)]="visible"
    >
      <form
        class="user-form"
        novalidate
        (submit)="onSubmit($event)"
        [attr.aria-busy]="submitting() || null"
      >
        <p class="user-lead">{{ 'platform.user.description' | transloco }}</p>
        @if (generalError(); as message) {
          <p class="user-error" role="alert">{{ message }}</p>
        }
        <ui-field [label]="'platform.user.email' | transloco" [error]="errors().email" required>
          <input uiInput type="email" autocomplete="off" [formField]="userForm.email" />
        </ui-field>
        <ui-field [label]="'platform.user.name' | transloco" [error]="errors().name" required>
          <input uiInput type="text" autocomplete="off" [formField]="userForm.name" />
        </ui-field>
        <ui-field [label]="'platform.user.phone' | transloco" [error]="errors().phone">
          <input uiInput type="tel" autocomplete="off" [formField]="userForm.phone" />
        </ui-field>
        <ui-field
          [label]="'platform.user.password' | transloco"
          [hint]="'platform.user.passwordHint' | transloco"
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
        <ui-field [label]="'platform.user.role' | transloco" [error]="errors().role" required>
          <select uiSelect [formField]="userForm.role">
            @for (role of roles; track role) {
              <option [value]="role">{{ 'platform.user.roles.' + role | transloco }}</option>
            }
          </select>
        </ui-field>
        <div class="user-actions">
          <ui-button
            variant="secondary"
            [label]="'platform.common.cancel' | transloco"
            (pressed)="visible.set(false)"
          />
          <ui-button
            type="submit"
            [label]="'platform.user.submit' | transloco"
            [loading]="submitting()"
          />
        </div>
      </form>
    </ui-dialog>
  `,
  styles: `
    .user-form {
      display: flex;
      flex-direction: column;
      gap: var(--if-space-3);
    }
    .user-lead {
      margin: 0;
      color: var(--if-color-text-muted);
    }
    .user-error {
      margin: 0;
      padding: var(--if-space-2) var(--if-space-3);
      color: var(--if-color-danger);
      background: var(--if-color-danger-subtle);
      border-inline-start: 4px solid currentColor;
      border-radius: var(--if-radius-sm);
    }
    .user-actions {
      display: flex;
      flex-wrap: wrap;
      justify-content: flex-end;
      gap: var(--if-space-2);
    }
  `,
})
export class CompanyUserDialogComponent {
  private readonly api = inject(Api);
  private readonly transloco = inject(TranslocoService);

  readonly company = input<UserCompany | null>(null);
  readonly visible = model(false);
  /** Texto de confirmación para la página, cuando el usuario quedó creado. */
  readonly created = output<string>();

  protected readonly roles = COMPANY_USER_ROLES;
  private readonly model = signal<CompanyUserModel>(emptyCompanyUser());
  protected readonly userForm = createCompanyUserForm(this.model);
  protected readonly submitting = computed(() => this.userForm().submitting());
  protected readonly generalError = signal<string | null>(null);

  protected readonly header = computed(() =>
    this.text('user.title', { name: this.company()?.name ?? '' }),
  );

  protected readonly errors = computed(() => {
    const translate = (key: string, params?: Record<string, unknown>) => this.text(key, params);
    return Object.fromEntries(
      COMPANY_USER_FIELDS.map((name) => [name, fieldMessage(this.userForm[name](), translate)]),
    ) as Record<keyof CompanyUserModel, string | null>;
  });

  constructor() {
    // Cada vez que se abre, el formulario empieza vacío (sin datos ni contraseña de la vez anterior).
    effect(() => {
      if (this.visible()) {
        untracked(() => this.reset());
      }
    });
  }

  protected async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    const company = this.company();
    if (!company) {
      return;
    }
    this.generalError.set(null);
    await submit(this.userForm, {
      action: async () => {
        const body = companyUserBody(this.model(), company.id);
        // La contraseña sale del estado antes de esperar al Api: así no queda en memoria pase lo que pase.
        this.model.update((current) => ({ ...current, password: '' }));
        try {
          const user = await firstValueFrom(
            this.api.invoke(createUser, { body }, new HttpContext().set(SKIP_ERROR_TOAST, true)),
          );
          this.created.emit(
            this.text('user.done', {
              email: user.email,
              role: this.text(`user.roleNames.${user.role}`),
              name: company.name,
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
      this.generalError.set(this.text('common.unexpected'));
      return undefined;
    }
    if (error.code === 'user.email_taken') {
      return [{ kind: 'server', message: error.message, fieldTree: this.userForm.email }];
    }
    if (error.code === 'user.company_not_found') {
      this.generalError.set(this.text('user.companyNotFound'));
      return undefined;
    }
    if (error.code === 'user.role_not_allowed') {
      this.generalError.set(this.text('user.roleNotAllowed'));
      return undefined;
    }
    const onFields = serverFieldErrors(this.userForm, COMPANY_USER_FIELDS, error);
    if (onFields.length === 0) {
      this.generalError.set(error.message || this.text('common.unexpected'));
    }
    return onFields;
  }

  private text(key: string, params?: Record<string, unknown>): string {
    return platformText(this.transloco, key, params);
  }
}
