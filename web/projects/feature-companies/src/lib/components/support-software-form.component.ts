import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormField, submit } from '@angular/forms/signals';
import { AppError, ToastStore } from '@impifact/core';
import {
  ButtonComponent,
  CardComponent,
  CardFooterDirective,
  FieldComponent,
  InputDirective,
  PasswordToggleDirective,
} from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { companiesText } from '../companies-i18n';
import { fieldMessage, serverFieldErrors } from '../forms/form-messages';
import {
  SUPPORT_SOFTWARE_FIELDS,
  SavedSoftware,
  SupportSoftwareModel,
  createSupportSoftwareForm,
  keepsPin,
  supportSoftwareCommand,
} from '../forms/software.form';
import { CompanySetupStore } from '../state/company-setup.store';

/**
 * Software del documento soporte en la pestaña «Software»: identificador y PIN del software que la DIAN asignó al
 * documento soporte (otro registro, distinto del de la factura). Se guarda con `setCompanySoftware`
 * (`idsupportdocument` y `pinsupportdocument`) y funciona igual que el de la factura: el PIN va en un campo de
 * contraseña, nunca se lee del Api y se vacía después de cada envío; con un PIN guardado, dejarlo vacío lo conserva.
 * El Api no recibe un set de pruebas propio para el documento soporte, así que aquí no se pide.
 */
@Component({
  selector: 'feat-support-software-form',
  imports: [
    FormField,
    TranslocoPipe,
    ButtonComponent,
    CardComponent,
    CardFooterDirective,
    FieldComponent,
    InputDirective,
    PasswordToggleDirective,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form novalidate (submit)="onSubmit($event)" [attr.aria-busy]="submitting() || null">
      <ui-card
        [headingLevel]="3"
        [title]="'companies.software.support.title' | transloco"
        [subtitle]="'companies.software.support.description' | transloco"
      >
        <div class="companies-grid">
          @if (generalError(); as message) {
            <p class="companies-message companies-message--error" role="alert">{{ message }}</p>
          }
          <ui-field
            [label]="'companies.software.support.id' | transloco"
            [hint]="'companies.software.support.idHint' | transloco"
            [error]="errors().id"
            required
          >
            <input
              uiInput
              type="text"
              autocomplete="off"
              [placeholder]="'companies.software.placeholders.id' | transloco"
              [formField]="supportForm.idsupportdocument"
            />
          </ui-field>
          <ui-field
            [label]="'companies.software.support.pin' | transloco"
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
              [placeholder]="'companies.software.placeholders.pin' | transloco"
              [formField]="supportForm.pinsupportdocument"
            />
          </ui-field>
        </div>
        <div uiCardFooter class="companies-actions">
          <ui-button
            type="submit"
            [label]="'companies.software.support.save' | transloco"
            [loading]="submitting()"
          />
        </div>
      </ui-card>
    </form>
  `,
  styleUrls: ['../pages/companies-page.css'],
})
export class SupportSoftwareFormComponent {
  private readonly setup = inject(CompanySetupStore);
  private readonly toasts = inject(ToastStore);
  private readonly transloco = inject(TranslocoService);

  /** Software del documento soporte guardado en su ambiente actual, sin el PIN. */
  private readonly saved = computed<SavedSoftware | null>(() => {
    const software = this.setup.supportSoftware();
    return software
      ? { identifier: software.identifier ?? '', hasPin: software.has_pin === true }
      : null;
  });
  private readonly model = signal<SupportSoftwareModel>(this.savedModel());
  protected readonly supportForm = createSupportSoftwareForm(this.model, this.saved);
  protected readonly pinRequired = computed(
    () => !keepsPin(this.saved(), this.model().idsupportdocument),
  );
  protected readonly pinHint = computed(() =>
    this.text(this.pinRequired() ? 'software.pinHint' : 'software.pinKeepHint'),
  );
  protected readonly generalError = signal<string | null>(null);
  protected readonly submitting = computed(() => this.supportForm().submitting());

  protected readonly errors = computed(() => {
    const translate = (key: string, params?: Record<string, unknown>) => this.text(key, params);
    return {
      id: fieldMessage(this.supportForm.idsupportdocument(), translate),
      pin: fieldMessage(this.supportForm.pinsupportdocument(), translate, 'software.pinFormat'),
    };
  });

  /** Lo llama la página cuando terminan de llegar el software y los ambientes. */
  refill(): void {
    if (!this.supportForm().dirty()) {
      this.model.set(this.savedModel());
    }
  }

  dirty(): boolean {
    return this.supportForm().dirty();
  }

  private savedModel(): SupportSoftwareModel {
    return {
      idsupportdocument: this.setup.supportSoftware()?.identifier ?? '',
      pinsupportdocument: '',
    };
  }

  protected async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    this.generalError.set(null);
    await submit(this.supportForm, {
      action: async () => {
        const body = supportSoftwareCommand(this.model());
        // El PIN solo viaja en esta solicitud: se borra del formulario antes de esperar la respuesta.
        this.model.update((current) => ({ ...current, pinsupportdocument: '' }));
        if (body) {
          try {
            await this.setup.saveSoftware(body);
          } catch (error) {
            return this.showError(error);
          }
        }
        // Without a new PIN there is nothing to send: the saved software stays as it is.
        this.supportForm().reset();
        this.toasts.show({ kind: 'success', text: this.text('common.saved') });
        return undefined;
      },
      onInvalid: () =>
        SUPPORT_SOFTWARE_FIELDS.map((name) => this.supportForm[name])
          .find((field) => field().invalid())?.()
          .focusBoundControl(),
    });
  }

  private showError(error: unknown) {
    if (!(error instanceof AppError)) {
      this.generalError.set(this.text('common.errors.unexpected'));
      return undefined;
    }
    const onFields = serverFieldErrors(this.supportForm, SUPPORT_SOFTWARE_FIELDS, error);
    if (onFields.length === 0) {
      this.generalError.set(error.message);
    }
    return onFields;
  }

  private text(key: string, params?: Record<string, unknown>): string {
    return companiesText(this.transloco, key, params);
  }
}
