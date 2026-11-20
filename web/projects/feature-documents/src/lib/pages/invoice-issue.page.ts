import { HttpContext } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormField } from '@angular/forms/signals';
import { Router, RouterLink } from '@angular/router';
import {
  Api,
  CustomerResponse,
  issueInvoice,
  listCustomers,
  listProducts,
  previewInvoice,
} from '@impifact/api';
import {
  AppError,
  HasUnsavedChanges,
  IDEMPOTENCY_KEY,
  SKIP_ERROR_TOAST,
  ToastStore,
} from '@impifact/core';
import {
  ButtonComponent,
  CardActionsDirective,
  CardComponent,
  FieldComponent,
  InputDirective,
  NonNegativeDirective,
  PageTitleComponent,
  SelectDirective,
} from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { firstValueFrom, map } from 'rxjs';
import { LineFieldsComponent } from '../components/line-fields.component';
import {
  PickerLabels,
  PickerOption,
  SearchPickerComponent,
} from '../components/search-picker.component';
import { TotalsPanelComponent } from '../components/totals-panel.component';
import { DOCUMENTS_I18N_SCOPE, provideDocumentsI18n } from '../documents-i18n';
import {
  CREDIT_PAYMENT_FORM,
  PAYMENT_FORMS,
  PAYMENT_METHODS,
  buildCalculationRequest,
  buildInvoiceBody,
  createInvoiceForm,
  emptyLine,
  lineFromProduct,
} from '../forms/invoice.form';
import { PdfPreviewComponent } from '../components/pdf-preview.component';
import { DocumentActions, previewProblem } from '../state/document-actions';
import { debouncedSearch } from '../state/debounced-search';
import { serverTotals } from '../state/server-totals';

const SEARCH_LIMIT = 10;
const silent = () => new HttpContext().set(SKIP_ERROR_TOAST, true);

/**
 * Emisión de factura (`/documentos/nuevo`, sección 8.1). Los totales e impuestos siempre vienen del servidor
 * (`calculateDocumentTotals`, pedido de nuevo cada vez que cambian las líneas) y «Emitir» manda esos mismos valores
 * con la `Idempotency-Key` generada al abrir el formulario: reintentar no emite dos veces. «Vista previa» manda el
 * mismo cuerpo a `previewInvoice` y abre el PDF sin número ni CUFE, sin gastar consecutivo.
 */
@Component({
  selector: 'feat-invoice-issue-page',
  imports: [
    FormField,
    RouterLink,
    TranslocoPipe,
    ButtonComponent,
    CardComponent,
    CardActionsDirective,
    FieldComponent,
    InputDirective,
    NonNegativeDirective,
    PageTitleComponent,
    SelectDirective,
    LineFieldsComponent,
    SearchPickerComponent,
    TotalsPanelComponent,
    PdfPreviewComponent,
  ],
  providers: [provideDocumentsI18n(), DocumentActions],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './invoice-issue.page.html',
  styleUrls: ['./documents-page.css', './invoice-issue.page.css'],
})
export class InvoiceIssuePage implements HasUnsavedChanges {
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  private readonly toasts = inject(ToastStore);
  private readonly transloco = inject(TranslocoService);
  private readonly actions = inject(DocumentActions);

  /** Una llave por formulario abierto: un reintento del mismo envío devuelve el mismo documento. */
  private readonly idempotencyKey = crypto.randomUUID();

  protected readonly invoiceForm = createInvoiceForm();
  protected readonly paymentForms = PAYMENT_FORMS;
  protected readonly paymentMethods = PAYMENT_METHODS;

  protected readonly customer = signal<CustomerResponse | null>(null);
  /** Correo del cliente elegido, para explicar a dónde irá la factura (vacío si no tiene). */
  protected readonly customerEmail = computed(() => {
    const email = this.customer()?.email;
    return typeof email === 'string' ? email.trim() : '';
  });
  protected readonly submitting = signal(false);
  protected readonly issueError = signal<{ message: string; details: string[] } | null>(null);
  protected readonly previewing = signal(false);
  /** PDF de la vista previa abierto en el diálogo; `null` si está cerrado. */
  protected readonly previewPdf = signal<Blob | null>(null);
  protected readonly previewError = signal<string | null>(null);
  private issued = false;

  protected readonly onCredit = computed(
    () => this.invoiceForm.paymentFormId().value() === CREDIT_PAYMENT_FORM,
  );
  protected readonly lineCount = computed(() => this.invoiceForm.lines().value().length);

  // Búsquedas de clientes y productos en el Api (nunca se descarga la lista completa).
  private readonly customerTerm = signal('');
  private readonly productTerm = signal('');
  protected readonly customerSearch = debouncedSearch(this.customerTerm, (term) =>
    this.api
      .invoke(listCustomers, { search: term, limit: SEARCH_LIMIT }, silent())
      .pipe(map((page) => page.items ?? [])),
  );
  protected readonly productSearch = debouncedSearch(this.productTerm, (term) =>
    this.api
      .invoke(listProducts, { search: term, limit: SEARCH_LIMIT }, silent())
      .pipe(map((page) => page.items ?? [])),
  );

  protected readonly customerOptions = computed<PickerOption[]>(() =>
    this.customerSearch().items.map((item) => ({
      id: item.id ?? '',
      title: item.name ?? '',
      detail: [item.identification_number, item.email].filter(Boolean).join(' · '),
    })),
  );
  protected readonly productOptions = computed<PickerOption[]>(() =>
    this.productSearch().items.map((item) => ({
      id: item.id ?? '',
      title: item.name ?? '',
      detail: item.code ?? '',
    })),
  );
  protected readonly customerLabels: PickerLabels = this.pickerLabels('customer');
  protected readonly productLabels: PickerLabels = this.pickerLabels('product');

  /** Totales e impuestos del servidor para las líneas de este momento. */
  protected readonly totals = serverTotals(
    computed(() => buildCalculationRequest(this.invoiceForm().value())),
  );
  protected readonly calculated = this.totals.calculated;

  protected readonly canIssue = computed(
    () =>
      !this.submitting() &&
      this.customer() !== null &&
      this.calculated() !== null &&
      this.invoiceForm().valid(),
  );

  hasUnsavedChanges(): boolean {
    return !this.issued && (this.invoiceForm().dirty() || this.customer() !== null);
  }

  protected searchCustomers(term: string): void {
    this.customerTerm.set(term);
  }

  protected chooseCustomer(id: string): void {
    this.customer.set(this.customerSearch().items.find((item) => item.id === id) ?? null);
    this.customerTerm.set('');
  }

  protected clearCustomer(): void {
    this.customer.set(null);
  }

  protected searchProducts(term: string): void {
    this.productTerm.set(term);
  }

  protected addProduct(id: string): void {
    const product = this.productSearch().items.find((item) => item.id === id);
    if (product) {
      this.invoiceForm.lines().value.update((lines) => [...lines, lineFromProduct(product)]);
      this.productTerm.set('');
    }
  }

  protected addFreeLine(): void {
    this.invoiceForm.lines().value.update((lines) => [...lines, emptyLine()]);
  }

  protected removeLine(index: number): void {
    this.invoiceForm.lines().value.update((lines) => lines.filter((_, i) => i !== index));
  }

  /** Campos de la línea `index` (siempre existe: la plantilla recorre las líneas del modelo). */
  protected lineFields(index: number) {
    return this.invoiceForm.lines[index] as NonNullable<(typeof this.invoiceForm.lines)[number]>;
  }

  /** Valor de la línea que calculó el servidor, si el cálculo está al día. */
  protected lineAmount(index: number): number | string | undefined {
    return this.calculated()?.lines[index]?.line_extension_amount;
  }

  protected fieldError(field: 'paymentDueDate' | 'durationDays'): string | null {
    const state = this.invoiceForm[field]();
    if (!state.touched() || state.errors().length === 0) {
      return null;
    }
    return this.text(
      field === 'paymentDueDate' ? 'issue.dueDateRequired' : 'issue.durationRequired',
    );
  }

  protected async issue(event: Event): Promise<void> {
    event.preventDefault();
    const customer = this.customer();
    const calculated = this.calculated();
    if (!this.canIssue() || !customer || !calculated) {
      return;
    }
    this.submitting.set(true);
    this.issueError.set(null);
    try {
      const body = buildInvoiceBody(this.invoiceForm().value(), customer, calculated);
      const response = await firstValueFrom(
        this.api.invoke(
          issueInvoice,
          { body },
          new HttpContext().set(IDEMPOTENCY_KEY, this.idempotencyKey),
        ),
      );
      this.issued = true;
      this.toasts.show({
        kind: 'success',
        text: this.text('issue.issued', { number: response.document.full_number ?? '' }),
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

  protected savePreview(): void {
    const pdf = this.previewPdf();
    if (pdf) this.actions.savePreview(pdf);
  }

  /** Abre el PDF que tendría la factura (sin emitirla); un 503 es el conversor de PDF caído. */
  protected async preview(): Promise<void> {
    const customer = this.customer();
    const calculated = this.calculated();
    if (!this.canIssue() || this.previewing() || !customer || !calculated) {
      return;
    }
    this.previewing.set(true);
    this.previewError.set(null);
    this.issueError.set(null);
    const body = buildInvoiceBody(this.invoiceForm().value(), customer, calculated);
    const result = await this.actions.fetchPreview(previewInvoice.PATH, body);
    this.previewing.set(false);
    if (result.ok) {
      this.previewPdf.set(result.pdf);
      return;
    }
    const problem = previewProblem(result.error);
    this.issueError.set(problem.validation);
    this.previewError.set(
      problem.notice &&
        ('key' in problem.notice ? this.text(problem.notice.key) : problem.notice.message),
    );
  }

  private pickerLabels(kind: 'customer' | 'product'): PickerLabels {
    return {
      search: this.text(`issue.${kind}Search`),
      hint: this.text(`issue.${kind}SearchHint`),
      results: this.text(`issue.${kind}Results`),
      none: this.text(`issue.${kind}None`),
      searching: this.text('issue.searching'),
      error: this.text('issue.searchError'),
      choose: this.text(`issue.${kind}Choose`),
    };
  }

  private text(key: string, params?: Record<string, string>): string {
    return this.transloco.translate(`${DOCUMENTS_I18N_SCOPE}.${key}`, params);
  }
}
