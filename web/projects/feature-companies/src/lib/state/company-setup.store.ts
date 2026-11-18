import { HttpContext } from '@angular/common/http';
import { computed, inject } from '@angular/core';
import {
  Api,
  CertificateResponse,
  CertificateSavedResponse,
  CompanyResponse,
  DocumentDetailResponse,
  EnvironmentResponse,
  NumberingRangeResponse,
  ResolutionResponse,
  SetEnvironmentCommand,
  SetSoftwareCommand,
  SoftwareResponse,
  UpdateCompanyCommand,
  UpsertResolutionCommand,
  getNumberingRanges,
  listCompanyCertificates,
  saveCompanyResolution,
  setCompanyEnvironment,
  setCompanySoftware,
  updateCompany,
  uploadCompanyCertificate,
} from '@impifact/api';
import { MyCompaniesStore, SKIP_ERROR_TOAST } from '@impifact/core';
import { patchState, signalStore, withComputed, withMethods, withState } from '@ngrx/signals';
import { firstValueFrom } from 'rxjs';
import { INVOICE_KIND } from '../models/dian';

/** Empresa con la que se trabaja: la activa de la sesión, con el NIT y el DV que piden las rutas del Api. */
export interface CompanyRef {
  id: string;
  name: string;
  nit: string;
  dv: string;
}

export type LoadStatus = 'idle' | 'loading' | 'ready' | 'error';

interface CompanySetupState {
  company: CompanyRef | null;
  /** Datos guardados en esta sesión (el Api no tiene lectura de la empresa). */
  saved: CompanyResponse | null;
  /** Ambientes por familia; `null` mientras el Api no los haya devuelto en esta sesión. */
  environments: EnvironmentResponse[] | null;
  software: SoftwareResponse[] | null;
  /** Resoluciones guardadas en esta sesión (el Api no las lista). */
  resolutions: ResolutionResponse[];
  certificates: CertificateResponse[];
  certificatesStatus: LoadStatus;
  /** Set de pruebas escrito en el asistente cuando no se guardó el software en esta sesión. */
  testSetId: string;
  /** Última factura de prueba enviada desde el asistente, con su estado en la DIAN. */
  testDocument: DocumentDetailResponse | null;
}

const EMPTY: Omit<CompanySetupState, 'company'> = {
  saved: null,
  environments: null,
  software: null,
  resolutions: [],
  certificates: [],
  certificatesStatus: 'idle',
  testSetId: '',
  testDocument: null,
};

/** Las páginas muestran los errores en el formulario, no en un aviso flotante. */
const IN_FORM = () => new HttpContext().set(SKIP_ERROR_TOAST, true);

/**
 * Configuración de la empresa activa (NgRx SignalStore), compartida por las pantallas de la empresa y el asistente
 * de paso a producción. Los métodos son la única forma de cambiarla y llaman al cliente generado; los errores
 * (`AppError`) llegan a quien llama para ponerlos junto a cada campo.
 *
 * El Api no tiene lectura de los datos, ambientes, software ni resoluciones de la empresa: el store recuerda lo que
 * devolvió cada guardado de esta sesión. Al cambiar de empresa, se olvida todo.
 */
export const CompanySetupStore = signalStore(
  { providedIn: 'root' },
  withState<CompanySetupState>({ company: null, ...EMPTY }),
  withComputed((store) => ({
    activeCertificate: computed(
      () => store.certificates().find((certificate) => certificate.is_active) ?? null,
    ),
    /** Software de la factura electrónica, si se guardó en esta sesión. */
    invoiceSoftware: computed(
      () => store.software()?.find((entry) => Number(entry.document_kind) === INVOICE_KIND) ?? null,
    ),
  })),
  withComputed((store) => ({
    /** Set de pruebas para el asistente: el escrito allí o el del software guardado. */
    effectiveTestSetId: computed(
      () => store.testSetId().trim() || store.invoiceSoftware()?.test_set_id?.trim() || '',
    ),
  })),
  withMethods((store, api = inject(Api), directory = inject(MyCompaniesStore)) => {
    const current = () => {
      const company = store.company();
      if (!company) {
        throw new Error('CompanySetupStore.use() debe llamarse antes.');
      }
      return company;
    };

    return {
      /** Fija la empresa con la que se trabaja; si es otra, olvida lo de la anterior. */
      use(company: CompanyRef): void {
        if (store.company()?.id === company.id) {
          patchState(store, { company });
          return;
        }
        patchState(store, { company, ...EMPTY });
      },

      environmentOf(kind: number): number | null {
        const found = store.environments()?.find((entry) => Number(entry.document_kind) === kind);
        return found ? Number(found.type_environment_id) : null;
      },

      async saveCompany(body: UpdateCompanyCommand): Promise<CompanyResponse> {
        const { nit, dv } = current();
        const response = await firstValueFrom(
          api.invoke(updateCompany, { nit, dv, body }, IN_FORM()),
        );
        patchState(store, {
          saved: response.company,
          environments: response.company.environments ?? store.environments(),
        });
        // La razón social se muestra en el menú y en la selección de empresa.
        void directory.reload();
        return response.company;
      },

      async setEnvironments(body: SetEnvironmentCommand): Promise<EnvironmentResponse[]> {
        const response = await firstValueFrom(
          api.invoke(setCompanyEnvironment, { nit: current().nit, body }, IN_FORM()),
        );
        const environments = response.environments ?? [];
        patchState(store, { environments });
        return environments;
      },

      async saveSoftware(body: SetSoftwareCommand): Promise<SoftwareResponse[]> {
        const response = await firstValueFrom(
          api.invoke(setCompanySoftware, { nit: current().nit, body }, IN_FORM()),
        );
        const software = response.software ?? [];
        patchState(store, { software });
        return software;
      },

      async saveResolution(body: UpsertResolutionCommand): Promise<ResolutionResponse> {
        const response = await firstValueFrom(
          api.invoke(saveCompanyResolution, { nit: current().nit, body }, IN_FORM()),
        );
        const saved = response.resolution;
        patchState(store, {
          resolutions: [...store.resolutions().filter((entry) => entry.id !== saved.id), saved],
        });
        return saved;
      },

      /**
       * Sube el certificado en base64 con su contraseña. La contraseña solo viaja en esta solicitud: el store no la
       * guarda.
       */
      async uploadCertificate(
        certificate: string,
        password: string,
      ): Promise<CertificateSavedResponse> {
        const response = await firstValueFrom(
          api.invoke(
            uploadCompanyCertificate,
            { nit: current().nit, body: { certificate, password } },
            IN_FORM(),
          ),
        );
        const uploaded = response.certificate;
        patchState(store, {
          certificates: [
            uploaded,
            ...store
              .certificates()
              .filter((entry) => entry.id !== uploaded.id)
              .map((entry) => (uploaded.is_active ? { ...entry, is_active: false } : entry)),
          ],
          certificatesStatus: 'ready',
        });
        return response;
      },

      async loadCertificates(): Promise<void> {
        patchState(store, { certificatesStatus: 'loading' });
        try {
          const response = await firstValueFrom(
            api.invoke(listCompanyCertificates, { nit: current().nit }, IN_FORM()),
          );
          patchState(store, {
            certificates: response.certificates ?? [],
            certificatesStatus: 'ready',
          });
        } catch {
          patchState(store, { certificatesStatus: 'error' });
        }
      },

      /** Consulta en vivo a la DIAN de los rangos de numeración del software. */
      numberingRanges(softwareId?: string): Promise<NumberingRangeResponse> {
        return firstValueFrom(
          api.invoke(
            getNumberingRanges,
            { nit: current().nit, software_id: softwareId?.trim() || undefined },
            IN_FORM(),
          ),
        );
      },

      setTestSetId(testSetId: string): void {
        patchState(store, { testSetId });
      },

      setTestDocument(testDocument: DocumentDetailResponse | null): void {
        patchState(store, { testDocument });
      },
    };
  }),
);
