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
  FieldComponent,
  InputDirective,
  PageTitleComponent,
  SelectDirective,
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
  NoteKind,
  NoteModel,
  buildNoteBody,
  createNoteForm,
  emptyNote,
  linesFromDocument,
} from '../forms/note.form';
import { filesAvailable } from '../models/document-status';
import { debouncedSearch } from '../state/debounced-search';
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
 */
@Component({
  selector: 'feat-note-issue-page',
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
    LineFieldsComponent,
    SearchPickerComponent,
    TotalsPanelComponent,
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

  /** Una llave por formulario abierto: un reintento del mismo envío devuelve la misma nota. */
  private readonly idempotencyKey = crypto.randomUUID();

  private readonly model = signal<NoteModel>(emptyNote());
  protected readonly noteForm = createNoteForm(this.model);
  protected readonly original = signal<OriginalState>({ status: 'none' });
  protected readonly submitting = signal(false);
  protected readonly issueError = signal<{ message: string; details: string[] } | null>(null);
  protected readonly previewing = signal(false);
  protected readonly previewError = signal<string | null>(null);
  private issued = false;

  protected readonly originalDocument = computed(() => {
    const state = this.original();
    return state.status === 'ready' ? state.document : null;
  });
  protected readonly notAccepted = computed(() => {
    const document = this.originalDocument();
    return document !== null && !filesAvailable(document.status);
  });

  /** Conceptos de corrección del catálogo de la DIAN para este tipo de nota. */
  private readonly conceptCatalog = computed(() =>
    this.kind() === 'credit'
      ? 'credit-note-discrepancy-responses'
      : 'debit-note-discrepancy-responses',
  );
  protected readonly concepts = computed(() => {
    const name = this.conceptCatalog();
    return catalogOptions(name, this.catalogs.entries()[name]?.items ?? []);
  });

  // Búsqueda de facturas aceptadas de la empresa en el Api (nunca se descarga la lista completa).
  private readonly invoiceTerm = signal('');
  protected readonly invoiceSearch = debouncedSearch(this.invoiceTerm, (term) =>
    this.api
      .invoke(
        listDocuments,
        {
          kind: 'invoice',
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
  protected readonly invoiceLabels: PickerLabels = {
    search: this.text('note.invoiceSearch'),
    hint: this.text('note.invoiceSearchHint'),
    results: this.text('note.invoiceResults'),
    none: this.text('note.invoiceNone'),
    searching: this.text('issue.searching'),
    error: this.text('issue.searchError'),
    choose: this.text('issue.customerChoose'),
  };

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
    effect(() => void this.catalogs.load(this.conceptCatalog()));
    effect(() => {
      const id = this.factura();
      if (id) {
        untracked(() => void this.loadOriginal(id));
      }
    });
  }

  hasUnsavedChanges(): boolean {
    return !this.issued && (this.noteForm().dirty() || this.originalDocument() !== null);
  }

  protected titleKey(): string {
    return this.kind() === 'credit' ? 'note.creditTitle' : 'note.debitTitle';
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
        kind === 'credit'
          ? this.api.invoke(issueCreditNote, { body }, context)
          : this.api.invoke(issueDebitNote, { body }, context),
      );
      this.issued = true;
      this.toasts.show({
        kind: 'success',
        text: this.text(kind === 'credit' ? 'note.creditIssued' : 'note.debitIssued', {
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

  /** Abre el PDF que tendría la nota (sin emitirla); un 503 es el conversor de PDF caído. */
  protected async preview(): Promise<void> {
    const original = this.originalDocument();
    const calculated = this.calculated();
    if (!this.canIssue() || this.previewing() || !original || !calculated) {
      return;
    }
    this.previewing.set(true);
    this.previewError.set(null);
    this.issueError.set(null);
    const kind = this.kind();
    const body = buildNoteBody(kind, this.noteForm().value(), original, calculated);
    const path = kind === 'credit' ? previewCreditNote.PATH : previewDebitNote.PATH;
    const result = await this.actions.openPreview(path, body);
    this.previewing.set(false);
    if (result.ok) {
      return;
    }
    const problem = previewProblem(result.error);
    this.issueError.set(problem.validation);
    this.previewError.set(
      problem.notice &&
        ('key' in problem.notice ? this.text(problem.notice.key) : problem.notice.message),
    );
  }

  /** Carga la factura con sus líneas; la crédito empieza con ellas, la débito con una línea libre. */
  private async loadOriginal(id: string): Promise<void> {
    this.original.set({ status: 'loading' });
    try {
      const document = await firstValueFrom(this.api.invoke(getDocument, { id }, silent()));
      if (document.kind !== 'invoice') {
        this.original.set({ status: 'error', message: this.text('note.notAnInvoice') });
        return;
      }
      this.original.set({ status: 'ready', document });
      this.noteForm
        .lines()
        .value.set(this.kind() === 'credit' ? linesFromDocument(document) : [emptyLine()]);
    } catch (error) {
      this.original.set({
        status: 'error',
        message: error instanceof AppError ? error.message : this.text('note.invoiceLoadFailed'),
      });
    }
  }

  private text(key: string, params?: Record<string, string>): string {
    return this.transloco.translate(`${DOCUMENTS_I18N_SCOPE}.${key}`, params);
  }
}
