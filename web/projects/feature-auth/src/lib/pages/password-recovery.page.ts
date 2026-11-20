import { HttpContext } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormField, submit } from '@angular/forms/signals';
import { RouterLink } from '@angular/router';
import { Api, requestPasswordReset } from '@impifact/api';
import { AppError, SKIP_ERROR_TOAST } from '@impifact/core';
import { ButtonComponent, FieldComponent, InputDirective, PageTitleComponent } from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { AUTH_I18N_SCOPE, provideAuthI18n } from '../auth-i18n';
import { AuthBrandComponent } from '../components/auth-brand.component';
import {
  createRecoveryForm,
  resetFieldMessage,
  serverFieldError,
} from '../forms/password-reset.form';

/**
 * Pedir el enlace para restablecer la contraseña (`/recuperar`, pública). El Api responde siempre lo mismo, exista o
 * no la cuenta, y la página también: tras enviar muestra siempre la misma confirmación neutra (no usa el `message`
 * del Api ni dice si el correo está registrado). Solo un error de formato del correo (422 en `email`), el límite de
 * intentos (429) o la falta de conexión impiden mostrarla, porque en esos casos no se envió nada.
 */
@Component({
  selector: 'feat-password-recovery-page',
  imports: [
    FormField,
    RouterLink,
    TranslocoPipe,
    ButtonComponent,
    FieldComponent,
    InputDirective,
    PageTitleComponent,
    AuthBrandComponent,
  ],
  providers: [provideAuthI18n()],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="auth-page">
      <feat-auth-brand />
      <div class="auth-card">
        <ui-page-title
          [subtitle]="sent() ? undefined : ('auth.recovery.description' | transloco)"
          >{{ 'auth.recovery.title' | transloco }}</ui-page-title
        >

        @if (sent()) {
          <p class="auth-notice" role="status">{{ 'auth.recovery.sent' | transloco }}</p>
        } @else {
          <form novalidate (submit)="onSubmit($event)" [attr.aria-busy]="submitting() || null">
            @if (generalError(); as message) {
              <p class="auth-error" role="alert">{{ message }}</p>
            }
            <ui-field [label]="'auth.login.email' | transloco" [error]="emailError()" required>
              <input
                uiInput
                type="email"
                autocomplete="username"
                [formField]="recoveryForm.email"
              />
            </ui-field>
            <div class="auth-actions">
              <ui-button
                type="submit"
                block
                [label]="'auth.recovery.submit' | transloco"
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
export class PasswordRecoveryPage {
  private readonly api = inject(Api);
  private readonly transloco = inject(TranslocoService);

  protected readonly recoveryForm = createRecoveryForm();
  protected readonly submitting = computed(() => this.recoveryForm().submitting());
  protected readonly sent = signal(false);
  protected readonly generalError = signal<string | null>(null);

  protected readonly emailError = computed(() =>
    resetFieldMessage('email', this.recoveryForm.email(), (key) => this.text(key)),
  );

  protected async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    this.generalError.set(null);
    await submit(this.recoveryForm, {
      action: async () => {
        try {
          await firstValueFrom(
            this.api.invoke(
              requestPasswordReset,
              { body: { email: this.recoveryForm().value().email } },
              new HttpContext().set(SKIP_ERROR_TOAST, true),
            ),
          );
        } catch (error) {
          const shown = this.showError(error);
          if (shown) {
            return shown;
          }
        }
        this.sent.set(true);
        return undefined;
      },
      onInvalid: () => this.recoveryForm.email().focusBoundControl(),
    });
  }

  /**
   * Muestra los errores que significan que no se envió nada y devuelve los del campo; `null` si el error no cambia
   * la confirmación neutra (cualquier otra respuesta del Api).
   */
  private showError(error: unknown) {
    if (!(error instanceof AppError)) {
      return null;
    }
    const onField = serverFieldError(error, 'email', this.recoveryForm.email);
    if (onField.length > 0) {
      return onField;
    }
    if (error.status === 429) {
      this.generalError.set(this.text('login.errors.tooManyAttempts'));
      return [];
    }
    if (error.status === 0) {
      this.generalError.set(this.text('recovery.errors.network'));
      return [];
    }
    return null;
  }

  private text(key: string): string {
    return this.transloco.translate(`${AUTH_I18N_SCOPE}.${key}`);
  }
}
