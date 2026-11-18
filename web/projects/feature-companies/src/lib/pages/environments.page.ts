import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { SetEnvironmentCommand } from '@impifact/api';
import { AppError, ToastStore } from '@impifact/core';
import { ButtonComponent, FieldComponent, SelectDirective } from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { companiesText, provideCompaniesI18n } from '../companies-i18n';
import {
  ProductionConfirmDialogComponent,
  ProductionConfirmLabels,
} from '../components/production-confirm-dialog.component';
import { ENVIRONMENT_KINDS, HABILITATION, PRODUCTION, kindKey } from '../models/dian';
import { CompanySetupStore } from '../state/company-setup.store';

type Choice = '' | `${typeof PRODUCTION}` | `${typeof HABILITATION}`;

/**
 * Ambientes (`/empresas/:id/ambientes`): habilitación o producción por familia de documentos. El Api no informa el
 * ambiente actual hasta que se cambia, así que cada lista empieza en «Sin cambios» y solo se envían las familias que
 * cambian. Pasar una familia a producción pide una confirmación explícita.
 */
@Component({
  selector: 'feat-environments-page',
  imports: [
    TranslocoPipe,
    ButtonComponent,
    FieldComponent,
    SelectDirective,
    ProductionConfirmDialogComponent,
  ],
  providers: [provideCompaniesI18n()],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="companies-section">
      <h2>{{ 'companies.environments.title' | transloco }}</h2>
      <p class="companies-lead">{{ 'companies.environments.description' | transloco }}</p>
      <p class="companies-message">{{ 'companies.environments.notReadable' | transloco }}</p>

      <form
        class="companies-form"
        novalidate
        (submit)="onSubmit($event)"
        [attr.aria-busy]="saving() || null"
      >
        @if (message(); as text) {
          <p class="companies-message companies-message--error" role="alert">{{ text }}</p>
        }
        @for (row of rows(); track row.kind) {
          <ui-field [label]="row.label" [hint]="row.current">
            <select
              uiSelect
              [value]="row.choice"
              (change)="choose(row.kind, $any($event.target).value)"
            >
              <option value="">{{ 'companies.common.noChange' | transloco }}</option>
              <option [value]="'' + habilitation">
                {{ 'companies.environments.habilitation' | transloco }}
              </option>
              <option [value]="'' + production">
                {{ 'companies.environments.production' | transloco }}
              </option>
            </select>
          </ui-field>
        }
        <div class="companies-form__actions">
          <ui-button
            type="submit"
            [label]="'companies.common.save' | transloco"
            [loading]="saving()"
          />
        </div>
      </form>
    </section>

    <feat-production-confirm-dialog
      [labels]="confirmLabels()"
      [busy]="saving()"
      [(visible)]="confirming"
      (confirmed)="send()"
    />
  `,
  styleUrl: './companies-page.css',
})
export class EnvironmentsPage {
  private readonly setup = inject(CompanySetupStore);
  private readonly toasts = inject(ToastStore);
  private readonly transloco = inject(TranslocoService);

  protected readonly production = PRODUCTION;
  protected readonly habilitation = HABILITATION;

  private readonly choices = signal<Record<number, Choice>>({});
  protected readonly saving = signal(false);
  protected readonly message = signal<string | null>(null);
  protected readonly confirming = signal(false);

  protected readonly rows = computed(() =>
    ENVIRONMENT_KINDS.map(({ kind }) => {
      const current = this.setup.environmentOf(kind);
      return {
        kind,
        label: this.text(kindKey(kind)),
        choice: this.choices()[kind] ?? '',
        current:
          current === null
            ? this.text('environments.unknown')
            : this.text('environments.current', { name: this.environmentName(current) }),
      };
    }),
  );

  /** Cuerpo de `setCompanyEnvironment` con solo las familias que cambian. */
  private readonly command = computed(() => {
    const body: SetEnvironmentCommand = {};
    for (const { kind, field } of ENVIRONMENT_KINDS) {
      const choice = this.choices()[kind];
      if (choice) {
        body[field] = Number(choice);
      }
    }
    return body;
  });

  protected readonly confirmLabels = computed<ProductionConfirmLabels>(() => {
    const kinds = ENVIRONMENT_KINDS.filter(({ kind }) => this.choices()[kind] === `${PRODUCTION}`)
      .map(({ kind }) => this.text(kindKey(kind)))
      .join(', ');
    return {
      header: this.text('environments.confirm.header'),
      body: this.text('environments.confirm.body', { kinds }),
      accept: this.text('environments.confirm.accept'),
      confirm: this.text('environments.confirm.confirm'),
      cancel: this.text('common.cancel'),
      close: this.text('common.close'),
    };
  });

  protected choose(kind: number, value: Choice): void {
    this.choices.update((current) => ({ ...current, [kind]: value }));
  }

  protected onSubmit(event: Event): void {
    event.preventDefault();
    this.message.set(null);
    const body = this.command();
    if (Object.keys(body).length === 0) {
      this.message.set(this.text('environments.nothingToSave'));
      return;
    }
    if (Object.values(body).includes(PRODUCTION)) {
      this.confirming.set(true);
      return;
    }
    void this.send();
  }

  protected async send(): Promise<void> {
    this.saving.set(true);
    try {
      await this.setup.setEnvironments(this.command());
      this.choices.set({});
      this.toasts.show({ kind: 'success', text: this.text('common.saved') });
    } catch (error) {
      this.message.set(
        error instanceof AppError
          ? (Object.values(error.fieldErrors)[0]?.[0] ?? error.message)
          : this.text('common.errors.unexpected'),
      );
    } finally {
      this.saving.set(false);
      this.confirming.set(false);
    }
  }

  private environmentName(id: number): string {
    return this.text(id === PRODUCTION ? 'environments.production' : 'environments.habilitation');
  }

  protected text(key: string, params?: Record<string, unknown>): string {
    return companiesText(this.transloco, key, params);
  }
}
