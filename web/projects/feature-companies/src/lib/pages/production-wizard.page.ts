import { HttpContext } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { Api, calculateDocumentTotals, getDocument, issueTestSetInvoice } from '@impifact/api';
import { IDEMPOTENCY_KEY, SKIP_ERROR_TOAST, ToastStore } from '@impifact/core';
import { ButtonComponent, FieldComponent, InputDirective } from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { companiesText, provideCompaniesI18n } from '../companies-i18n';
import {
  ProductionConfirmDialogComponent,
  ProductionConfirmLabels,
} from '../components/production-confirm-dialog.component';
import {
  ENVIRONMENT_KINDS,
  EnvironmentField,
  INVOICE_KIND,
  PRODUCTION,
  isAcceptedStatus,
  isPendingStatus,
  kindKey,
  statusKey,
} from '../models/dian';
import { dianErrorText } from '../models/dian-errors';
import { TEST_INVOICE_CALCULATION, buildTestInvoiceBody } from '../models/test-invoice';
import { CompanySetupStore } from '../state/company-setup.store';
import { pageVisibility, pollWhilePending } from '../state/polling';

export type StepStatus = 'done' | 'missing' | 'unverified' | 'locked' | 'inProgress';

interface Step {
  id: 'company' | 'certificate' | 'resolution' | 'software' | 'testSet' | 'production';
  title: string;
  status: StepStatus;
  detail: string;
  /** Sección de la empresa donde se completa el paso. */
  section?: { path: string; label: string };
}

const silent = () => new HttpContext().set(SKIP_ERROR_TOAST, true);

/**
 * Asistente de paso a producción (`/empresas/:id/produccion`, sección 8.2 de la arquitectura del frontend): empresa,
 * certificado, resolución, software y set de pruebas, envío al set de pruebas y producción por familia de documentos.
 *
 * El estado de cada paso sale del Api: el certificado se consulta al abrir; los datos, la resolución y el software
 * solo se conocen si se guardaron en esta sesión (el Api no tiene lectura de ellos), y entonces el paso queda
 * «Sin verificar» y no bloquea. Sin un certificado activo y vigente, el set de pruebas y la producción quedan
 * bloqueados. Pasar a producción pide una confirmación explícita, porque los documentos tienen efecto fiscal real.
 */
@Component({
  selector: 'feat-production-wizard-page',
  imports: [
    RouterLink,
    TranslocoPipe,
    ButtonComponent,
    FieldComponent,
    InputDirective,
    ProductionConfirmDialogComponent,
  ],
  providers: [provideCompaniesI18n()],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './production-wizard.page.html',
  styleUrl: './companies-page.css',
})
export class ProductionWizardPage {
  protected readonly setup = inject(CompanySetupStore);
  private readonly api = inject(Api);
  private readonly toasts = inject(ToastStore);
  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly visible = pageVisibility();

  protected readonly sending = signal(false);
  protected readonly testError = signal<string | null>(null);
  /** Familia que se está por pasar a producción (diálogo abierto). */
  protected readonly switchingKind = signal<number | null>(null);
  protected readonly switching = signal(false);
  protected readonly switchError = signal<string | null>(null);
  protected readonly confirming = signal(false);

  private readonly certificateStep = computed<Pick<Step, 'status' | 'detail'>>(() => {
    const status = this.setup.certificatesStatus();
    if (status === 'idle' || status === 'loading') {
      return { status: 'inProgress', detail: this.text('common.loading') };
    }
    if (status === 'error') {
      return { status: 'missing', detail: this.text('wizard.steps.certificate.error') };
    }
    const active = this.setup.activeCertificate();
    const days = Number(active?.certificate_days_left ?? 0);
    return active && days > 0
      ? {
          status: 'done',
          detail: this.text('wizard.steps.certificate.done', {
            subject: active.subject ?? active.name ?? '',
            days,
          }),
        }
      : { status: 'missing', detail: this.text('wizard.steps.certificate.missing') };
  });

  /** Los pasos que mandan documentos a la DIAN necesitan un certificado vigente. */
  protected readonly unlocked = computed(() => this.certificateStep().status === 'done');

  protected readonly testDocument = computed(() => {
    const document = this.setup.testDocument();
    if (!document) {
      return null;
    }
    const code = document.dian_status_code ?? '';
    const description = document.dian_status_description ?? '';
    return {
      result: this.text('wizard.steps.testSet.result', {
        number: document.full_number ?? '',
        status: this.text(statusKey(document.status)),
      }),
      dian:
        code || description
          ? this.text('wizard.steps.testSet.dian', { code, description }).trim()
          : null,
      accepted: isAcceptedStatus(document.status),
      pending: isPendingStatus(document.status),
    };
  });

  protected readonly canSendTest = computed(
    () => this.unlocked() && !!this.setup.effectiveTestSetId() && !this.sending(),
  );

  protected readonly productionKinds = computed(() =>
    ENVIRONMENT_KINDS.map(({ kind, field }) => ({
      kind,
      field,
      label: this.text(kindKey(kind)),
      inProduction: this.setup.environmentOf(kind) === PRODUCTION,
    })),
  );

  protected readonly steps = computed<Step[]>(() => {
    const locked = !this.unlocked();
    const saved = this.setup.saved();
    const resolution = this.setup
      .resolutions()
      .find((entry) => Number(entry.type_document_id) === 1);
    const software = this.setup.invoiceSoftware();
    const testSetId = this.setup.effectiveTestSetId();
    const test = this.testDocument();
    const section = (path: string, label: string) => ({
      path: `../${path}`,
      label: this.text('wizard.open', { section: this.text(`layout.tabs.${label}`) }),
    });
    return [
      {
        id: 'company',
        title: this.text('wizard.steps.company.title'),
        status: saved ? 'done' : 'unverified',
        detail: this.text(saved ? 'wizard.steps.company.done' : 'wizard.steps.company.unverified'),
        section: section('datos', 'data'),
      },
      {
        id: 'certificate',
        title: this.text('wizard.steps.certificate.title'),
        ...this.certificateStep(),
        section: section('certificado', 'certificate'),
      },
      {
        id: 'resolution',
        title: this.text('wizard.steps.resolution.title'),
        status: resolution ? 'done' : 'unverified',
        detail: resolution
          ? this.text('wizard.steps.resolution.done', {
              resolution: resolution.resolution ?? '',
              prefix: resolution.prefix ?? '',
            })
          : this.text('wizard.steps.resolution.unverified'),
        section: section('resoluciones', 'resolutions'),
      },
      {
        id: 'software',
        title: this.text('wizard.steps.software.title'),
        status: testSetId ? 'done' : 'unverified',
        detail: testSetId
          ? this.text('wizard.steps.software.done', {
              identifier: software?.identifier ?? '',
              testSetId,
            })
          : this.text('wizard.steps.software.unverified'),
        section: section('software', 'software'),
      },
      {
        id: 'testSet',
        title: this.text('wizard.steps.testSet.title'),
        status: locked
          ? 'locked'
          : test?.accepted
            ? 'done'
            : this.sending() || test?.pending
              ? 'inProgress'
              : 'missing',
        detail: this.text(locked ? 'wizard.lockedHint' : 'wizard.steps.testSet.description'),
      },
      {
        id: 'production',
        title: this.text('wizard.steps.production.title'),
        status: locked
          ? 'locked'
          : this.setup.environmentOf(INVOICE_KIND) === PRODUCTION
            ? 'done'
            : 'missing',
        detail: this.text(locked ? 'wizard.lockedHint' : 'wizard.steps.production.description'),
      },
    ];
  });

  protected readonly confirmLabels = computed<ProductionConfirmLabels>(() => {
    const kind = this.switchingKind();
    return {
      header: this.text('environments.confirm.header'),
      body: this.text('environments.confirm.body', {
        kinds: kind === null ? '' : this.text(kindKey(kind)),
      }),
      accept: this.text('environments.confirm.accept'),
      confirm: this.text('environments.confirm.confirm'),
      cancel: this.text('common.cancel'),
      close: this.text('common.close'),
    };
  });

  constructor() {
    void this.setup.loadCertificates();
  }

  protected refresh(): void {
    void this.setup.loadCertificates();
  }

  protected setTestSetId(value: string): void {
    this.setup.setTestSetId(value);
  }

  /**
   * Envía la factura de ejemplo al set de pruebas: pide los totales al Api, la emite por la ruta del set de pruebas
   * con una `Idempotency-Key` nueva y sigue su estado en la DIAN hasta una respuesta final.
   */
  protected async sendTestInvoice(): Promise<void> {
    const testSetId = this.setup.effectiveTestSetId();
    if (!testSetId) {
      this.testError.set(this.text('wizard.steps.testSet.needsTestSet'));
      return;
    }
    this.testError.set(null);
    this.sending.set(true);
    let documentId: string;
    try {
      const calculation = await firstValueFrom(
        this.api.invoke(calculateDocumentTotals, { body: TEST_INVOICE_CALCULATION }, silent()),
      );
      const issued = await firstValueFrom(
        this.api.invoke(
          issueTestSetInvoice,
          { testSetId, body: buildTestInvoiceBody(calculation) },
          silent().set(IDEMPOTENCY_KEY, crypto.randomUUID()),
        ),
      );
      documentId = issued.document.id ?? '';
      this.setup.setTestDocument({
        id: documentId,
        full_number: issued.document.full_number,
        status: issued.document.status,
      });
    } catch (error) {
      this.testError.set(dianErrorText(error, this.text));
      this.sending.set(false);
      return;
    }

    pollWhilePending(
      () => this.api.invoke(getDocument, { id: documentId }, silent()),
      (document) => isPendingStatus(document.status),
      this.visible,
    )
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (document) => this.setup.setTestDocument(document),
        error: (error: unknown) => {
          this.testError.set(dianErrorText(error, this.text));
          this.sending.set(false);
        },
        complete: () => this.sending.set(false),
      });
  }

  protected askProduction(kind: number): void {
    this.switchError.set(null);
    this.switchingKind.set(kind);
    this.confirming.set(true);
  }

  protected async switchToProduction(): Promise<void> {
    const kind = this.switchingKind();
    const entry = ENVIRONMENT_KINDS.find((candidate) => candidate.kind === kind);
    if (!entry) {
      return;
    }
    this.switching.set(true);
    try {
      const field: EnvironmentField = entry.field;
      await this.setup.setEnvironments({ [field]: PRODUCTION });
      this.toasts.show({
        kind: 'success',
        text: this.text('wizard.steps.production.switched', {
          kind: this.text(kindKey(entry.kind)),
        }),
      });
    } catch (error) {
      this.switchError.set(dianErrorText(error, this.text));
    } finally {
      this.switching.set(false);
      this.confirming.set(false);
    }
  }

  protected readonly text = (key: string, params?: Record<string, unknown>): string =>
    companiesText(this.transloco, key, params);
}
