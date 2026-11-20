import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormField, submit } from '@angular/forms/signals';
import { AppError, HasUnsavedChanges, ToastStore } from '@impifact/core';
import {
  BadgeComponent,
  ButtonComponent,
  CardComponent,
  CardFooterDirective,
  ErrorStateComponent,
  FieldComponent,
  InputDirective,
  LoadingComponent,
  PasswordToggleDirective,
  TableCellDirective,
  TableComponent,
  UiBadgeTone,
  UiTableColumn,
} from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { companiesTableLabels, companiesText, provideCompaniesI18n } from '../companies-i18n';
import { fieldMessage, serverFieldErrors } from '../forms/form-messages';
import {
  SOFTWARE_FIELDS,
  SavedSoftware,
  SoftwareModel,
  createSoftwareForm,
  keepsPin,
  softwareCommand,
} from '../forms/software.form';
import { PRODUCTION, kindKey } from '../models/dian';
import { CompanySetupStore } from '../state/company-setup.store';

/**
 * Software (`/empresas/:id/software`): identificador, PIN y set de pruebas de la factura electrónica. Al abrir se lee
 * el software guardado (`getCompanySoftware`) y el ambiente actual; el Api nunca devuelve el PIN, solo si existe
 * (`has_pin`). Con un PIN guardado el campo es opcional: vacío lo conserva. El PIN va en un campo de contraseña y se
 * vacía después de cada envío, haya salido bien o no.
 */
@Component({
  selector: 'feat-software-page',
  imports: [
    TableComponent,
    TableCellDirective,
    FormField,
    TranslocoPipe,
    BadgeComponent,
    ButtonComponent,
    CardComponent,
    CardFooterDirective,
    ErrorStateComponent,
    FieldComponent,
    InputDirective,
    PasswordToggleDirective,
    LoadingComponent,
  ],
  providers: [provideCompaniesI18n()],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="companies-section">
      <form novalidate (submit)="onSubmit($event)" [attr.aria-busy]="submitting() || null">
        <ui-card
          [title]="'companies.software.title' | transloco"
          [subtitle]="'companies.software.description' | transloco"
        >
          <div class="companies-grid">
            @if (generalError(); as message) {
              <p class="companies-message companies-message--error" role="alert">{{ message }}</p>
            }
            <ui-field
              [label]="'companies.software.id' | transloco"
              [hint]="'companies.software.idHint' | transloco"
              [error]="errors().id"
              required
            >
              <input uiInput type="text" autocomplete="off" [formField]="softwareForm.id" />
            </ui-field>
            <ui-field
              [label]="'companies.software.pin' | transloco"
              [hint]="pinHint()"
              [error]="errors().pin"
              [required]="pinRequired()"
            >
              <input
                uiInput
                uiPasswordToggle
                type="password"
                inputmode="numeric"
                autocomplete="new-password"
                [formField]="softwareForm.pin"
              />
            </ui-field>
            <ui-field
              class="companies-wide"
              [label]="'companies.software.testSetId' | transloco"
              [hint]="'companies.software.testSetIdHint' | transloco"
              [error]="errors().test_set_id"
            >
              <input
                uiInput
                type="text"
                autocomplete="off"
                [formField]="softwareForm.test_set_id"
              />
            </ui-field>
          </div>
          <div uiCardFooter class="companies-actions">
            <ui-button
              type="submit"
              [label]="'companies.common.save' | transloco"
              [loading]="submitting()"
            />
          </div>
        </ui-card>
      </form>

      <ui-card
        [headingLevel]="3"
        [title]="'companies.software.registered' | transloco"
        [padding]="software()?.length ? 'none' : 'normal'"
      >
        @if (software(); as rows) {
          @if (rows.length === 0) {
            <p class="companies-lead">{{ 'companies.software.registeredEmpty' | transloco }}</p>
          } @else {
            <ui-table
              flush
              [caption]="'companies.software.registered' | transloco"
              [columns]="softwareColumns()"
              [rows]="rows"
              [labels]="tableLabels()"
            >
              <ng-template uiTableCell="environment" let-row>
                <ui-badge [tone]="row.environmentTone">{{ row.environment }}</ui-badge>
              </ng-template>
            </ui-table>
          }
        } @else if (setup.softwareStatus() === 'error') {
          <ui-error-state
            [title]="'companies.software.loadError' | transloco"
            [retryLabel]="'companies.common.retry' | transloco"
            (retry)="load()"
          />
        } @else {
          <ui-loading [label]="'companies.common.loading' | transloco" />
        }
      </ui-card>
    </section>
  `,
  styleUrls: ['./companies-page.css', './companies-table.css'],
})
export class SoftwarePage implements HasUnsavedChanges {
  protected readonly setup = inject(CompanySetupStore);
  private readonly toasts = inject(ToastStore);
  private readonly transloco = inject(TranslocoService);

  /** Software de la factura guardado en el ambiente actual, sin el PIN. */
  private readonly saved = computed<SavedSoftware | null>(() => {
    const software = this.setup.invoiceSoftware();
    return software
      ? { identifier: software.identifier ?? '', hasPin: software.has_pin === true }
      : null;
  });
  private readonly model = signal<SoftwareModel>(this.savedModel());
  protected readonly softwareForm = createSoftwareForm(this.model, this.saved);
  protected readonly pinRequired = computed(() => !keepsPin(this.saved(), this.model().id));
  protected readonly pinHint = computed(() =>
    this.text(this.pinRequired() ? 'software.pinHint' : 'software.pinKeepHint'),
  );
  protected readonly generalError = signal<string | null>(null);
  protected readonly submitting = computed(() => this.softwareForm().submitting());

  protected readonly tableLabels = computed(() => companiesTableLabels(this.transloco));
  protected readonly softwareColumns = computed<UiTableColumn[]>(() => [
    { key: 'kind', header: this.text('software.columns.kind') },
    { key: 'environment', header: this.text('software.columns.environment') },
    { key: 'identifier', header: this.text('software.columns.identifier') },
    { key: 'testSetId', header: this.text('software.columns.testSet') },
    { key: 'pin', header: this.text('software.columns.pin') },
  ]);

  protected readonly software = computed(() => {
    const rows = this.setup.software();
    return rows
      ? rows.map((row) => ({
          kind: this.text(kindKey(row.document_kind)),
          environment: this.text(
            Number(row.type_environment_id) === PRODUCTION
              ? 'environments.production'
              : 'environments.habilitation',
          ),
          environmentTone: (Number(row.type_environment_id) === PRODUCTION
            ? 'success'
            : 'info') as UiBadgeTone,
          identifier: row.identifier ?? '',
          testSetId: row.test_set_id ?? '',
          pin: this.text(row.has_pin ? 'software.pinConfigured' : 'software.pinMissing'),
        }))
      : null;
  });

  protected readonly errors = computed(() => {
    const translate = (key: string, params?: Record<string, unknown>) => this.text(key, params);
    return {
      id: fieldMessage(this.softwareForm.id(), translate),
      pin: fieldMessage(this.softwareForm.pin(), translate, 'software.pinFormat'),
      test_set_id: fieldMessage(this.softwareForm.test_set_id(), translate),
    };
  });

  constructor() {
    this.load();
  }

  /** Lee el software y el ambiente actual; si llegan antes de que se escriba algo, llenan el formulario. */
  protected load(): void {
    void Promise.all([this.setup.loadSoftware(), this.setup.loadEnvironments()]).then(() =>
      this.refill(),
    );
  }

  private refill(): void {
    if (!this.softwareForm().dirty()) {
      this.model.set(this.savedModel());
    }
  }

  private savedModel(): SoftwareModel {
    const software = this.setup.invoiceSoftware();
    return {
      id: software?.identifier ?? '',
      pin: '',
      test_set_id: software?.test_set_id ?? '',
    };
  }

  hasUnsavedChanges(): boolean {
    return this.softwareForm().dirty();
  }

  protected async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    this.generalError.set(null);
    await submit(this.softwareForm, {
      action: async () => {
        const body = softwareCommand(this.model());
        // El PIN solo viaja en esta solicitud: se borra del formulario antes de esperar la respuesta.
        this.model.update((current) => ({ ...current, pin: '' }));
        try {
          await this.setup.saveSoftware(body);
        } catch (error) {
          return this.showError(error);
        }
        this.softwareForm().reset();
        this.toasts.show({ kind: 'success', text: this.text('common.saved') });
        return undefined;
      },
      onInvalid: () =>
        SOFTWARE_FIELDS.map((name) => this.softwareForm[name])
          .find((field) => field().invalid())?.()
          .focusBoundControl(),
    });
  }

  private showError(error: unknown) {
    if (!(error instanceof AppError)) {
      this.generalError.set(this.text('common.errors.unexpected'));
      return undefined;
    }
    const onFields = serverFieldErrors(this.softwareForm, SOFTWARE_FIELDS, error);
    if (onFields.length === 0) {
      this.generalError.set(error.message);
    }
    return onFields;
  }

  protected text(key: string, params?: Record<string, unknown>): string {
    return companiesText(this.transloco, key, params);
  }
}
