import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { FormField, submit } from '@angular/forms/signals';
import { Router, RouterLink } from '@angular/router';
import { AppError, CORE_ROUTE_PATHS, SessionStore } from '@impifact/core';
import {
  ButtonComponent,
  FieldComponent,
  InputDirective,
  PageTitleComponent,
  PasswordToggleDirective,
} from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { AUTH_I18N_SCOPE, provideAuthI18n } from '../auth-i18n';
import { AuthBrandComponent } from '../components/auth-brand.component';
import {
  createLoginForm,
  fieldMessage,
  generalErrorKey,
  serverFieldErrors,
} from '../forms/login.form';
import { safeReturnUrl } from '../navigation/return-url';

/**
 * Inicio de sesión (`/login`): el formulario a la izquierda y, a la derecha, un panel de color que dice en breve qué
 * hace ImpiFact (debajo del formulario en pantallas angostas). Tras entrar vuelve a la ruta que pidió la sesión
 * (`returnUrl`), o pasa por la selección de empresa si el usuario tiene empresas y ninguna quedó activa, o va al
 * inicio.
 */
@Component({
  selector: 'feat-login-page',
  imports: [
    FormField,
    RouterLink,
    TranslocoPipe,
    ButtonComponent,
    FieldComponent,
    InputDirective,
    PasswordToggleDirective,
    PageTitleComponent,
    AuthBrandComponent,
  ],
  providers: [provideAuthI18n()],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './login.page.html',
  styleUrls: ['./auth-page.css', './login.page.css'],
})
export class LoginPage {
  private readonly session = inject(SessionStore);
  private readonly router = inject(Router);
  private readonly paths = inject(CORE_ROUTE_PATHS);
  private readonly transloco = inject(TranslocoService);

  /** Ruta que pidió la sesión (la pone `authGuard`). */
  readonly returnUrl = input<string>();
  /** Motivo por el que se llegó al login (`inactivity`, `password-reset`), si lo hubo. */
  readonly reason = input<string>();

  /** Points of the side panel that says what ImpiFact does. */
  protected readonly introPoints = [
    'auth.login.intro.invoices',
    'auth.login.intro.status',
    'auth.login.intro.companies',
  ] as const;

  protected readonly loginForm = createLoginForm();
  protected readonly submitting = computed(() => this.loginForm().submitting());
  protected readonly generalError = signal<string | null>(null);

  protected readonly emailError = computed(() =>
    fieldMessage('email', this.loginForm.email(), (key) => this.text(key)),
  );
  protected readonly passwordError = computed(() =>
    fieldMessage('password', this.loginForm.password(), (key) => this.text(key)),
  );

  protected async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    this.generalError.set(null);
    await submit(this.loginForm, {
      action: async () => {
        try {
          await this.session.login(this.loginForm().value());
        } catch (error) {
          return this.showError(error);
        }
        await this.continueAfterLogin();
        return undefined;
      },
      onInvalid: () => this.focusFirstInvalid(),
    });
  }

  private continueAfterLogin(): Promise<boolean> {
    const returnUrl = safeReturnUrl(this.returnUrl());
    if (!this.session.companyId() && this.session.companies().length > 0) {
      return this.router.navigate([this.paths.selectCompany], {
        queryParams: returnUrl ? { returnUrl } : {},
      });
    }
    return this.router.navigateByUrl(returnUrl ?? this.paths.home);
  }

  /** Errores por campo junto a su campo; el resto en el aviso general del formulario. */
  private showError(error: unknown) {
    if (!(error instanceof AppError)) {
      this.generalError.set(this.text('login.errors.unexpected'));
      return undefined;
    }
    const onFields = serverFieldErrors(this.loginForm, error);
    if (onFields.length === 0) {
      const key = generalErrorKey(error);
      this.generalError.set(key ? this.text(key) : error.message);
    }
    return onFields;
  }

  private focusFirstInvalid(): void {
    const first = [this.loginForm.email, this.loginForm.password].find((field) =>
      field().invalid(),
    );
    first?.().focusBoundControl();
  }

  private text(key: string): string {
    return this.transloco.translate(`${AUTH_I18N_SCOPE}.${key}`);
  }
}
