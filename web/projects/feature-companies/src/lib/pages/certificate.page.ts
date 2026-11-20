import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { AppError } from '@impifact/core';
import {
  BadgeComponent,
  ButtonComponent,
  CardComponent,
  CardFooterDirective,
  ErrorStateComponent,
  UiBadgeTone,
  FieldComponent,
  InputDirective,
  PasswordToggleDirective,
  LoadingComponent,
} from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { companiesText, provideCompaniesI18n } from '../companies-i18n';
import { CompanySetupStore } from '../state/company-setup.store';

/** Tamaño máximo del archivo; un .p12 normal pesa unos pocos kilobytes. */
const MAX_BYTES = 1024 * 1024;
const EXTENSIONS = /\.(p12|pfx)$/i;

/** Contenido del archivo en base64, sin el prefijo `data:` que agrega `FileReader`. */
function readAsBase64(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result).replace(/^data:[^,]*,/, ''));
    reader.onerror = () => reject(reader.error);
    reader.readAsDataURL(file);
  });
}

/**
 * Certificado digital (`/empresas/:id/certificado`): los certificados de la empresa (titular, emisor, vencimiento y
 * días restantes) y la carga de uno nuevo en base64, que es lo que recibe el Api.
 *
 * La contraseña se lee del campo `type=password` solo al enviar y nunca pasa por un signal, el store ni la consola;
 * el campo y el archivo se vacían después de cada envío, haya salido bien o no.
 */
@Component({
  selector: 'feat-certificate-page',
  imports: [
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
      <ui-card
        [title]="'companies.certificate.title' | transloco"
        [subtitle]="'companies.certificate.description' | transloco"
        [padding]="
          setup.certificatesStatus() === 'ready' && certificates().length ? 'none' : 'normal'
        "
      >
        @switch (setup.certificatesStatus()) {
          @case ('error') {
            <ui-error-state
              [title]="'companies.certificate.loadError' | transloco"
              [retryLabel]="'companies.common.retry' | transloco"
              (retry)="setup.loadCertificates()"
            />
          }
          @case ('ready') {
            @if (certificates().length === 0) {
              <p class="companies-lead">{{ 'companies.certificate.empty' | transloco }}</p>
            } @else {
              <ul
                class="companies-list"
                [attr.aria-label]="'companies.certificate.current' | transloco"
              >
                @for (certificate of certificates(); track certificate.id) {
                  <li>
                    <div>
                      <p>
                        <strong>{{ 'companies.certificate.subject' | transloco }}:</strong>&ngsp;
                        <span>{{ certificate.subject }}</span>
                      </p>
                      <p class="companies-lead">
                        {{ 'companies.certificate.issuer' | transloco }}: {{ certificate.issuer }}
                      </p>
                      <p class="companies-lead">
                        {{ 'companies.certificate.expiration' | transloco }}:
                        {{ certificate.expiration }}
                      </p>
                    </div>
                    <div class="companies-badges">
                      <ui-badge [tone]="certificate.active ? 'success' : 'neutral'">{{
                        (certificate.active
                          ? 'companies.certificate.active'
                          : 'companies.certificate.inactive'
                        ) | transloco
                      }}</ui-badge>
                      <ui-badge [tone]="certificate.validityTone">
                        @if (certificate.expired) {
                          {{ 'companies.certificate.expired' | transloco }}
                        } @else {
                          {{
                            'companies.certificate.daysLeft'
                              | transloco: { days: certificate.daysLeft }
                          }}
                        }
                      </ui-badge>
                    </div>
                  </li>
                }
              </ul>
            }
          }
          @default {
            <ui-loading [label]="'companies.common.loading' | transloco" />
          }
        }
      </ui-card>

      <form novalidate (submit)="onSubmit($event)" [attr.aria-busy]="uploading() || null">
        <ui-card
          [headingLevel]="3"
          [title]="'companies.certificate.uploadTitle' | transloco"
          [subtitle]="'companies.certificate.uploadHint' | transloco"
        >
          <div class="companies-grid">
            @if (result(); as message) {
              <p class="companies-message companies-message--success" role="status">
                {{ message }}
              </p>
            }
            @if (generalError(); as message) {
              <p class="companies-message companies-message--error" role="alert">{{ message }}</p>
            }
            <ui-field
              [label]="'companies.certificate.file' | transloco"
              [hint]="'companies.certificate.fileHint' | transloco"
              [error]="fileError()"
              required
            >
              <input #file uiInput type="file" accept=".p12,.pfx" (change)="fileError.set(null)" />
            </ui-field>
            <ui-field
              [label]="'companies.certificate.password' | transloco"
              [hint]="'companies.certificate.passwordHint' | transloco"
              [error]="passwordError()"
              required
            >
              <input
                #password
                uiInput
                uiPasswordToggle
                type="password"
                autocomplete="new-password"
                (input)="passwordError.set(null)"
              />
            </ui-field>
          </div>
          <div uiCardFooter class="companies-actions">
            <ui-button
              type="submit"
              [label]="'companies.certificate.upload' | transloco"
              [loading]="uploading()"
            />
          </div>
        </ui-card>
      </form>
    </section>
  `,
  styleUrls: ['./companies-page.css', './companies-list.css'],
})
export class CertificatePage {
  protected readonly setup = inject(CompanySetupStore);
  private readonly transloco = inject(TranslocoService);

  private readonly fileInput = viewChild.required<ElementRef<HTMLInputElement>>('file');
  private readonly passwordInput = viewChild.required<ElementRef<HTMLInputElement>>('password');

  protected readonly uploading = signal(false);
  protected readonly fileError = signal<string | null>(null);
  protected readonly passwordError = signal<string | null>(null);
  protected readonly generalError = signal<string | null>(null);
  protected readonly result = signal<string | null>(null);

  protected readonly certificates = computed(() =>
    this.setup.certificates().map((certificate) => {
      const daysLeft = Number(certificate.certificate_days_left ?? 0);
      return {
        id: certificate.id,
        subject: certificate.subject ?? certificate.name ?? '',
        issuer: certificate.issuer ?? '',
        expiration: certificate.expiration_date ?? '',
        daysLeft,
        expired: daysLeft <= 0,
        active: !!certificate.is_active,
        validityTone: validityTone(daysLeft),
      };
    }),
  );

  constructor() {
    void this.setup.loadCertificates();
  }

  protected async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    this.generalError.set(null);
    this.result.set(null);
    const file = this.fileInput().nativeElement.files?.[0] ?? null;
    const password = this.passwordInput().nativeElement.value;
    this.fileError.set(this.validateFile(file));
    this.passwordError.set(password ? null : this.text('certificate.errors.passwordRequired'));
    if (!file || this.fileError() || this.passwordError()) {
      return;
    }

    this.uploading.set(true);
    try {
      const response = await this.setup.uploadCertificate(await readAsBase64(file), password);
      this.result.set(
        this.text('certificate.uploaded', {
          days: response.certificate_days_left ?? response.certificate.certificate_days_left ?? 0,
        }),
      );
    } catch (error) {
      this.showError(error);
    } finally {
      this.uploading.set(false);
      this.clearInputs();
    }
  }

  private validateFile(file: File | null): string | null {
    if (!file) {
      return this.text('certificate.errors.fileRequired');
    }
    if (!EXTENSIONS.test(file.name)) {
      return this.text('certificate.errors.fileType');
    }
    return file.size > MAX_BYTES ? this.text('certificate.errors.fileSize') : null;
  }

  private showError(error: unknown): void {
    if (!(error instanceof AppError)) {
      this.generalError.set(this.text('common.errors.unexpected'));
      return;
    }
    const certificate = error.fieldErrors['certificate']?.[0];
    const password = error.fieldErrors['password']?.[0];
    this.fileError.set(certificate ?? null);
    this.passwordError.set(password ?? null);
    if (!certificate && !password) {
      this.generalError.set(error.message);
    }
  }

  private clearInputs(): void {
    this.passwordInput().nativeElement.value = '';
    this.fileInput().nativeElement.value = '';
  }

  protected text(key: string, params?: Record<string, unknown>): string {
    return companiesText(this.transloco, key, params);
  }
}

/** Days of validity at or below which a certificate is about to expire and should be renewed. */
const EXPIRING_DAYS = 30;

/** Badge tone of the validity: expired (danger), about to expire (warning) or valid (success). */
function validityTone(daysLeft: number): UiBadgeTone {
  if (daysLeft <= 0) {
    return 'danger';
  }
  return daysLeft <= EXPIRING_DAYS ? 'warning' : 'success';
}
