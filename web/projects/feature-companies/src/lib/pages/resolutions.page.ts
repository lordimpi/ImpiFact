import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormField, submit } from '@angular/forms/signals';
import { NumberingRangeItem } from '@impifact/api';
import { AppError, HasUnsavedChanges, ToastStore } from '@impifact/core';
import {
  BadgeComponent,
  ButtonComponent,
  CardActionsDirective,
  CardComponent,
  CardFooterDirective,
  ErrorStateComponent,
  UiBadgeTone,
  FieldComponent,
  InputDirective,
  LoadingComponent,
  SelectDirective,
} from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { companiesText, provideCompaniesI18n } from '../companies-i18n';
import {
  NextNumberDialogComponent,
  NextNumberLabels,
} from '../components/next-number-dialog.component';
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

/** Resolución a la que se le cambia el próximo número. */
interface NextNumberTarget {
  id: string;
  next: string;
  from: number;
  to: number;
}

const DIGITS = /^\d+$/;

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
    BadgeComponent,
    ButtonComponent,
    CardActionsDirective,
    CardComponent,
    CardFooterDirective,
    ErrorStateComponent,
    FieldComponent,
    InputDirective,
    LoadingComponent,
    SelectDirective,
    NextNumberDialogComponent,
  ],
  providers: [provideCompaniesI18n()],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './resolutions.page.html',
  styleUrls: ['./companies-page.css', './companies-table.css'],
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
      range: [`${resolution.from}`, `${resolution.to}`],
      validity: [resolution.date_from ?? '', resolution.date_to ?? ''],
      next: `${resolution.next_number ?? ''}`,
      remaining: `${resolution.remaining ?? ''}`,
      daysLeft:
        resolution.days_left === null || resolution.days_left === undefined
          ? this.text('resolutions.noExpiry')
          : `${resolution.days_left}`,
      status: this.text(resolution.is_active ? 'resolutions.active' : 'resolutions.inactive'),
      statusTone: statusTone(resolution.is_active, resolution.days_left),
      target: {
        id: resolution.id ?? '',
        next: `${resolution.next_number ?? ''}`,
        from: Number(resolution.from),
        to: Number(resolution.to),
      } satisfies NextNumberTarget,
    })),
  );

  protected readonly nextNumberTarget = signal<NextNumberTarget | null>(null);
  protected readonly nextNumberOpen = signal(false);
  protected readonly nextNumberError = signal<string | null>(null);
  protected readonly nextNumberBusy = signal(false);
  protected readonly nextNumberLabels = computed<NextNumberLabels>(() => {
    const target = this.nextNumberTarget();
    return {
      header: this.text('resolutions.nextNumber.header'),
      help: this.text('resolutions.nextNumber.help'),
      field: this.text('resolutions.nextNumber.field'),
      range: target
        ? this.text('resolutions.nextNumber.range', { from: target.from, to: target.to })
        : '',
      save: this.text('resolutions.nextNumber.save'),
      cancel: this.text('common.cancel'),
      close: this.text('common.close'),
    };
  });

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

  protected openNextNumber(target: NextNumberTarget): void {
    this.nextNumberTarget.set(target);
    this.nextNumberError.set(null);
    this.nextNumberOpen.set(true);
  }

  protected async saveNextNumber(value: string): Promise<void> {
    const target = this.nextNumberTarget();
    const text = value.trim();
    if (!target || this.nextNumberBusy()) {
      return;
    }
    if (text === '' || !DIGITS.test(text)) {
      this.nextNumberError.set(
        this.text(text === '' ? 'common.errors.required' : 'resolutions.numberFormat'),
      );
      return;
    }
    this.nextNumberError.set(null);
    this.nextNumberBusy.set(true);
    try {
      await this.setup.setNextNumber(target.id, Number(text));
      this.nextNumberOpen.set(false);
      this.toasts.show({ kind: 'success', text: this.text('resolutions.nextNumber.saved') });
    } catch (error) {
      this.nextNumberError.set(
        error instanceof AppError
          ? (error.fieldErrors['next_number']?.[0] ?? error.message)
          : this.text('common.errors.unexpected'),
      );
    } finally {
      this.nextNumberBusy.set(false);
    }
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
      // Una respuesta de la DIAN sin rangos es normal (en habilitación el rango SETP es genérico y la DIAN responde
      // «No fue encontrado ningún rango…»): se muestra en neutro. En rojo quedan solo las fallas (red, 5xx, certificado).
      const answer = response.operation_description ?? response.message;
      this.lookup.set({
        kind: 'message',
        error: false,
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

/** Days of validity at or below which an active resolution is about to expire and should be renewed. */
const EXPIRING_DAYS = 30;

/** Badge tone of a saved resolution: inactive (neutral), about to expire (warning) or active (success). */
function statusTone(
  active: boolean | null | undefined,
  daysLeft: string | number | null | undefined,
): UiBadgeTone {
  if (!active) {
    return 'neutral';
  }
  const days =
    daysLeft === null || daysLeft === undefined || daysLeft === '' ? NaN : Number(daysLeft);
  return days <= EXPIRING_DAYS ? 'warning' : 'success';
}
