import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormField, submit } from '@angular/forms/signals';
import {
  AppError,
  CatalogsStore,
  HasUnsavedChanges,
  SmallCatalogName,
  ToastStore,
  catalogOptions,
  municipalityPicker,
  optionsWith,
} from '@impifact/core';
import {
  ButtonComponent,
  CardComponent,
  CardFooterDirective,
  CheckboxComponent,
  ComboboxComponent,
  ComboboxLabels,
  DigitsOnlyDirective,
  ErrorStateComponent,
  FieldComponent,
  HelpComponent,
  InputDirective,
  LoadingComponent,
  SelectComponent,
} from '@impifact/ui';
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
import { CompanySetupStore } from '../state/company-setup.store';

/**
 * Datos de la empresa (`/empresas/:id/datos`): razón social, ubicación y responsabilidades fiscales. Al abrir se leen
 * del Api (`getCompany`) y llenan el formulario; esa misma forma es la que se envía al guardar. El teléfono solo admite
 * dígitos, el municipio se elige después del departamento (el departamento sale del municipio guardado) y cada campo
 * explica qué poner en su tooltip. Los errores por campo del Api se muestran junto a cada campo.
 */
@Component({
  selector: 'feat-company-data-page',
  imports: [
    CheckboxComponent,
    FormField,
    TranslocoPipe,
    ButtonComponent,
    CardComponent,
    CardFooterDirective,
    ComboboxComponent,
    DigitsOnlyDirective,
    ErrorStateComponent,
    FieldComponent,
    HelpComponent,
    InputDirective,
    LoadingComponent,
    SelectComponent,
  ],
  providers: [provideCompaniesI18n()],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './company-data.page.html',
  styleUrls: ['./companies-page.css', './companies-checks.css'],
})
export class CompanyDataPage implements HasUnsavedChanges {
  protected readonly setup = inject(CompanySetupStore);
  private readonly toasts = inject(ToastStore);
  private readonly transloco = inject(TranslocoService);

  private readonly model = signal<CompanyModel>(this.initialModel());
  protected readonly companyForm = createCompanyForm(this.model);
  protected readonly generalError = signal<string | null>(null);
  protected readonly submitting = computed(() => this.companyForm().submitting());

  protected readonly liabilityIds = computed(() => new Set(this.model().type_liability_ids));
  // Listas de los catálogos de la DIAN en el Api; el municipio se busca en el servidor (son 1.122).
  private readonly catalogs = inject(CatalogsStore);
  private readonly other = (id: number) => this.text('common.otherOption', { id });
  private readonly catalogField = (name: SmallCatalogName, current: () => string) => {
    const items = this.catalogs.items(name);
    return computed(() => optionsWith(catalogOptions(name, items()), current(), this.other));
  };
  private readonly liabilityItems = this.catalogs.items('liabilities');
  protected readonly liabilities = computed(() =>
    catalogOptions('liabilities', this.liabilityItems()),
  );
  protected readonly identificationTypes = this.catalogField(
    'identification-types',
    () => this.model().type_document_identification_id,
  );
  protected readonly organizationTypes = this.catalogField(
    'organization-types',
    () => this.model().type_organization_id,
  );
  protected readonly regimes = this.catalogField('regimes', () => this.model().type_regime_id);
  protected readonly location = municipalityPicker({
    current: () => this.model().municipality_id,
    otherName: this.other,
    clear: () => this.model.update((current) => ({ ...current, municipality_id: '' })),
  });
  protected readonly municipalities = this.location.municipalities;
  protected readonly departmentLabels: ComboboxLabels = {
    placeholder: this.text('data.departmentPlaceholder'),
    loading: this.text('common.catalog.loading'),
    error: this.text('common.catalog.error'),
    empty: this.text('data.departmentNone'),
    retry: this.text('common.retry'),
  };

  protected readonly municipalityLabels: ComboboxLabels = {
    placeholder: this.text('data.municipalityPlaceholder'),
    idle: this.text('data.municipalityIdle'),
    loading: this.text('common.catalog.loading'),
    error: this.text('common.catalog.error'),
    empty: this.text('data.municipalityNone'),
    retry: this.text('common.retry'),
  };

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

  constructor() {
    this.load();
  }

  hasUnsavedChanges(): boolean {
    return this.companyForm().dirty();
  }

  /** Lee los datos guardados; si llegan antes de que se escriba algo, llenan el formulario. */
  protected load(): void {
    void this.setup.loadCompany().then((company) => {
      if (company && !this.companyForm().dirty()) {
        this.model.set(companyModel(company));
      }
    });
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
    const saved = this.setup.companyData();
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
