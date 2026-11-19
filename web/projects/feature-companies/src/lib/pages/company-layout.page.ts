import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  untracked,
} from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MyCompaniesStore } from '@impifact/core';
import { ErrorStateComponent, LoadingComponent, PageTitleComponent } from '@impifact/ui';
import { TranslocoPipe } from '@jsverse/transloco';
import { provideCompaniesI18n } from '../companies-i18n';
import { CompanySetupStore } from '../state/company-setup.store';

/** Secciones de la configuración, en el orden del paso a producción. */
const SECTIONS = [
  { path: 'datos', label: 'companies.layout.tabs.data' },
  { path: 'ambientes', label: 'companies.layout.tabs.environments' },
  { path: 'software', label: 'companies.layout.tabs.software' },
  { path: 'certificado', label: 'companies.layout.tabs.certificate' },
  { path: 'resoluciones', label: 'companies.layout.tabs.resolutions' },
  { path: 'produccion', label: 'companies.layout.tabs.production' },
] as const;

/**
 * Marco de `/empresas/:id`: razón social y NIT (Número de Identificación Tributaria) de la empresa activa, las
 * secciones y la sección abierta. El NIT y el DV (dígito de verificación) que piden las rutas del Api salen de
 * `MyCompaniesStore`; sin ellos no se puede guardar nada, así que se muestra un error con reintento.
 */
@Component({
  selector: 'feat-company-layout-page',
  imports: [
    RouterLink,
    RouterLinkActive,
    RouterOutlet,
    TranslocoPipe,
    ErrorStateComponent,
    LoadingComponent,
    PageTitleComponent,
  ],
  providers: [provideCompaniesI18n()],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="companies-page">
      <header class="companies-header">
        <div>
          <ui-page-title>{{ 'companies.layout.title' | transloco }}</ui-page-title>
          @if (company(); as company) {
            <p class="companies-company">
              <strong>{{ company.name }}</strong>
              <span>{{
                'companies.layout.nit' | transloco: { nit: company.nit, dv: company.dv }
              }}</span>
            </p>
          }
        </div>
      </header>

      @if (company()) {
        <nav class="companies-tabs" [attr.aria-label]="'companies.layout.sections' | transloco">
          @for (section of sections; track section.path) {
            <a
              [routerLink]="section.path"
              routerLinkActive="is-active"
              ariaCurrentWhenActive="page"
              >{{ section.label | transloco }}</a
            >
          }
        </nav>
        <router-outlet />
      } @else if (directory.status() === 'loading' || directory.status() === 'idle') {
        <ui-loading [label]="'companies.common.loading' | transloco" />
      } @else {
        <ui-error-state
          [title]="'companies.layout.notFoundTitle' | transloco"
          [description]="'companies.layout.notFoundDescription' | transloco"
          [retryLabel]="'companies.common.retry' | transloco"
          (retry)="directory.reload()"
        />
      }
    </section>
  `,
  styleUrl: './companies-page.css',
})
export class CompanyLayoutPage {
  protected readonly directory = inject(MyCompaniesStore);
  private readonly setup = inject(CompanySetupStore);

  /** Id de la empresa en la ruta (el guard ya comprobó que es la activa). */
  readonly id = input.required<string>();

  protected readonly sections = SECTIONS;

  /** La empresa con la que trabaja el store; las secciones solo se abren cuando ya está fijada. */
  protected readonly company = computed(() => {
    const current = this.setup.company();
    return current?.id === this.id() ? current : null;
  });

  constructor() {
    effect(() => {
      const known = this.directory.byId(this.id());
      if (known) {
        untracked(() =>
          this.setup.use({ id: known.id, name: known.name, nit: known.nit, dv: known.dv }),
        );
      }
    });
  }
}
