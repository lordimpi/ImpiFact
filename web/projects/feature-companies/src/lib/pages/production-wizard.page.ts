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
import {
  BadgeComponent,
  ButtonComponent,
  CardActionsDirective,
  CardComponent,
  ErrorStateComponent,
  FieldComponent,
  InputDirective,
  LoadingComponent,
  UiBadgeTone,
} from '@impifact/ui';
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
  SUPPORT_DOCUMENT_TYPE,
  isAcceptedStatus,
  isPendingStatus,
  kindKey,
  statusKey,
} from '../models/dian';
import { dianErrorText } from '../models/dian-errors';
import { TEST_INVOICE_CALCULATION, buildTestInvoiceBody } from '../models/test-invoice';
import { CompanySetupStore } from '../state/company-setup.store';
import { pageVisibility, pollWhilePending } from '../state/polling';

export type StepStatus = 'done' | 'missing' | 'locked' | 'inProgress';

interface Step {
  id:
    | 'company'
    | 'certificate'
    | 'resolution'
    | 'software'
    | 'testSet'
    | 'supportDocument'
    | 'production';
  title: string;
  status: StepStatus;
  detail: string;
  /** Resultado que informa el Api para el paso (por ejemplo, la respuesta de la DIAN al set de pruebas). */
  note?: string;
  /** Sección de la empresa donde se completa el paso. */
  section?: { path: string; label: string };
}

/** Badge tone of each step status: done (success), pending (warning), running (info) and locked (neutral). */
const STATUS_TONES: Record<StepStatus, UiBadgeTone> = {
  done: 'success',
  missing: 'warning',
  inProgress: 'info',
  locked: 'neutral',
};

const silent = () => new HttpContext().set(SKIP_ERROR_TOAST, true);

/**
 * Asistente de paso a producción (`/empresas/:id/produccion`, sección 8.2 de la arquitectura del frontend): empresa,
 * certificado, resolución, software y set de pruebas, envío al set de pruebas, la familia del documento soporte
 * (opcional: su software y su resolución, del bloque `support_document`) y producción por familia de documentos.
 *
 * El estado de cada paso sale de `getCompanySetupStatus`, que el Api calcula con lo guardado; el certificado y el
 * software se leen además para mostrar sus datos. El estado se vuelve a pedir después de cada guardado, al terminar
 * una factura de prueba y con «Volver a revisar». Sin un certificado activo y vigente, el set de pruebas y la
 * producción quedan bloqueados. Pasar a producción pide una confirmación explícita, porque los documentos tienen
 * efecto fiscal real.
 */
@Component({
  selector: 'feat-production-wizard-page',
  imports: [
    RouterLink,
    TranslocoPipe,
    BadgeComponent,
    ButtonComponent,
    CardActionsDirective,
    CardComponent,
    ErrorStateComponent,
    FieldComponent,
    InputDirective,
    LoadingComponent,
    ProductionConfirmDialogComponent,
  ],
  providers: [provideCompaniesI18n()],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './production-wizard.page.html',
  styleUrls: ['./companies-page.css', './production-wizard.page.css'],
})
export class ProductionWizardPage {
  protected readonly tones = STATUS_TONES;

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
    if (!this.setup.readiness()?.certificate_valid) {
      return { status: 'missing', detail: this.text('wizard.steps.certificate.missing') };
    }
    const active = this.setup.activeCertificate();
    return {
      status: 'done',
      detail: active
        ? this.text('wizard.steps.certificate.done', {
            subject: active.subject ?? active.name ?? '',
            days: Number(active.certificate_days_left ?? 0),
          })
        : this.text('wizard.steps.certificate.valid'),
    };
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

  /** Familia del documento soporte (opcional): su software y una resolución vigente en su ambiente actual. */
  private readonly supportDocumentStep = computed<Step>(() => {
    const support = this.setup.readiness()?.support_document;
    const software = support?.software_configured === true;
    const resolution = !!support?.resolutions?.some(
      (entry) => Number(entry.type_document_id) === SUPPORT_DOCUMENT_TYPE && entry.valid,
    );
    const section = (path: string, label: string) => ({
      path: `../${path}`,
      label: this.text('wizard.open', { section: this.text(`layout.tabs.${label}`) }),
    });
    const base = {
      id: 'supportDocument',
      title: this.text('wizard.steps.supportDocument.title'),
    } as const;
    if (software && resolution) {
      const environment = Number(support?.type_environment_id) === PRODUCTION ? 1 : 2;
      return {
        ...base,
        status: 'done',
        detail: this.text('wizard.steps.supportDocument.done', {
          environment: this.text(`wizard.steps.supportDocument.environments.${environment}`),
        }),
      };
    }
    return software
      ? {
          ...base,
          status: 'missing',
          detail: this.text('wizard.steps.supportDocument.missingResolution'),
          section: section('resoluciones', 'resolutions'),
        }
      : {
          ...base,
          status: 'missing',
          detail: this.text('wizard.steps.supportDocument.missing'),
          section: section('software', 'software'),
        };
  });

  protected readonly steps = computed<Step[]>(() => {
    const readiness = this.setup.readiness();
    const locked = !this.unlocked();
    const invoiceResolution = readiness?.resolutions?.find(
      (entry) => Number(entry.type_document_id) === 1,
    );
    const software = this.setup.invoiceSoftware();
    const accepted = readiness?.test_set_accepted ?? null;
    const section = (path: string, label: string) => ({
      path: `../${path}`,
      label: this.text('wizard.open', { section: this.text(`layout.tabs.${label}`) }),
    });
    const softwareDone = !!readiness?.software_configured && !!readiness.test_set_id_configured;
    return [
      {
        id: 'company',
        title: this.text('wizard.steps.company.title'),
        status: readiness?.company_complete ? 'done' : 'missing',
        detail: this.text(
          readiness?.company_complete
            ? 'wizard.steps.company.done'
            : 'wizard.steps.company.missing',
        ),
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
        status: invoiceResolution?.valid ? 'done' : 'missing',
        detail: this.text(
          invoiceResolution?.valid
            ? 'wizard.steps.resolution.done'
            : 'wizard.steps.resolution.missing',
        ),
        section: section('resoluciones', 'resolutions'),
      },
      {
        id: 'software',
        title: this.text('wizard.steps.software.title'),
        status: softwareDone ? 'done' : 'missing',
        detail: softwareDone
          ? this.text('wizard.steps.software.done', {
              identifier: software?.identifier ?? '',
              testSetId: software?.test_set_id ?? '',
            })
          : this.text(
              readiness?.software_configured
                ? 'wizard.steps.software.missingTestSet'
                : 'wizard.steps.software.missing',
            ),
        section: section('software', 'software'),
      },
      {
        id: 'testSet',
        title: this.text('wizard.steps.testSet.title'),
        status: locked
          ? 'locked'
          : accepted === true
            ? 'done'
            : this.sending() || this.testDocument()?.pending
              ? 'inProgress'
              : 'missing',
        detail: this.text(locked ? 'wizard.lockedHint' : 'wizard.steps.testSet.description'),
        // With the set approved, the template shows a badge in place of the button (like the production step).
        note:
          locked || accepted === true
            ? undefined
            : this.text(
                accepted === null
                  ? 'wizard.steps.testSet.noResult'
                  : 'wizard.steps.testSet.setRejected',
              ),
      },
      this.supportDocumentStep(),
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
    this.refresh();
  }

  /** Vuelve a pedir el estado del paso a producción y los datos que se muestran del certificado y el software. */
  protected refresh(): void {
    void this.setup.loadReadiness();
    void this.setup.loadCertificates();
    void this.setup.loadSoftware();
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
        complete: () => {
          this.sending.set(false);
          // Con la respuesta final de la DIAN, el resultado del set de pruebas puede haber cambiado.
          void this.setup.loadReadiness();
        },
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
