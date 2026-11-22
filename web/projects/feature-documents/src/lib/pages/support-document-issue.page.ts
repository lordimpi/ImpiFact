import { HttpContext } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { FieldState, FormField } from '@angular/forms/signals';
import { Router, RouterLink } from '@angular/router';
import { Api, issueSupportDocument } from '@impifact/api';
import {
  AppError,
  CatalogsStore,
  HasUnsavedChanges,
  IDEMPOTENCY_KEY,
  ToastStore,
  catalogOptions,
} from '@impifact/core';
import {
  ButtonComponent,
  ButtonLinkDirective,
  CheckboxComponent,
  CardActionsDirective,
  CardComponent,
  DatePickerComponent,
  FieldComponent,
  InputDirective,
  NonNegativeDirective,
  PageTitleComponent,
  SelectComponent,
} from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { LineFieldsComponent } from '../components/line-fields.component';
import { TotalsPanelComponent } from '../components/totals-panel.component';
import { DOCUMENTS_I18N_SCOPE, provideDocumentsI18n } from '../documents-i18n';
import {
  CREDIT_PAYMENT_FORM,
  PAYMENT_FORMS,
  PAYMENT_METHODS,
  buildLinesCalculation,
} from '../forms/invoice.form';
import {
  GENERATION_MODES,
  NON_RESIDENT_OPERATION,
  SELLER_IDENTIFICATION_TYPES,
  SUPPORT_OPERATIONS,
  SellerModel,
  SupportDocumentModel,
  buildSupportDocumentBody,
  createSupportDocumentForm,
  emptySupportDocument,
  emptySupportLine,
} from '../forms/support-document.form';
import { serverTotals } from '../state/server-totals';

type SellerField = keyof SellerModel;

/** Texto del error de formato de cada campo del proveedor (`supportDocument.errors.*`). */
const FORMAT_ERRORS: Partial<Record<SellerField, string>> = {
  identificationNumber: 'identificationFormat',
  dv: 'dvFormat',
  email: 'emailFormat',
  postalZoneCode: 'postalFormat',
};

/**
 * Emisión de documento soporte en adquisiciones a no obligados a facturar (`/documentos/documento-soporte`): la
 * empresa compra a un proveedor que no factura y es ella quien emite el documento. El proveedor se escribe aquí (no
 * es un cliente del maestro). Los totales e impuestos siempre vienen de `calculateDocumentTotals`, como en la
 * factura, y «Emitir» manda esos valores a `issueSupportDocument` con la `Idempotency-Key` creada al abrir el
 * formulario. El Api no tiene vista previa del documento soporte, así que esta página no la ofrece.
 */
@Component({
  selector: 'feat-support-document-issue-page',
  imports: [
    ButtonLinkDirective,
    FormField,
    RouterLink,
    TranslocoPipe,
    ButtonComponent,
    CheckboxComponent,
    CardComponent,
    CardActionsDirective,
    DatePickerComponent,
    FieldComponent,
    InputDirective,
    NonNegativeDirective,
    PageTitleComponent,
    SelectComponent,
    LineFieldsComponent,
    TotalsPanelComponent,
  ],
  providers: [provideDocumentsI18n()],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './support-document-issue.page.html',
  styleUrls: ['./documents-page.css', './invoice-issue.page.css'],
})
export class SupportDocumentIssuePage implements HasUnsavedChanges {
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  private readonly toasts = inject(ToastStore);
  private readonly transloco = inject(TranslocoService);
  private readonly catalogs = inject(CatalogsStore);

  /** Una llave por formulario abierto: un reintento del mismo envío devuelve el mismo documento. */
  private readonly idempotencyKey = crypto.randomUUID();

  private readonly model = signal<SupportDocumentModel>({
    ...emptySupportDocument(),
    lines: [emptySupportLine()],
  });
  protected readonly supportForm = createSupportDocumentForm(this.model);
  protected readonly submitting = signal(false);
  protected readonly issueError = signal<{ message: string; details: string[] } | null>(null);
  private issued = false;

  /** Correo del proveedor, para explicar a dónde irá el documento (vacío si no tiene). */
  protected readonly sellerEmail = computed(() => this.supportForm.seller.email().value().trim());
  protected readonly nonResident = computed(
    () => this.supportForm.operation().value() === NON_RESIDENT_OPERATION,
  );
  protected readonly onCredit = computed(
    () => this.supportForm.paymentFormId().value() === CREDIT_PAYMENT_FORM,
  );

  protected readonly operations = computed(() =>
    SUPPORT_OPERATIONS.map((id) => ({ id, name: this.text(`supportDocument.operations.${id}`) })),
  );
  protected readonly generationModes = computed(() =>
    GENERATION_MODES.map((id) => ({
      id,
      name: this.text(`supportDocument.generationModes.${id}`),
    })),
  );
  protected readonly paymentForms = computed(() =>
    PAYMENT_FORMS.map((id) => ({ id, name: this.text(`issue.paymentForms.${id}`) })),
  );
  protected readonly paymentMethods = computed(() =>
    PAYMENT_METHODS.map((id) => ({ id, name: this.text(`issue.paymentMethods.${id}`) })),
  );
  /** Solo los tipos de identificación que el Api acepta para el proveedor. */
  protected readonly identificationTypes = computed(() =>
    catalogOptions(
      'identification-types',
      (this.catalogs.entries()['identification-types']?.items ?? []).filter((item) =>
        SELLER_IDENTIFICATION_TYPES.includes(Number(item.id)),
      ),
    ),
  );
  protected readonly countries = computed(() =>
    catalogOptions('countries', this.catalogs.entries()['countries']?.items ?? []),
  );

  /** Totales e impuestos del servidor para las líneas de este momento. */
  protected readonly totals = serverTotals(
    computed(() => buildLinesCalculation(this.supportForm().value().lines)),
  );
  protected readonly calculated = this.totals.calculated;

  protected readonly canIssue = computed(
    () => !this.submitting() && this.calculated() !== null && this.supportForm().valid(),
  );

  constructor() {
    void this.catalogs.load('identification-types');
    // The country list is only needed for a seller outside Colombia.
    effect(() => {
      if (this.nonResident()) void this.catalogs.load('countries');
    });
  }

  hasUnsavedChanges(): boolean {
    return !this.issued && this.supportForm().dirty();
  }

  protected addLine(): void {
    this.supportForm.lines().value.update((lines) => [...lines, emptySupportLine()]);
  }

  protected removeLine(index: number): void {
    this.supportForm.lines().value.update((lines) => lines.filter((_, i) => i !== index));
  }

  /** Campos de la línea `index` (siempre existe: la plantilla recorre las líneas del modelo). */
  protected lineFields(index: number) {
    return this.supportForm.lines[index] as NonNullable<(typeof this.supportForm.lines)[number]>;
  }

  /** Valor de la línea que calculó el servidor, si el cálculo está al día. */
  protected lineAmount(index: number): number | string | undefined {
    return this.calculated()?.lines[index]?.line_extension_amount;
  }

  /** Error de un campo del proveedor, cuando la persona ya pasó por él. */
  protected sellerError(field: SellerField): string | null {
    return this.errorOf(this.supportForm.seller[field](), FORMAT_ERRORS[field]);
  }

  protected fieldError(
    field: 'startDate' | 'paymentDueDate' | 'durationDays' | 'operation',
  ): string | null {
    return this.errorOf(this.supportForm[field]());
  }

  protected async issue(event: Event): Promise<void> {
    event.preventDefault();
    const calculated = this.calculated();
    if (!this.canIssue() || !calculated) {
      return;
    }
    this.submitting.set(true);
    this.issueError.set(null);
    try {
      const body = buildSupportDocumentBody(this.supportForm().value(), calculated);
      const response = await firstValueFrom(
        this.api.invoke(
          issueSupportDocument,
          { body },
          new HttpContext().set(IDEMPOTENCY_KEY, this.idempotencyKey),
        ),
      );
      this.issued = true;
      this.toasts.show({
        kind: 'success',
        text: this.text('supportDocument.issued', {
          number: response.document.full_number ?? '',
        }),
      });
      await this.router.navigate(['/documentos', response.document.id]);
    } catch (error) {
      // Los errores sin detalle por campo ya los avisa core; los de validación se muestran aquí.
      if (error instanceof AppError && error.hasFieldErrors) {
        this.issueError.set({
          message: error.message,
          details: Object.values(error.fieldErrors).flat(),
        });
      }
    } finally {
      this.submitting.set(false);
    }
  }

  private errorOf(state: FieldState<unknown>, formatKey?: string): string | null {
    const errors = state.errors();
    if (!state.touched() || errors.length === 0) {
      return null;
    }
    const kind = errors[0]?.kind;
    if (kind === 'required') return this.text('supportDocument.errors.required');
    if (kind === 'maxLength') return this.text('supportDocument.errors.tooLong');
    return this.text(`supportDocument.errors.${formatKey ?? 'required'}`);
  }

  private text(key: string, params?: Record<string, string>): string {
    return this.transloco.translate(`${DOCUMENTS_I18N_SCOPE}.${key}`, params);
  }
}
