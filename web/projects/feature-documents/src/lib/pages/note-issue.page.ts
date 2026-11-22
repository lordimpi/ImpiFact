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
import { FormField } from '@angular/forms/signals';
import { Router, RouterLink } from '@angular/router';
import {
  Api,
  DocumentDetailResponse,
  DocumentSummaryResponse,
  getDocument,
  issueCreditNote,
  issueDebitNote,
  issueSupportDocumentAdjustmentNote,
  listDocuments,
  previewCreditNote,
  previewDebitNote,
} from '@impifact/api';
import {
  AppError,
  CatalogsStore,
  HasUnsavedChanges,
  IDEMPOTENCY_KEY,
  SKIP_ERROR_TOAST,
  ToastStore,
  catalogOptions,
} from '@impifact/core';
import {
  ButtonComponent,
  ButtonLinkDirective,
  CardActionsDirective,
  CardComponent,
  CheckboxComponent,
  FieldComponent,
  InputDirective,
  PageTitleComponent,
  SelectComponent,
} from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { firstValueFrom, map } from 'rxjs';
import { CopCurrencyPipe } from '../components/cop-currency.pipe';
import { LineFieldsComponent } from '../components/line-fields.component';
import {
  PickerLabels,
  PickerOption,
  SearchPickerComponent,
} from '../components/search-picker.component';
import { TotalsPanelComponent } from '../components/totals-panel.component';
import { DOCUMENTS_I18N_SCOPE, provideDocumentsI18n } from '../documents-i18n';
import { buildLinesCalculation, emptyLine } from '../forms/invoice.form';
import {
  ADJUSTMENT_CONCEPTS,
  NoteKind,
  NoteModel,
  buildNoteBody,
  createNoteForm,
  emptyNote,
  linesFromDocument,
} from '../forms/note.form';
import { documentKindOf, filesAvailable } from '../models/document-status';
import { debouncedSearch } from '../state/debounced-search';
import { PdfPreviewComponent } from '../components/pdf-preview.component';
import { DocumentActions, previewProblem } from '../state/document-actions';
import { serverTotals } from '../state/server-totals';

/** Estados con la factura aceptada por la DIAN: los únicos que admite una nota. */
const ACCEPTED_STATUSES = ['Accepted', 'AcceptedWithWarnings'];
const SEARCH_LIMIT = 10;
/** Un número con o sin prefijo (`SETP990000001` o `990000001`); lo demás se busca como cliente. */
const NUMBER_PATTERN = /^[A-Za-z]{0,4}\d+$/;
const silent = () => new HttpContext().set(SKIP_ERROR_TOAST, true);

type OriginalState =
  | { status: 'none' }
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'ready'; document: DocumentDetailResponse };

/**
 * Emisión de una nota crédito (`/documentos/nota-credito`) o débito (`/documentos/nota-debito`), que corrige una
 * factura aceptada por la DIAN (Dirección de Impuestos y Aduanas Nacionales). La factura se busca entre las aceptadas
 * de la empresa o llega en el enlace (`?factura=<id>`, desde el detalle). La crédito empieza con las líneas de la
 * factura; la débito, con una línea libre. Los totales siempre vienen de `calculateDocumentTotals` y «Emitir» manda
 * la `Idempotency-Key` creada al abrir el formulario. El Api rechaza una nota cuya factura no esté aceptada
 * (`document.reference_not_accepted`) y la página muestra ese error. «Vista previa» manda el mismo cuerpo a
 * `previewCreditNote` o `previewDebitNote` y abre el PDF sin número ni CUDE (no revisa que la factura esté aceptada).
 *
 * La nota de ajuste (`/documentos/nota-ajuste`, `?soporte=<id>` desde el detalle) corrige un documento soporte
 * aceptado con el mismo formulario: busca entre los documentos soporte, sus conceptos son los cinco fijos de la DIAN,
 * pide el código postal del proveedor (el documento guardado no lo trae) y se emite con
 * `issueSupportDocumentAdjustmentNote` (el correo va al proveedor). El Api no tiene vista previa para ella.
 */
@Component({
  selector: 'feat-note-issue-page',
  imports: [
    CheckboxComponent,
    ButtonLinkDirective,
    FormField,
    RouterLink,
    TranslocoPipe,
    ButtonComponent,
    CardComponent,
    CardActionsDirective,
    FieldComponent,
    InputDirective,
    PageTitleComponent,
    SelectComponent,
    CopCurrencyPipe,
    LineFieldsComponent,
    SearchPickerComponent,
    TotalsPanelComponent,
    PdfPreviewComponent,
  ],
  providers: [provideDocumentsI18n(), DocumentActions],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './note-issue.page.html',
  styleUrls: ['./documents-page.css', './invoice-issue.page.css'],
})
export class NoteIssuePage implements HasUnsavedChanges {
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  private readonly toasts = inject(ToastStore);
  private readonly transloco = inject(TranslocoService);
  private readonly catalogs = inject(CatalogsStore);
  private readonly actions = inject(DocumentActions);

  /** Tipo de nota, de los datos de la ruta. */
  readonly kind = input<NoteKind>('credit');
  /** Factura que se corrige, del enlace (`?factura=<id>`). */
  readonly factura = input<string>();
  /** Documento soporte que se ajusta, del enlace (`?soporte=<id>`). */
  readonly soporte = input<string>();

  /** Una llave por formulario abierto: un reintento del mismo envío devuelve la misma nota. */
  private readonly idempotencyKey = crypto.randomUUID();

  private readonly model = signal<NoteModel>(emptyNote());
  protected readonly noteForm = createNoteForm(this.model, () => this.kind());
  /** La nota de ajuste corrige un documento soporte; las demás, una factura. */
  protected readonly adjustment = computed(() => this.kind() === 'adjustment');
  protected readonly original = signal<OriginalState>({ status: 'none' });
  protected readonly submitting = signal(false);
  protected readonly issueError = signal<{ message: string; details: string[] } | null>(null);
  protected readonly previewing = signal(false);
  /** PDF de la vista previa abierto en el diálogo; `null` si está cerrado. */
  protected readonly previewPdf = signal<Blob | null>(null);
  protected readonly previewError = signal<string | null>(null);
  private issued = false;

  protected readonly originalDocument = computed(() => {
    const state = this.original();
    return state.status === 'ready' ? state.document : null;
  });
  /** Correo del cliente de la factura, para explicar a dónde irá la nota (vacío si no tiene). */
  protected readonly customerEmail = computed(() => {
    const email = this.originalDocument()?.customer?.email;
    return typeof email === 'string' ? email.trim() : '';
  });
  protected readonly notAccepted = computed(() => {
    const document = this.originalDocument();
    return document !== null && !filesAvailable(document.status);
  });

  /** Conceptos de corrección del catálogo de la DIAN para este tipo de nota (la de ajuste no tiene catálogo). */
  private readonly conceptCatalog = computed(() => {
    switch (this.kind()) {
      case 'credit':
        return 'credit-note-discrepancy-responses';
      case 'debit':
        return 'debit-note-discrepancy-responses';
      default:
        return null;
    }
  });
  protected readonly concepts = computed(() => {
    const name = this.conceptCatalog();
    if (name === null) {
      return ADJUSTMENT_CONCEPTS.map((id) => ({
        id,
        name: this.text(`note.adjustmentConcepts.${id}`),
      }));
    }
    return catalogOptions(name, this.catalogs.entries()[name]?.items ?? []);
  });

  // Búsqueda de facturas aceptadas de la empresa en el Api (nunca se descarga la lista completa).
  private readonly invoiceTerm = signal('');
  protected readonly invoiceSearch = debouncedSearch(this.invoiceTerm, (term) =>
    this.api
      .invoke(
        listDocuments,
        {
          kind: this.adjustment() ? 'support_document' : 'invoice',
          status: ACCEPTED_STATUSES,
          limit: SEARCH_LIMIT,
          ...(NUMBER_PATTERN.test(term) ? { number: term } : { customer: term }),
        },
        silent(),
      )
      .pipe(map((page) => page.items ?? [])),
  );
  protected readonly invoiceOptions = computed<PickerOption[]>(() =>
    this.invoiceSearch().items.map((item: DocumentSummaryResponse) => ({
      id: item.id ?? '',
      title: item.full_number ?? '',
      detail: [item.customer?.name, item.issue_date].filter(Boolean).join(' · '),
    })),
  );
  protected readonly invoiceLabels = computed<PickerLabels>(() => {
    const target = this.adjustment() ? 'support' : 'invoice';
    return {
      search: this.text(`note.${target}Search`),
      hint: this.text(`note.${target}SearchHint`),
      placeholder: this.text(`note.${target}SearchPlaceholder`),
      results: this.text(`note.${target}Results`),
      none: this.text(`note.${target}None`),
      searching: this.text('issue.searching'),
      error: this.text('issue.searchError'),
      choose: this.text('issue.customerChoose'),
    };
  });

  protected readonly totals = serverTotals(
    computed(() => buildLinesCalculation(this.noteForm().value().lines)),
  );
  protected readonly calculated = this.totals.calculated;

  protected readonly canIssue = computed(
    () =>
      !this.submitting() &&
      this.originalDocument() !== null &&
      this.calculated() !== null &&
      this.noteForm().valid(),
  );

  constructor() {
    effect(() => {
      const catalog = this.conceptCatalog();
      if (catalog) void this.catalogs.load(catalog);
    });
    effect(() => {
      const id = this.adjustment() ? this.soporte() : this.factura();
      if (id) {
        untracked(() => void this.loadOriginal(id));
      }
    });
  }

  hasUnsavedChanges(): boolean {
    return !this.issued && (this.noteForm().dirty() || this.originalDocument() !== null);
  }

  /** Texto de la nota según su tipo (`note.credit*`, `note.debit*` o `note.adjustment*`). */
  protected noteKey(suffix: 'Title' | 'Description' | 'Submit' | 'Issued'): string {
    return `note.${this.kind()}${suffix}`;
  }

  protected postalCodeError(): string | null {
    const state = this.noteForm.sellerPostalCode();
    if (!state.touched() || state.errors().length === 0) {
      return null;
    }
    return this.text(
      state.errors()[0]?.kind === 'required'
        ? 'supportDocument.errors.required'
        : 'note.sellerPostalCodeInvalid',
    );
  }

  protected searchInvoices(term: string): void {
    this.invoiceTerm.set(term);
  }

  protected chooseInvoice(id: string): void {
    this.invoiceTerm.set('');
    void this.loadOriginal(id);
  }

  protected clearInvoice(): void {
    this.original.set({ status: 'none' });
    this.model.update((current) => ({ ...current, lines: [] }));
  }

  protected copyOriginalLines(): void {
    const document = this.originalDocument();
    if (document) {
      this.noteForm.lines().value.set(linesFromDocument(document));
    }
  }

  protected addFreeLine(): void {
    this.noteForm.lines().value.update((lines) => [...lines, emptyLine()]);
  }

  protected removeLine(index: number): void {
    this.noteForm.lines().value.update((lines) => lines.filter((_, i) => i !== index));
  }

  protected lineFields(index: number) {
    return this.noteForm.lines[index] as NonNullable<(typeof this.noteForm.lines)[number]>;
  }

  protected lineAmount(index: number): number | string | undefined {
    return this.calculated()?.lines[index]?.line_extension_amount;
  }

  protected conceptError(): string | null {
    const state = this.noteForm.discrepancyCode();
    return state.touched() && state.errors().length > 0 ? this.text('note.conceptRequired') : null;
  }

  protected async issue(event: Event): Promise<void> {
    event.preventDefault();
    const original = this.originalDocument();
    const calculated = this.calculated();
    if (!this.canIssue() || !original || !calculated) {
      return;
    }
    this.submitting.set(true);
    this.issueError.set(null);
    try {
      const kind = this.kind();
      const body = buildNoteBody(kind, this.noteForm().value(), original, calculated);
      const context = silent().set(IDEMPOTENCY_KEY, this.idempotencyKey);
      const response = await firstValueFrom(
        kind === 'adjustment'
          ? this.api.invoke(issueSupportDocumentAdjustmentNote, { body }, context)
          : kind === 'credit'
            ? this.api.invoke(issueCreditNote, { body }, context)
            : this.api.invoke(issueDebitNote, { body }, context),
      );
      this.issued = true;
      this.toasts.show({
        kind: 'success',
        text: this.text(this.noteKey('Issued'), {
          number: response.document.full_number ?? '',
        }),
      });
      await this.router.navigate(['/documentos', response.document.id]);
    } catch (error) {
      // Todos los errores de la emisión se muestran junto al botón, también la factura no aceptada.
      this.issueError.set(
        error instanceof AppError
          ? { message: error.message, details: Object.values(error.fieldErrors).flat() }
          : { message: this.text('note.issueFailed'), details: [] },
      );
    } finally {
      this.submitting.set(false);
    }
  }

  protected savePreview(): void {
    const pdf = this.previewPdf();
    if (pdf) this.actions.savePreview(pdf);
  }

  /** Abre el PDF que tendría la nota (sin emitirla); un 503 es el conversor de PDF caído. */
  protected async preview(): Promise<void> {
    const original = this.originalDocument();
    const calculated = this.calculated();
    const kind = this.kind();
    if (
      kind === 'adjustment' ||
      !this.canIssue() ||
      this.previewing() ||
      !original ||
      !calculated
    ) {
      return;
    }
    this.previewing.set(true);
    this.previewError.set(null);
    this.issueError.set(null);
    const body = buildNoteBody(kind, this.noteForm().value(), original, calculated);
    const path = kind === 'credit' ? previewCreditNote.PATH : previewDebitNote.PATH;
    const result = await this.actions.fetchPreview(path, body);
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

  /**
   * Carga el documento que se corrige con sus líneas; la crédito y la de ajuste empiezan con ellas, la débito con
   * una línea libre.
   */
  private async loadOriginal(id: string): Promise<void> {
    const adjustment = this.adjustment();
    this.original.set({ status: 'loading' });
    try {
      const document = await firstValueFrom(this.api.invoke(getDocument, { id }, silent()));
      if (documentKindOf(document) !== (adjustment ? 'support_document' : 'invoice')) {
        this.original.set({
          status: 'error',
          message: this.text(adjustment ? 'note.notASupportDocument' : 'note.notAnInvoice'),
        });
        return;
      }
      this.original.set({ status: 'ready', document });
      this.noteForm
        .lines()
        .value.set(this.kind() === 'debit' ? [emptyLine()] : linesFromDocument(document));
    } catch (error) {
      const fallback = adjustment ? 'note.supportLoadFailed' : 'note.invoiceLoadFailed';
      this.original.set({
        status: 'error',
        message: error instanceof AppError ? error.message : this.text(fallback),
      });
    }
  }

  private text(key: string, params?: Record<string, string>): string {
    return this.transloco.translate(`${DOCUMENTS_I18N_SCOPE}.${key}`, params);
  }
}
