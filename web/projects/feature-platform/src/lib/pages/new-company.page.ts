import { HttpContext } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormField, submit } from '@angular/forms/signals';
import { RouterLink } from '@angular/router';
import { Api, createCompany } from '@impifact/api';
import {
  AppError,
  CatalogsStore,
  SKIP_ERROR_TOAST,
  SmallCatalogName,
  ToastStore,
  catalogOptions,
  municipalityPicker,
  optionsWith,
  syncCheckDigit,
} from '@impifact/core';
import {
  ButtonComponent,
  ComboboxComponent,
  ComboboxLabels,
  DialogComponent,
  DigitsOnlyDirective,
  FieldComponent,
  HelpComponent,
  InputDirective,
  PageTitleComponent,
  SelectDirective,
} from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import {
  CompanyUserDialogComponent,
  UserCompany,
} from '../components/company-user-dialog.component';
import { PlatformNavComponent } from '../components/platform-nav.component';
import {
  NEW_COMPANY_FIELDS,
  NewCompanyModel,
  createNewCompanyForm,
  emptyNewCompany,
  newCompanyCommand,
} from '../forms/new-company.form';
import { fieldMessage, serverFieldErrors } from '../forms/platform.forms';
import { platformText, providePlatformI18n } from '../platform-i18n';

/** Resultado del alta: la empresa creada (o actualizada, si su NIT ya existía). */
interface CreatedCompany extends UserCompany {
  nit: string;
  dv: string;
  created: boolean;
}

/** Formato propio del error de patrón de algunos campos. */
const FORMAT_KEYS: Partial<Record<keyof NewCompanyModel, string>> = {
  nit: 'newCompany.nitFormat',
  phone: 'newCompany.phoneFormat',
};

/**
 * Página `/plataforma/empresas/nueva`: el administrador de la plataforma da de alta una empresa (`createCompany`,
 * `POST /api/v1/companies/{nit}/{dv}`) con los catálogos de la DIAN del Api. El NIT y el teléfono solo admiten
 * dígitos; el dígito de verificación se calcula del NIT (el Api lo vuelve a revisar); el municipio se elige después
 * del departamento, en un combobox con búsqueda; cada campo explica qué poner en su tooltip. Los errores por campo
 * del Api salen junto a cada campo. La llave de API inicial de la empresa se muestra **una sola
 * vez** en un diálogo con botón de copiar y sale de la memoria al cerrarlo; enseguida se ofrece crear el dueño. Si
 * el NIT ya existía, el Api actualiza sus datos sin llave nueva y la página lo dice.
 */
@Component({
  selector: 'feat-new-company-page',
  imports: [
    FormField,
    RouterLink,
    TranslocoPipe,
    ButtonComponent,
    DialogComponent,
    FieldComponent,
    HelpComponent,
    InputDirective,
    PageTitleComponent,
    ComboboxComponent,
    DigitsOnlyDirective,
    SelectDirective,
    CompanyUserDialogComponent,
    PlatformNavComponent,
  ],
  providers: [providePlatformI18n()],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './new-company.page.html',
  styleUrl: './platform-page.css',
})
export class NewCompanyPage {
  private readonly api = inject(Api);
  private readonly transloco = inject(TranslocoService);
  private readonly toasts = inject(ToastStore);

  private readonly model = signal<NewCompanyModel>(emptyNewCompany());
  protected readonly companyForm = createNewCompanyForm(this.model);
  protected readonly submitting = computed(() => this.companyForm().submitting());
  protected readonly generalError = signal<string | null>(null);

  /** Empresa recién guardada; con ella la página muestra el resultado en vez del formulario. */
  protected readonly result = signal<CreatedCompany | null>(null);
  /** Llave de API inicial; solo vive mientras su diálogo está abierto. */
  protected readonly secret = signal<string | null>(null);
  protected readonly secretOpen = computed(() => this.secret() !== null);
  protected readonly copied = signal(false);
  protected readonly userOpen = signal(false);
  protected readonly userDone = signal<string | null>(null);

  protected readonly resultText = computed(() => {
    const company = this.result();
    if (!company) {
      return null;
    }
    const params = { name: company.name, nit: company.nit, dv: company.dv };
    return this.text(company.created ? 'newCompany.created' : 'newCompany.updated', params);
  });
  protected readonly userButtonLabel = computed(() =>
    this.text('user.title', { name: this.result()?.name ?? '' }),
  );

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
  protected readonly municipalityLabels: ComboboxLabels = {
    idle: this.text('newCompany.municipalityIdle'),
    loading: this.text('common.catalog.loading'),
    error: this.text('common.catalog.error'),
    empty: this.text('newCompany.municipalityNone'),
    retry: this.text('common.retry'),
  };

  constructor() {
    // El DV no se escribe: sale del NIT.
    syncCheckDigit(this.model, (current) => current.nit);
  }

  protected readonly errors = computed(() => {
    const translate = (key: string, params?: Record<string, unknown>) => this.text(key, params);
    const messages = Object.fromEntries(
      NEW_COMPANY_FIELDS.map((name) => [
        name,
        fieldMessage(this.companyForm[name](), translate, FORMAT_KEYS[name]),
      ]),
    ) as Record<keyof NewCompanyModel, string | null>;
    const liabilities = this.companyForm.type_liability_ids();
    if (liabilities.touched() && liabilities.errors()[0]?.kind === 'liabilitiesRequired') {
      messages.type_liability_ids = this.text('newCompany.liabilitiesRequired');
    }
    return messages;
  });

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
        const model = this.model();
        const nit = model.nit.trim();
        const dv = model.dv.trim();
        try {
          const saved = await firstValueFrom(
            this.api.invoke(
              createCompany,
              { nit, dv, body: newCompanyCommand(model) },
              new HttpContext().set(SKIP_ERROR_TOAST, true),
            ),
          );
          const created = saved.created !== false;
          this.result.set({
            id: saved.company.id ?? '',
            name: saved.company.business_name ?? model.business_name.trim(),
            nit: saved.company.identification_number ?? nit,
            dv: saved.company.dv ?? dv,
            created,
          });
          this.userDone.set(null);
          this.copied.set(false);
          if (created && saved.token) {
            this.secret.set(saved.token);
          }
        } catch (error) {
          return this.showError(error);
        }
        return undefined;
      },
      onInvalid: () => this.focusFirstInvalid(),
    });
  }

  /** Al cerrar el diálogo (botón, X o Escape) la llave sale de la memoria y se ofrece crear el dueño. */
  protected onSecretVisible(visible: boolean): void {
    if (visible || this.secret() === null) {
      return;
    }
    this.secret.set(null);
    this.copied.set(false);
    this.userOpen.set(true);
  }

  protected async copySecret(): Promise<void> {
    const key = this.secret();
    if (!key) {
      return;
    }
    try {
      await navigator.clipboard.writeText(key);
      this.copied.set(true);
    } catch {
      this.toasts.show({ kind: 'warning', text: this.text('companyKey.copyFailed') });
    }
  }

  /** Vuelve al formulario vacío para dar de alta otra empresa. */
  protected startOver(): void {
    this.result.set(null);
    this.userDone.set(null);
    this.model.set(emptyNewCompany());
    this.companyForm().reset();
    this.location.selectDepartment('');
  }

  private showError(error: unknown) {
    if (!(error instanceof AppError)) {
      this.generalError.set(this.text('common.unexpected'));
      return undefined;
    }
    if (error.code === 'company.email_taken') {
      return [{ kind: 'server', message: error.message, fieldTree: this.companyForm.email }];
    }
    if (error.code === 'company.platform_only') {
      this.generalError.set(this.text('newCompany.platformOnly'));
      return undefined;
    }
    const onFields = serverFieldErrors(
      this.companyForm,
      NEW_COMPANY_FIELDS.filter((name) => name !== 'dv'),
      error,
    );
    // El DV sale del NIT y es de solo lectura: si el Api lo rechaza, el que hay que corregir es el NIT.
    const dv = error.fieldErrors['dv']?.[0];
    if (dv) {
      onFields.push({ kind: 'server', message: dv, fieldTree: this.companyForm.nit as never });
    }
    // El Api nombra `type_liability_id` cuando falta la responsabilidad (regla heredada del PHP).
    const liability = error.fieldErrors['type_liability_id']?.[0];
    if (liability) {
      onFields.push({
        kind: 'server',
        message: liability,
        fieldTree: this.companyForm.type_liability_ids as never,
      });
    }
    if (onFields.length === 0) {
      this.generalError.set(error.message || this.text('common.unexpected'));
    }
    return onFields;
  }

  private focusFirstInvalid(): void {
    const first = NEW_COMPANY_FIELDS.filter((name) => name !== 'type_liability_ids')
      .map((name) => this.companyForm[name])
      .find((field) => field().invalid());
    first?.().focusBoundControl();
  }

  protected text(key: string, params?: Record<string, unknown>): string {
    return platformText(this.transloco, key, params);
  }
}
