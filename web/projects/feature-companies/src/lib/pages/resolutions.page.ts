import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormField, submit } from '@angular/forms/signals';
import { NumberingRangeItem } from '@impifact/api';
import { AppError, HasUnsavedChanges, ToastStore } from '@impifact/core';
import {
  ButtonComponent,
  ErrorStateComponent,
  FieldComponent,
  InputDirective,
  LoadingComponent,
  SelectDirective,
} from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { companiesText, provideCompaniesI18n } from '../companies-i18n';
import { fieldMessage, serverFieldErrors } from '../forms/form-messages';
import {
  RESOLUTION_FIELDS,
  ResolutionModel,
  createResolutionForm,
  emptyResolution,
  habilitationResolution,
  resolutionCommand,
  resolutionFromRange,
} from '../forms/resolution.form';
import { RESOLUTION_DOCUMENT_TYPES } from '../models/dian';
import { CompanySetupStore } from '../state/company-setup.store';
import { dianErrorText } from '../models/dian-errors';

type LookupState =
  | { kind: 'idle' }
  | { kind: 'loading' }
  | { kind: 'ranges'; ranges: NumberingRangeItem[] }
  | { kind: 'message'; text: string; error: boolean };

/**
 * Resoluciones de numeración (`/empresas/:id/resoluciones`): alta o actualización de una resolución, con el ejemplo
 * de habilitación (SETP) y la consulta en vivo de los rangos que la DIAN tiene autorizados (`getNumberingRanges`)
 * para copiar uno al formulario. La tabla muestra las resoluciones guardadas (`listCompanyResolutions`) con el próximo
 * número, lo que queda del rango y los días de vigencia; se vuelve a leer después de cada guardado.
 */
@Component({
  selector: 'feat-resolutions-page',
  imports: [
    FormField,
    TranslocoPipe,
    ButtonComponent,
    ErrorStateComponent,
    FieldComponent,
    InputDirective,
    LoadingComponent,
    SelectDirective,
  ],
  providers: [provideCompaniesI18n()],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './resolutions.page.html',
  styleUrl: './companies-page.css',
})
export class ResolutionsPage implements HasUnsavedChanges {
  protected readonly setup = inject(CompanySetupStore);
  private readonly toasts = inject(ToastStore);
  private readonly transloco = inject(TranslocoService);

  private readonly model = signal<ResolutionModel>(emptyResolution());
  protected readonly resolutionForm = createResolutionForm(this.model);
  protected readonly generalError = signal<string | null>(null);
  protected readonly submitting = computed(() => this.resolutionForm().submitting());
  protected readonly documentTypes = RESOLUTION_DOCUMENT_TYPES;

  protected readonly softwareId = signal(this.setup.invoiceSoftware()?.identifier ?? '');
  protected readonly lookup = signal<LookupState>({ kind: 'idle' });
  protected readonly ranges = computed(() => {
    const state = this.lookup();
    return state.kind === 'ranges' ? state.ranges : [];
  });
  protected readonly lookupMessage = computed(() => {
    const state = this.lookup();
    return state.kind === 'message' ? state : null;
  });

  protected readonly saved = computed(() =>
    this.setup.resolutions().map((resolution) => ({
      id: resolution.id,
      type: this.text(`resolutions.documentTypes.${resolution.type_document_id}`),
      prefix: resolution.prefix ?? '',
      resolution: resolution.resolution ?? '',
      range: `${resolution.from} – ${resolution.to}`,
      validity: `${resolution.date_from ?? ''} – ${resolution.date_to ?? ''}`,
      next: `${resolution.next_number ?? ''}`,
      remaining: `${resolution.remaining ?? ''}`,
      daysLeft:
        resolution.days_left === null || resolution.days_left === undefined
          ? this.text('resolutions.noExpiry')
          : `${resolution.days_left}`,
      status: this.text(resolution.is_active ? 'resolutions.active' : 'resolutions.inactive'),
    })),
  );

  constructor() {
    void this.setup.loadResolutions();
  }

  protected readonly errors = computed(() => {
    const translate = (key: string, params?: Record<string, unknown>) => this.text(key, params);
    const messages = Object.fromEntries(
      RESOLUTION_FIELDS.map((name) => [
        name,
        fieldMessage(this.resolutionForm[name](), translate, 'resolutions.numberFormat'),
      ]),
    ) as Record<keyof ResolutionModel, string | null>;
    const to = this.resolutionForm.to();
    if (to.touched() && to.errors()[0]?.kind === 'rangeOrder') {
      messages.to = this.text('resolutions.rangeOrder');
    }
    return messages;
  });

  hasUnsavedChanges(): boolean {
    return this.resolutionForm().dirty();
  }

  protected useExample(): void {
    this.fill(habilitationResolution());
  }

  protected useRange(range: NumberingRangeItem): void {
    this.fill(resolutionFromRange(this.model(), range));
  }

  private fill(model: ResolutionModel): void {
    this.model.set(model);
    this.resolutionForm().markAsDirty();
  }

  protected async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    this.generalError.set(null);
    await submit(this.resolutionForm, {
      action: async () => {
        try {
          await this.setup.saveResolution(resolutionCommand(this.model()));
        } catch (error) {
          return this.showError(error);
        }
        this.resolutionForm().reset();
        this.toasts.show({ kind: 'success', text: this.text('common.saved') });
        return undefined;
      },
      onInvalid: () =>
        RESOLUTION_FIELDS.map((name) => this.resolutionForm[name])
          .find((field) => field().invalid())?.()
          .focusBoundControl(),
    });
  }

  protected async lookUp(): Promise<void> {
    this.lookup.set({ kind: 'loading' });
    try {
      const response = await this.setup.numberingRanges(this.softwareId());
      const ranges = response.numbering_ranges ?? [];
      if (ranges.length > 0) {
        this.lookup.set({ kind: 'ranges', ranges });
        return;
      }
      const answer = response.operation_description ?? response.message;
      this.lookup.set({
        kind: 'message',
        error: response.success === false,
        text: answer
          ? this.text('resolutions.ranges.dianMessage', { message: answer })
          : this.text('resolutions.ranges.empty'),
      });
    } catch (error) {
      this.lookup.set({ kind: 'message', error: true, text: dianErrorText(error, this.text) });
    }
  }

  private showError(error: unknown) {
    if (!(error instanceof AppError)) {
      this.generalError.set(this.text('common.errors.unexpected'));
      return undefined;
    }
    const onFields = serverFieldErrors(this.resolutionForm, RESOLUTION_FIELDS, error);
    if (onFields.length === 0) {
      this.generalError.set(error.message);
    }
    return onFields;
  }

  protected readonly text = (key: string, params?: Record<string, unknown>): string =>
    companiesText(this.transloco, key, params);
}
