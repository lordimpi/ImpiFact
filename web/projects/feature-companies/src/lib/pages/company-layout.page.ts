import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  untracked,
} from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { MyCompaniesStore } from '@impifact/core';
import {
  ErrorStateComponent,
  LoadingComponent,
  PageTitleComponent,
  TabsComponent,
  UiTab,
} from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { companiesText, provideCompaniesI18n } from '../companies-i18n';
import { CompanySetupStore } from '../state/company-setup.store';

/** Secciones de la configuración, en el orden del paso a producción. */
const SECTIONS = [
  { path: 'datos', label: 'layout.tabs.data' },
  { path: 'ambientes', label: 'layout.tabs.environments' },
  { path: 'software', label: 'layout.tabs.software' },
  { path: 'certificado', label: 'layout.tabs.certificate' },
  { path: 'resoluciones', label: 'layout.tabs.resolutions' },
  { path: 'produccion', label: 'layout.tabs.production' },
] as const;

/**
 * Marco de `/empresas/:id`: razón social y NIT (Número de Identificación Tributaria) de la empresa activa, las
 * secciones y la sección abierta. El NIT y el DV (dígito de verificación) que piden las rutas del Api salen de
 * `MyCompaniesStore`; sin ellos no se puede guardar nada, así que se muestra un error con reintento.
 */
@Component({
  selector: 'feat-company-layout-page',
  imports: [
    RouterOutlet,
    TranslocoPipe,
    ErrorStateComponent,
    LoadingComponent,
    PageTitleComponent,
    TabsComponent,
  ],
  providers: [provideCompaniesI18n()],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="companies-page">
      <ui-page-title [subtitle]="'companies.layout.subtitle' | transloco">
        {{ 'companies.layout.title' | transloco }}
        @if (company(); as company) {
          <p uiPageTitleActions class="companies-company">
            <strong>{{ company.name }}</strong>
            <span>{{
              'companies.layout.nit' | transloco: { nit: company.nit, dv: company.dv }
            }}</span>
          </p>
        }
      </ui-page-title>

      @if (company()) {
        <ui-tabs [label]="'companies.layout.sections' | transloco" [tabs]="tabs" />
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

  private readonly transloco = inject(TranslocoService);

  /** Section tabs; they also stay active on any child route of their section. */
  protected readonly tabs: readonly UiTab[] = SECTIONS.map((section) => ({
    label: companiesText(this.transloco, section.label),
    link: section.path,
    exact: false,
  }));

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
