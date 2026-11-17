import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { PageTitleComponent } from '@impifact/ui';
import { TranslocoPipe } from '@jsverse/transloco';
import { provideAuthI18n } from '../auth-i18n';

/**
 * Recuperación de contraseña (`/recuperar`). El Api todavía no tiene un endpoint para recuperarla, así que la página
 * solo lo explica y lleva de vuelta al login; cuando exista, aquí irá el formulario.
 */
@Component({
  selector: 'feat-password-recovery-page',
  imports: [PageTitleComponent, RouterLink, TranslocoPipe],
  providers: [provideAuthI18n()],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="auth-page">
      <div class="auth-card">
        <ui-page-title>{{ 'auth.recovery.title' | transloco }}</ui-page-title>
        <p>{{ 'auth.recovery.unavailable' | transloco }}</p>
        <p class="auth-lead">{{ 'auth.recovery.help' | transloco }}</p>
        <a class="auth-link" routerLink="/login">{{ 'auth.recovery.back' | transloco }}</a>
      </div>
    </section>
  `,
  styleUrl: './auth-page.css',
})
export class PasswordRecoveryPage {}
