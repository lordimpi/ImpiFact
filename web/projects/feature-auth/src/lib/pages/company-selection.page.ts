import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AppError, CORE_ROUTE_PATHS, MyCompaniesStore, SessionStore } from '@impifact/core';
import {
  BadgeComponent,
  ButtonComponent,
  EmptyStateComponent,
  PageTitleComponent,
} from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { AUTH_I18N_SCOPE, provideAuthI18n } from '../auth-i18n';
import { AuthBrandComponent } from '../components/auth-brand.component';
import { safeReturnUrl } from '../navigation/return-url';

const FORBIDDEN = 403;

/**
 * Selección de empresa (`/empresas/seleccionar`). Muestra las empresas de la sesión (`companies` del login y de la
 * renovación) y, al elegir una, renueva la sesión con `company_id` (`SessionStore.selectCompany`).
 *
 * La razón social y el NIT (Número de Identificación Tributaria) salen de `MyCompaniesStore`
 * (`GET /api/v1/me/companies`). Si esa lista no cargó o no trae una empresa, esa empresa se numera y se muestra su
 * identificador. Una empresa inactiva aparece deshabilitada.
 */
@Component({
  selector: 'feat-company-selection-page',
  imports: [
    AuthBrandComponent,
    BadgeComponent,
    ButtonComponent,
    EmptyStateComponent,
    PageTitleComponent,
    RouterLink,
    TranslocoPipe,
  ],
  providers: [provideAuthI18n()],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="auth-page">
      <feat-auth-brand />
      <div class="auth-card auth-card--wide">
        <ui-page-title
          [subtitle]="
            companies().length === 0 ? undefined : ('auth.selectCompany.description' | transloco)
          "
          >{{ 'auth.selectCompany.title' | transloco }}</ui-page-title
        >

        @if (companies().length === 0) {
          <ui-empty-state
            [title]="'auth.selectCompany.emptyTitle' | transloco"
            [description]="'auth.selectCompany.emptyDescription' | transloco"
          >
            <a class="auth-link" routerLink="/">{{ 'auth.selectCompany.goHome' | transloco }}</a>
          </ui-empty-state>
        } @else {
          @if (error(); as message) {
            <p class="auth-error" role="alert">{{ message }}</p>
          }
          <ul class="auth-list" [attr.aria-label]="'auth.selectCompany.listLabel' | transloco">
            @for (company of companies(); track company.id) {
              <li class="auth-company" [class.auth-company--active]="company.active">
                <div class="auth-company__info">
                  <span class="auth-company__avatar" aria-hidden="true">{{ company.initial }}</span>
                  <div class="auth-company__text">
                    @if (company.name; as name) {
                      <p class="auth-company__name">{{ name }}</p>
                      <p class="auth-company__id">
                        {{
                          'auth.selectCompany.nit' | transloco: { nit: company.nit, dv: company.dv }
                        }}
                      </p>
                    } @else {
                      <p class="auth-company__name">
                        {{ 'auth.selectCompany.company' | transloco: { number: company.number } }}
                      </p>
                      <p class="auth-company__id">
                        {{ 'auth.selectCompany.companyId' | transloco: { id: company.id } }}
                      </p>
                    }
                    <div class="auth-company__badges">
                      @if (company.active) {
                        <ui-badge tone="success">{{
                          'auth.selectCompany.active' | transloco
                        }}</ui-badge>
                      }
                      @if (company.inactive) {
                        <ui-badge>{{ 'auth.selectCompany.inactive' | transloco }}</ui-badge>
                      }
                    </div>
                  </div>
                </div>
                <ui-button
                  size="small"
                  [variant]="company.active ? 'secondary' : 'primary'"
                  [label]="
                    company.name
                      ? ('auth.selectCompany.selectNamed' | transloco: { name: company.name })
                      : ('auth.selectCompany.select' | transloco: { number: company.number })
                  "
                  [loading]="pending() === company.id"
                  [disabled]="pending() !== null || company.inactive"
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
  private readonly directory = inject(MyCompaniesStore);

  /** Ruta a la que se vuelve después de elegir (la trae el login). */
  readonly returnUrl = input<string>();

  protected readonly companies = computed(() => {
    const active = this.session.companyId();
    return this.session.companies().map((id, index) => {
      const known = this.directory.byId(id);
      return {
        id,
        number: index + 1,
        active: id === active,
        name: known?.name ?? null,
        initial: (known?.name?.trim().charAt(0) ?? '').toUpperCase() || String(index + 1),
        nit: known?.nit ?? '',
        dv: known?.dv ?? '',
        inactive: known ? !known.is_active : false,
      };
    });
  });
  /** Empresa que se está activando; mientras tanto las demás no se pueden elegir. */
  protected readonly pending = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);

  protected async choose(companyId: string): Promise<void> {
    if (this.directory.byId(companyId)?.is_active === false) {
      return;
    }
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
