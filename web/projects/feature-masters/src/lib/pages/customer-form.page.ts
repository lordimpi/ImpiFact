import { HttpContext } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { FormField, submit } from '@angular/forms/signals';
import { Router, RouterLink } from '@angular/router';
import {
  Api,
  CustomerResponse,
  createCustomer,
  getCustomer,
  lookupAcquirer,
  updateCustomer,
} from '@impifact/api';
import { AppError, HasUnsavedChanges, SKIP_ERROR_TOAST, ToastStore } from '@impifact/core';
import {
  ButtonComponent,
  ErrorStateComponent,
  FieldComponent,
  InputDirective,
  LoadingComponent,
  PageTitleComponent,
  SelectDirective,
} from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import {
  CUSTOMER_FIELDS,
  CustomerModel,
  createCustomerForm,
  customerCommand,
  customerModel,
  emptyCustomer,
  isNit,
} from '../forms/customer.form';
import { fieldMessage, serverFieldErrors } from '../forms/form-messages';
import { mastersText, provideMastersI18n } from '../masters-i18n';
import {
  IDENTIFICATION_TYPES,
  LIABILITIES,
  MUNICIPALITIES,
  ORGANIZATION_TYPES,
  REGIMES,
  optionsWith,
} from '../models/catalogs';

/** Resultado de la consulta a la DIAN que se muestra junto al botón. */
type LookupState =
  | { kind: 'idle' }
  | { kind: 'loading' }
  | { kind: 'info'; text: string }
  | { kind: 'error'; text: string };

/** Errores de guardar y de consultar se muestran en el formulario, no en un aviso flotante. */
const IN_FORM = () => new HttpContext().set(SKIP_ERROR_TOAST, true);

/**
 * Alta (`/clientes/nuevo`) y edición (`/clientes/:id`) de un cliente. «Buscar en la DIAN» consulta el adquiriente
 * con la identificación escrita y llena el nombre y el correo registrados allá. Los errores por campo del Api se
 * muestran junto a cada campo; salir con cambios sin guardar pide confirmación.
 */
@Component({
  selector: 'feat-customer-form-page',
  imports: [
    FormField,
    RouterLink,
    TranslocoPipe,
    ButtonComponent,
    ErrorStateComponent,
    FieldComponent,
    InputDirective,
    LoadingComponent,
    PageTitleComponent,
    SelectDirective,
  ],
  providers: [provideMastersI18n()],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './customer-form.page.html',
  styleUrl: './masters-page.css',
})
export class CustomerFormPage implements HasUnsavedChanges {
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  private readonly toasts = inject(ToastStore);
  private readonly transloco = inject(TranslocoService);

  /** Id del cliente en la ruta; sin él, es un alta. */
  readonly id = input<string>();

  private readonly model = signal<CustomerModel>(emptyCustomer());
  protected readonly customerForm = createCustomerForm(this.model);

  /** Cliente cargado (edición): conserva su estado al guardar. */
  private readonly loaded = signal<CustomerResponse | null>(null);
  protected readonly loadState = signal<'loading' | 'ready' | 'error'>('ready');
  protected readonly generalError = signal<string | null>(null);
  protected readonly lookup = signal<LookupState>({ kind: 'idle' });
  protected readonly lookupText = computed(() => {
    const state = this.lookup();
    return state.kind === 'info' || state.kind === 'error' ? state.text : '';
  });
  protected readonly submitting = computed(() => this.customerForm().submitting());
  private saved = false;

  protected readonly isNit = computed(() => isNit(this.model().type_document_identification_id));
  protected readonly isEdit = computed(() => !!this.id());

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
  protected readonly liabilities = computed(() =>
    optionsWith(LIABILITIES, this.model().type_liability_id, this.other),
  );
  protected readonly municipalities = computed(() =>
    optionsWith(MUNICIPALITIES, this.model().municipality_id, this.other),
  );

  /** Mensaje bajo cada campo, por nombre del campo. */
  protected readonly errors = computed(() => {
    const translate = (key: string, params?: Record<string, unknown>) => this.text(key, params);
    return Object.fromEntries(
      CUSTOMER_FIELDS.map((name) => [name, fieldMessage(this.customerForm[name](), translate)]),
    ) as Record<keyof CustomerModel, string | null>;
  });

  constructor() {
    effect(() => {
      const id = this.id();
      if (id) {
        untracked(() => void this.load(id));
      }
    });
  }

  hasUnsavedChanges(): boolean {
    return !this.saved && this.customerForm().dirty();
  }

  protected async load(id = this.id()): Promise<void> {
    if (!id) {
      return;
    }
    this.loadState.set('loading');
    try {
      const customer = await firstValueFrom(this.api.invoke(getCustomer, { id }, IN_FORM()));
      this.loaded.set(customer);
      this.model.set(customerModel(customer));
      this.loadState.set('ready');
    } catch {
      this.loadState.set('error');
    }
  }

  protected async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    this.generalError.set(null);
    await submit(this.customerForm, {
      action: async () => {
        try {
          await this.save();
        } catch (error) {
          return this.showError(error);
        }
        this.saved = true;
        this.toasts.show({ kind: 'success', text: this.text('customers.form.saved') });
        await this.router.navigateByUrl('/clientes');
        return undefined;
      },
      onInvalid: () => this.focusFirstInvalid(),
    });
  }

  /** Consulta el adquiriente en la DIAN y llena el nombre y el correo que tenga registrados. */
  protected async lookUp(): Promise<void> {
    const { type_document_identification_id: type, identification_number: number } = this.model();
    if (!type || !number.trim()) {
      this.lookup.set({
        kind: 'error',
        text: this.text('customers.form.lookup.needsIdentification'),
      });
      return;
    }
    this.lookup.set({ kind: 'loading' });
    try {
      const result = await firstValueFrom(
        this.api.invoke(lookupAcquirer, { type, number: number.trim() }, IN_FORM()),
      );
      if (!result.found) {
        this.lookup.set({ kind: 'info', text: this.text('customers.form.lookup.notFound') });
        return;
      }
      this.prefill(result.name ?? null, result.email ?? null);
      this.lookup.set({
        kind: 'info',
        text: this.text('customers.form.lookup.found', { name: result.name ?? '' }),
      });
    } catch (error) {
      this.lookup.set({ kind: 'error', text: this.lookupError(error) });
    }
  }

  private prefill(name: string | null, email: string | null): void {
    this.model.update((current) => ({
      ...current,
      name: name ?? current.name,
      email: email ?? current.email,
    }));
    if (name) {
      this.customerForm.name().markAsDirty();
    }
    if (email) {
      this.customerForm.email().markAsDirty();
    }
  }

  private lookupError(error: unknown): string {
    if (!(error instanceof AppError)) {
      return this.text('customers.form.lookup.failed');
    }
    if (error.status === 503) {
      return this.text('customers.form.lookup.unavailable');
    }
    if (error.code === 'certificate.not_found') {
      return this.text('customers.form.lookup.noCertificate');
    }
    if (error.status === 422) {
      const message = Object.values(error.fieldErrors)[0]?.[0] ?? error.message;
      return this.text('customers.form.lookup.rejected', { message });
    }
    return this.text('customers.form.lookup.failed');
  }

  private save(): Promise<unknown> {
    const body = customerCommand(this.model());
    const id = this.id();
    if (!id) {
      return firstValueFrom(this.api.invoke(createCustomer, { body }, IN_FORM()));
    }
    const isActive = this.loaded()?.is_active ?? true;
    return firstValueFrom(
      this.api.invoke(updateCustomer, { id, body: { ...body, is_active: isActive } }, IN_FORM()),
    );
  }

  /** Errores por campo junto a su campo; el resto en el aviso general del formulario. */
  private showError(error: unknown) {
    if (!(error instanceof AppError)) {
      this.generalError.set(this.text('common.errors.unexpected'));
      return undefined;
    }
    const onFields = serverFieldErrors(this.customerForm, CUSTOMER_FIELDS, error);
    if (onFields.length === 0) {
      this.generalError.set(
        error.code === 'customer.duplicate' ? this.text('customers.form.duplicate') : error.message,
      );
    }
    return onFields;
  }

  private focusFirstInvalid(): void {
    const first = CUSTOMER_FIELDS.map((name) => this.customerForm[name]).find((field) =>
      field().invalid(),
    );
    first?.().focusBoundControl();
  }

  protected text(key: string, params?: Record<string, unknown>): string {
    return mastersText(this.transloco, key, params);
  }
}
