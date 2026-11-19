import { HttpContext } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { FormField, submit } from '@angular/forms/signals';
import { Meta } from '@angular/platform-browser';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Api, confirmPasswordReset } from '@impifact/api';
import { AppError, CORE_ROUTE_PATHS, SKIP_ERROR_TOAST, SessionStore } from '@impifact/core';
import {
  ButtonComponent,
  FieldComponent,
  InputDirective,
  PageTitleComponent,
  PasswordToggleDirective,
} from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { AUTH_I18N_SCOPE, provideAuthI18n } from '../auth-i18n';
import {
  createResetForm,
  isInvalidLink,
  resetFieldMessage,
  serverFieldError,
} from '../forms/password-reset.form';

/** Motivo con el que el login avisa que la contraseña quedó cambiada. */
export const PASSWORD_RESET_REASON = 'password-reset';

/**
 * Fijar la contraseña nueva (`/restablecer?token=...`, el enlace del correo; pública, también con sesión abierta).
 * El token es un secreto de un solo uso: la página lo lee una vez, lo quita de la URL visible (navegación con
 * `replaceUrl`, así tampoco queda en el historial) y lo guarda solo en un campo privado para enviarlo con la
 * contraseña; nunca va a un store, al almacenamiento del navegador ni a un registro. Mientras la página está abierta
 * pone `<meta name="referrer" content="no-referrer">` para que el token no salga en el encabezado `Referer` de
 * ninguna solicitud. Al terminar cierra la sesión que hubiera en este navegador (el Api cerró todas) y va al login.
 */
@Component({
  selector: 'feat-password-reset-page',
  imports: [
    FormField,
    RouterLink,
    TranslocoPipe,
    ButtonComponent,
    FieldComponent,
    InputDirective,
    PasswordToggleDirective,
    PageTitleComponent,
  ],
  providers: [provideAuthI18n()],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="auth-page">
      <div class="auth-card">
        <ui-page-title>{{ 'auth.reset.title' | transloco }}</ui-page-title>

        @if (invalidLink()) {
          <div class="auth-error" role="alert">
            <p class="auth-error__text">{{ 'auth.reset.invalidLink' | transloco }}</p>
            <a class="auth-link" routerLink="/recuperar">{{
              'auth.reset.requestNew' | transloco
            }}</a>
          </div>
        } @else {
          <p class="auth-lead">{{ 'auth.reset.description' | transloco }}</p>
          <form novalidate (submit)="onSubmit($event)" [attr.aria-busy]="submitting() || null">
            @if (generalError(); as message) {
              <p class="auth-error" role="alert">{{ message }}</p>
            }
            <ui-field
              [label]="'auth.reset.newPassword' | transloco"
              [hint]="'auth.reset.newPasswordHint' | transloco"
              [error]="newPasswordError()"
              required
            >
              <input
                uiInput
                uiPasswordToggle
                type="password"
                autocomplete="new-password"
                [formField]="resetForm.newPassword"
              />
            </ui-field>
            <ui-field
              [label]="'auth.reset.confirmation' | transloco"
              [error]="confirmationError()"
              required
            >
              <input
                uiInput
                uiPasswordToggle
                type="password"
                autocomplete="new-password"
                [formField]="resetForm.confirmation"
              />
            </ui-field>
            <div class="auth-actions">
              <ui-button
                type="submit"
                [label]="'auth.reset.submit' | transloco"
                [loading]="submitting()"
              />
            </div>
          </form>
        }

        <p class="auth-back">
          <a class="auth-link" routerLink="/login">{{ 'auth.recovery.back' | transloco }}</a>
        </p>
      </div>
    </section>
  `,
  styleUrl: './auth-page.css',
})
export class PasswordResetPage implements OnInit {
  private readonly api = inject(Api);
  private readonly session = inject(SessionStore);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly paths = inject(CORE_ROUTE_PATHS);
  private readonly transloco = inject(TranslocoService);

  /** Se lee una sola vez, antes de quitarlo de la URL. */
  readonly #token = this.route.snapshot.queryParamMap.get('token')?.trim() ?? '';

  protected readonly resetForm = createResetForm();
  protected readonly submitting = computed(() => this.resetForm().submitting());
  protected readonly invalidLink = signal(this.#token === '');
  protected readonly generalError = signal<string | null>(null);

  protected readonly newPasswordError = computed(() =>
    resetFieldMessage('newPassword', this.resetForm.newPassword(), (key) => this.text(key)),
  );
  protected readonly confirmationError = computed(() =>
    resetFieldMessage('confirmation', this.resetForm.confirmation(), (key) => this.text(key)),
  );

  constructor() {
    const meta = inject(Meta);
    meta.updateTag({ name: 'referrer', content: 'no-referrer' });
    inject(DestroyRef).onDestroy(() => meta.removeTag("name='referrer'"));
  }

  ngOnInit(): void {
    if (this.route.snapshot.queryParamMap.has('token')) {
      void this.router.navigate([], { relativeTo: this.route, queryParams: {}, replaceUrl: true });
    }
  }

  protected async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    this.generalError.set(null);
    await submit(this.resetForm, {
      action: async () => {
        try {
          await firstValueFrom(
            this.api.invoke(
              confirmPasswordReset,
              { body: { token: this.#token, new_password: this.resetForm().value().newPassword } },
              new HttpContext().set(SKIP_ERROR_TOAST, true),
            ),
            { defaultValue: undefined },
          );
        } catch (error) {
          return this.showError(error);
        }
        await this.goToLogin();
        return undefined;
      },
      onInvalid: () => this.focusFirstInvalid(),
    });
  }

  /** El Api cerró todas las sesiones del usuario; si este navegador tenía una, se cierra aquí también. */
  private async goToLogin(): Promise<void> {
    if (this.session.isAuthenticated()) {
      await this.session.logout();
    }
    await this.router.navigate([this.paths.login], {
      queryParams: { reason: PASSWORD_RESET_REASON },
    });
  }

  private showError(error: unknown) {
    if (!(error instanceof AppError)) {
      this.generalError.set(this.text('reset.errors.unexpected'));
      return [];
    }
    if (isInvalidLink(error)) {
      this.invalidLink.set(true);
      return [];
    }
    const onField = serverFieldError(error, 'new_password', this.resetForm.newPassword);
    if (onField.length > 0) {
      return onField;
    }
    this.generalError.set(
      error.status === 429 ? this.text('login.errors.tooManyAttempts') : error.message,
    );
    return [];
  }

  private focusFirstInvalid(): void {
    const first = [this.resetForm.newPassword, this.resetForm.confirmation].find((field) =>
      field().invalid(),
    );
    first?.().focusBoundControl();
  }

  private text(key: string): string {
    return this.transloco.translate(`${AUTH_I18N_SCOPE}.${key}`);
  }
}
