import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AppError, CORE_ROUTE_PATHS, SessionStore } from '@impifact/core';
import { ButtonComponent, EmptyStateComponent, PageTitleComponent } from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { AUTH_I18N_SCOPE, provideAuthI18n } from '../auth-i18n';
import { safeReturnUrl } from '../navigation/return-url';

const FORBIDDEN = 403;

/**
 * Selección de empresa (`/empresas/seleccionar`). Muestra las empresas de la sesión (`companies` del login y de la
 * renovación) y, al elegir una, renueva la sesión con `company_id` (`SessionStore.selectCompany`).
 *
 * El Api solo entrega los identificadores de las empresas, no sus nombres: por eso se numeran y se muestra el
 * identificador. Cuando exista un endpoint con las empresas del usuario, aquí irán la razón social y el NIT.
 */
@Component({
  selector: 'feat-company-selection-page',
  imports: [ButtonComponent, EmptyStateComponent, PageTitleComponent, RouterLink, TranslocoPipe],
  providers: [provideAuthI18n()],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="auth-page">
      <div class="auth-card">
        <ui-page-title>{{ 'auth.selectCompany.title' | transloco }}</ui-page-title>

        @if (companies().length === 0) {
          <ui-empty-state
            [title]="'auth.selectCompany.emptyTitle' | transloco"
            [description]="'auth.selectCompany.emptyDescription' | transloco"
          >
            <a class="auth-link" routerLink="/">{{ 'auth.selectCompany.goHome' | transloco }}</a>
          </ui-empty-state>
        } @else {
          <p class="auth-lead">{{ 'auth.selectCompany.description' | transloco }}</p>
          @if (error(); as message) {
            <p class="auth-error" role="alert">{{ message }}</p>
          }
          <ul class="auth-list" [attr.aria-label]="'auth.selectCompany.listLabel' | transloco">
            @for (company of companies(); track company.id) {
              <li class="auth-company">
                <div>
                  <p class="auth-company__name">
                    {{ 'auth.selectCompany.company' | transloco: { number: company.number } }}
                  </p>
                  <p class="auth-company__id">
                    {{ 'auth.selectCompany.companyId' | transloco: { id: company.id } }}
                  </p>
                  @if (company.active) {
                    <p class="auth-company__active">
                      {{ 'auth.selectCompany.active' | transloco }}
                    </p>
                  }
                </div>
                <ui-button
                  [variant]="company.active ? 'secondary' : 'primary'"
                  [label]="'auth.selectCompany.select' | transloco: { number: company.number }"
                  [loading]="pending() === company.id"
                  [disabled]="pending() !== null"
                  (pressed)="choose(company.id)"
                />
              </li>
            }
          </ul>
        }
      </div>
    </section>
  `,
  styleUrl: './auth-page.css',
})
export class CompanySelectionPage {
  private readonly session = inject(SessionStore);
  private readonly router = inject(Router);
  private readonly paths = inject(CORE_ROUTE_PATHS);
  private readonly transloco = inject(TranslocoService);

  /** Ruta a la que se vuelve después de elegir (la trae el login). */
  readonly returnUrl = input<string>();

  protected readonly companies = computed(() => {
    const active = this.session.companyId();
    return this.session
      .companies()
      .map((id, index) => ({ id, number: index + 1, active: id === active }));
  });
  /** Empresa que se está activando; mientras tanto las demás no se pueden elegir. */
  protected readonly pending = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);

  protected async choose(companyId: string): Promise<void> {
    const returnUrl = safeReturnUrl(this.returnUrl());
    this.error.set(null);
    this.pending.set(companyId);
    try {
      await this.session.selectCompany(companyId);
    } catch (error) {
      this.pending.set(null);
      if (!this.session.isAuthenticated()) {
        await this.router.navigate([this.paths.login], {
          queryParams: returnUrl ? { returnUrl } : {},
        });
        return;
      }
      this.error.set(this.messageFor(error));
      return;
    }
    this.pending.set(null);
    // Un cierre de sesión mientras tanto ya llevó al login.
    if (this.session.isAuthenticated()) {
      await this.router.navigateByUrl(returnUrl ?? this.paths.home);
    }
  }

  private messageFor(error: unknown): string {
    if (error instanceof AppError && error.status !== FORBIDDEN) {
      return error.message;
    }
    return this.transloco.translate(`${AUTH_I18N_SCOPE}.selectCompany.errors.notAllowed`);
  }
}
