import { HttpContext } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  Signal,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { FormField } from '@angular/forms/signals';
import { Router, RouterLink } from '@angular/router';
import {
  Api,
  CalculateDocumentTotalsRequest,
  CustomerResponse,
  DocumentTotalsCalculation,
  calculateDocumentTotals,
  issueInvoice,
  listCustomers,
  listProducts,
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
  FieldComponent,
  InputDirective,
  PageTitleComponent,
  SelectDirective,
} from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import {
  Observable,
  Subject,
  catchError,
  debounceTime,
  distinctUntilChanged,
  firstValueFrom,
  map,
  merge,
  of,
  startWith,
  switchMap,
} from 'rxjs';
import { CopCurrencyPipe } from '../components/cop-currency.pipe';
import {
  PickerLabels,
  PickerOption,
  SearchPickerComponent,
} from '../components/search-picker.component';
import { TotalsPanelComponent, TotalsState } from '../components/totals-panel.component';
import { DOCUMENTS_I18N_SCOPE, provideDocumentsI18n } from '../documents-i18n';
import {
  CREDIT_PAYMENT_FORM,
  IVA_PERCENTS,
  PAYMENT_FORMS,
  PAYMENT_METHODS,
  buildCalculationRequest,
  buildInvoiceBody,
  createInvoiceForm,
  emptyLine,
  lineFromProduct,
} from '../forms/invoice.form';

/** Resultado del último cálculo pedido al servidor. */
type Calculation =
  | { status: 'empty' }
  | { status: 'loading'; key: string }
  | { status: 'error'; key: string }
  | { status: 'ready'; key: string; result: DocumentTotalsCalculation };

interface SearchState<T> {
  term: string;
  items: T[];
  loading: boolean;
  error: boolean;
}

const CALCULATE_DEBOUNCE_MS = 300;
const SEARCH_DEBOUNCE_MS = 250;
const SEARCH_MIN_LENGTH = 2;
const SEARCH_LIMIT = 10;
const silent = () => new HttpContext().set(SKIP_ERROR_TOAST, true);

/**
 * Emisión de factura (`/documentos/nuevo`, sección 8.1). Los totales e impuestos siempre vienen del servidor
 * (`calculateDocumentTotals`, pedido de nuevo cada vez que cambian las líneas) y «Emitir» manda esos mismos valores
 * con la `Idempotency-Key` generada al abrir el formulario: reintentar no emite dos veces. La vista previa del PDF
 * no está porque el Api v1 aún no tiene ese endpoint.
 */
@Component({
  selector: 'feat-invoice-issue-page',
  imports: [
    FormField,
    RouterLink,
    TranslocoPipe,
    ButtonComponent,
    FieldComponent,
    InputDirective,
    PageTitleComponent,
    SelectDirective,
    CopCurrencyPipe,
    SearchPickerComponent,
    TotalsPanelComponent,
  ],
  providers: [provideDocumentsI18n()],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './invoice-issue.page.html',
  styleUrls: ['./documents-page.css', './invoice-issue.page.css'],
})
export class InvoiceIssuePage implements HasUnsavedChanges {
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  private readonly toasts = inject(ToastStore);
  private readonly transloco = inject(TranslocoService);

  /** Una llave por formulario abierto: un reintento del mismo envío devuelve el mismo documento. */
  private readonly idempotencyKey = crypto.randomUUID();

  protected readonly invoiceForm = createInvoiceForm();
  protected readonly paymentForms = PAYMENT_FORMS;
  protected readonly paymentMethods = PAYMENT_METHODS;
  protected readonly ivaPercents = IVA_PERCENTS;

  protected readonly customer = signal<CustomerResponse | null>(null);
  protected readonly submitting = signal(false);
  protected readonly issueError = signal<{ message: string; details: string[] } | null>(null);
  private issued = false;

  protected readonly onCredit = computed(
    () => this.invoiceForm.paymentFormId().value() === CREDIT_PAYMENT_FORM,
  );
  protected readonly lineCount = computed(() => this.invoiceForm.lines().value().length);

  // Búsquedas de clientes y productos en el Api (nunca se descarga la lista completa).
  private readonly customerTerm = signal('');
  private readonly productTerm = signal('');
  protected readonly customerSearch = this.searchState(this.customerTerm, (term) =>
    this.api
      .invoke(listCustomers, { search: term, limit: SEARCH_LIMIT }, silent())
      .pipe(map((page) => page.items ?? [])),
  );
  protected readonly productSearch = this.searchState(this.productTerm, (term) =>
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

  /** Solicitud de cálculo que corresponde al formulario de este momento (`null` si está incompleto). */
  private readonly calculationRequest = computed(() =>
    buildCalculationRequest(this.invoiceForm().value()),
  );
  private readonly calculationKey = computed(() => keyOf(this.calculationRequest()));
  private readonly retryCalculation = new Subject<void>();
  protected readonly calculation = signal<Calculation>({ status: 'empty' });

  /** El panel muestra «calculando» también mientras espera el *debounce* tras un cambio. */
  protected readonly totalsState = computed<TotalsState>(() => {
    const calculation = this.calculation();
    if (this.calculationRequest() === null) return 'empty';
    if (calculation.status === 'ready' && calculation.key === this.calculationKey()) return 'ready';
    if (calculation.status === 'error' && calculation.key === this.calculationKey()) return 'error';
    return 'loading';
  });
  protected readonly calculated = computed(() => {
    const calculation = this.calculation();
    return this.totalsState() === 'ready' && calculation.status === 'ready'
      ? calculation.result
      : null;
  });
  protected readonly taxes = computed(() =>
    (this.calculated()?.tax_totals ?? []).flatMap((total) => total.tax_subtotals),
  );

  protected readonly canIssue = computed(
    () =>
      !this.submitting() &&
      this.customer() !== null &&
      this.calculated() !== null &&
      this.invoiceForm().valid(),
  );

  constructor() {
    // Un cambio en las líneas pide un cálculo nuevo tras el *debounce*; «Calcular de nuevo» repite el vigente.
    merge(
      toObservable(this.calculationRequest).pipe(
        debounceTime(CALCULATE_DEBOUNCE_MS),
        distinctUntilChanged((previous, next) => keyOf(previous) === keyOf(next)),
      ),
      this.retryCalculation.pipe(map(() => this.calculationRequest())),
    )
      .pipe(
        switchMap((request) => this.calculate(request)),
        takeUntilDestroyed(),
      )
      .subscribe((calculation) => this.calculation.set(calculation));
  }

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

  protected recalculate(): void {
    this.retryCalculation.next();
  }

  /** Campos de la línea `index` (siempre existe: la plantilla recorre las líneas del modelo). */
  protected lineFields(index: number) {
    return this.invoiceForm.lines[index] as NonNullable<(typeof this.invoiceForm.lines)[number]>;
  }

  /** Valor de la línea que calculó el servidor, si el cálculo está al día. */
  protected lineAmount(index: number): number | string | undefined {
    return this.calculated()?.lines[index]?.line_extension_amount;
  }

  protected lineError(index: number, field: 'code' | 'description' | 'quantity' | 'unitPrice') {
    const state = this.invoiceForm.lines[index]?.[field]();
    if (!state || !state.touched() || state.errors().length === 0) {
      return null;
    }
    const keys = {
      code: 'issue.line.codeRequired',
      description: 'issue.line.descriptionRequired',
      quantity: 'issue.line.quantityInvalid',
      unitPrice: 'issue.line.priceInvalid',
    };
    return this.text(keys[field]);
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

  private calculate(request: CalculateDocumentTotalsRequest | null): Observable<Calculation> {
    if (request === null) {
      return of({ status: 'empty' });
    }
    const key = keyOf(request);
    return this.api.invoke(calculateDocumentTotals, { body: request }, silent()).pipe(
      map((result): Calculation => ({ status: 'ready', key, result })),
      catchError(() => of<Calculation>({ status: 'error', key })),
      startWith<Calculation>({ status: 'loading', key }),
    );
  }

  /** Estado de una búsqueda con *debounce*; menos de dos letras no consulta. */
  private searchState<T>(term: Signal<string>, fetch: (term: string) => Observable<T[]>) {
    const state = signal<SearchState<T>>({ term: '', items: [], loading: false, error: false });
    toObservable(term)
      .pipe(
        debounceTime(SEARCH_DEBOUNCE_MS),
        map((value) => value.trim()),
        distinctUntilChanged(),
        switchMap((value): Observable<SearchState<T>> => {
          if (value.length < SEARCH_MIN_LENGTH) {
            return of({ term: value, items: [], loading: false, error: false });
          }
          return fetch(value).pipe(
            map((items) => ({ term: value, items, loading: false, error: false })),
            catchError(() => of({ term: value, items: [], loading: false, error: true })),
            startWith({ term: value, items: [], loading: true, error: false }),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe((value) => state.set(value));
    return state.asReadonly();
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

function keyOf(request: CalculateDocumentTotalsRequest | null): string {
  return request === null ? '' : JSON.stringify(request);
}
