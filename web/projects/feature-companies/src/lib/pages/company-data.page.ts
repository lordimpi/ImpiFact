import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormField, submit } from '@angular/forms/signals';
import { AppError, HasUnsavedChanges, ToastStore } from '@impifact/core';
import { ButtonComponent, FieldComponent, InputDirective, SelectDirective } from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { companiesText, provideCompaniesI18n } from '../companies-i18n';
import {
  COMPANY_FIELDS,
  CompanyModel,
  companyCommand,
  companyModel,
  createCompanyForm,
  emptyCompany,
} from '../forms/company.form';
import { fieldMessage, serverFieldErrors } from '../forms/form-messages';
import {
  IDENTIFICATION_TYPES,
  LIABILITIES,
  MUNICIPALITIES,
  ORGANIZATION_TYPES,
  REGIMES,
  optionsWith,
} from '../models/catalogs';
import { CompanySetupStore } from '../state/company-setup.store';

/**
 * Datos de la empresa (`/empresas/:id/datos`): razón social, ubicación y responsabilidades fiscales. El Api no tiene
 * lectura de la empresa, así que el formulario empieza con la razón social del directorio (o con lo guardado en esta
 * sesión) y lo demás vacío. Los errores por campo del Api se muestran junto a cada campo.
 */
@Component({
  selector: 'feat-company-data-page',
  imports: [
    FormField,
    TranslocoPipe,
    ButtonComponent,
    FieldComponent,
    InputDirective,
    SelectDirective,
  ],
  providers: [provideCompaniesI18n()],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './company-data.page.html',
  styleUrl: './companies-page.css',
})
export class CompanyDataPage implements HasUnsavedChanges {
  private readonly setup = inject(CompanySetupStore);
  private readonly toasts = inject(ToastStore);
  private readonly transloco = inject(TranslocoService);

  private readonly model = signal<CompanyModel>(this.initialModel());
  protected readonly companyForm = createCompanyForm(this.model);
  protected readonly generalError = signal<string | null>(null);
  protected readonly submitting = computed(() => this.companyForm().submitting());

  protected readonly liabilityIds = computed(() => new Set(this.model().type_liability_ids));
  protected readonly liabilities = LIABILITIES;
  private readonly other = (id: number) => this.text('common.otherOption', { id });
  protected readonly identificationTypes = computed(() =>
    optionsWith(IDENTIFICATION_TYPES, this.model().type_document_identification_id, this.other),
  );
  protected readonly organizationTypes = computed(() =>
    optionsWith(ORGANIZATION_TYPES, this.model().type_organization_id, this.other),
  );
  protected readonly regimes = computed(() =>
    optionsWith(REGIMES, this.model().type_regime_id, this.other),
  );
  protected readonly municipalities = computed(() =>
    optionsWith(MUNICIPALITIES, this.model().municipality_id, this.other),
  );

  protected readonly errors = computed(() => {
    const translate = (key: string, params?: Record<string, unknown>) => this.text(key, params);
    const messages = Object.fromEntries(
      COMPANY_FIELDS.map((name) => [name, fieldMessage(this.companyForm[name](), translate)]),
    ) as Record<keyof CompanyModel, string | null>;
    const phone = this.companyForm.phone();
    if (phone.touched() && phone.errors()[0]?.kind === 'pattern') {
      messages.phone = this.text('data.phoneFormat');
    }
    const liabilities = this.companyForm.type_liability_ids();
    if (liabilities.touched() && liabilities.errors()[0]?.kind === 'liabilitiesRequired') {
      messages.type_liability_ids = this.text('data.liabilitiesRequired');
    }
    return messages;
  });

  hasUnsavedChanges(): boolean {
    return this.companyForm().dirty();
  }

  protected toggleLiability(id: number, checked: boolean): void {
    this.model.update((current) => ({
      ...current,
      type_liability_ids: checked
        ? [...current.type_liability_ids, id]
        : current.type_liability_ids.filter((value) => value !== id),
    }));
    const field = this.companyForm.type_liability_ids();
    field.markAsDirty();
    field.markAsTouched();
  }

  protected async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    this.generalError.set(null);
    await submit(this.companyForm, {
      action: async () => {
        try {
          await this.setup.saveCompany(companyCommand(this.model()));
        } catch (error) {
          return this.showError(error);
        }
        // Lo guardado queda en el formulario, ya sin cambios pendientes.
        this.companyForm().reset();
        this.toasts.show({ kind: 'success', text: this.text('common.saved') });
        return undefined;
      },
      onInvalid: () => this.focusFirstInvalid(),
    });
  }

  private initialModel(): CompanyModel {
    const saved = this.setup.saved();
    return saved ? companyModel(saved) : emptyCompany(this.setup.company()?.name ?? '');
  }

  private showError(error: unknown) {
    if (!(error instanceof AppError)) {
      this.generalError.set(this.text('common.errors.unexpected'));
      return undefined;
    }
    const onFields = serverFieldErrors(this.companyForm, COMPANY_FIELDS, error);
    const liability = error.fieldErrors['type_liability_id']?.[0];
    if (liability) {
      onFields.push({
        kind: 'server',
        message: liability,
        fieldTree: this.companyForm.type_liability_ids as never,
      });
    }
    if (onFields.length === 0) {
      this.generalError.set(error.message);
    }
    return onFields;
  }

  private focusFirstInvalid(): void {
    const first = COMPANY_FIELDS.filter((name) => name !== 'type_liability_ids')
      .map((name) => this.companyForm[name])
      .find((field) => field().invalid());
    first?.().focusBoundControl();
  }

  protected text(key: string, params?: Record<string, unknown>): string {
    return companiesText(this.transloco, key, params);
  }
}
