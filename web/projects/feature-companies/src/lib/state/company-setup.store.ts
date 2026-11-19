import { HttpContext } from '@angular/common/http';
import { computed, inject } from '@angular/core';
import {
  Api,
  CertificateResponse,
  CertificateSavedResponse,
  CompanyResponse,
  CompanySetupStatusResponse,
  ConfiguredSoftwareResponse,
  DocumentDetailResponse,
  EnvironmentResponse,
  NumberingRangeResponse,
  ResolutionResponse,
  ResolutionStatusResponse,
  SetEnvironmentCommand,
  SetSoftwareCommand,
  SoftwareResponse,
  UpdateCompanyCommand,
  UpsertResolutionCommand,
  getCompany,
  getCompanyEnvironments,
  getCompanySetupStatus,
  getCompanySoftware,
  getNumberingRanges,
  listCompanyCertificates,
  listCompanyResolutions,
  saveCompanyResolution,
  setCompanyEnvironment,
  setCompanySoftware,
  updateCompany,
  uploadCompanyCertificate,
} from '@impifact/api';
import { MyCompaniesStore, SKIP_ERROR_TOAST } from '@impifact/core';
import { patchState, signalStore, withComputed, withMethods, withState } from '@ngrx/signals';
import { firstValueFrom } from 'rxjs';
import { HABILITATION, INVOICE_KIND } from '../models/dian';

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
  /** Datos de la empresa como los devuelve `getCompany` (la misma forma que acepta `updateCompany`). */
  companyData: CompanyResponse | null;
  companyStatus: LoadStatus;
  /** Ambiente actual de cada familia de documentos. */
  environments: EnvironmentResponse[] | null;
  environmentsStatus: LoadStatus;
  /** Software de cada familia y ambiente; del PIN solo se sabe si existe (`has_pin`). */
  software: ConfiguredSoftwareResponse[] | null;
  softwareStatus: LoadStatus;
  /** Resoluciones con el próximo número, lo que queda y los días de vigencia. */
  resolutions: ResolutionStatusResponse[];
  resolutionsStatus: LoadStatus;
  /** Estado del paso a producción que calcula el Api (`getCompanySetupStatus`). */
  readiness: CompanySetupStatusResponse | null;
  readinessStatus: LoadStatus;
  certificates: CertificateResponse[];
  certificatesStatus: LoadStatus;
  /** Set de pruebas escrito en el asistente; tiene prioridad sobre el del software guardado. */
  testSetId: string;
  /** Última factura de prueba enviada desde el asistente, con su estado en la DIAN. */
  testDocument: DocumentDetailResponse | null;
}

const EMPTY: Omit<CompanySetupState, 'company'> = {
  companyData: null,
  companyStatus: 'idle',
  environments: null,
  environmentsStatus: 'idle',
  software: null,
  softwareStatus: 'idle',
  resolutions: [],
  resolutionsStatus: 'idle',
  readiness: null,
  readinessStatus: 'idle',
  certificates: [],
  certificatesStatus: 'idle',
  testSetId: '',
  testDocument: null,
};

/** Campos del estado que guardan el estado de una lectura. */
type StatusKey =
  | 'companyStatus'
  | 'environmentsStatus'
  | 'softwareStatus'
  | 'resolutionsStatus'
  | 'readinessStatus';

/** Las páginas muestran los errores en el formulario, no en un aviso flotante. */
const IN_FORM = () => new HttpContext().set(SKIP_ERROR_TOAST, true);

/**
 * Configuración de la empresa activa (NgRx SignalStore), compartida por las pantallas de la empresa y el asistente
 * de paso a producción. Los métodos son la única forma de cambiarla y llaman al cliente generado; los errores de un
 * guardado (`AppError`) llegan a quien llama para ponerlos junto a cada campo.
 *
 * Cada pantalla pide al abrir lo que muestra (`loadCompany`, `loadEnvironments`, `loadSoftware`, `loadResolutions`,
 * `loadReadiness`); mientras llega la respuesta se conserva lo anterior. Después de cada guardado se vuelve a pedir
 * el estado del paso a producción. Al cambiar de empresa, se olvida todo.
 */
export const CompanySetupStore = signalStore(
  { providedIn: 'root' },
  withState<CompanySetupState>({ company: null, ...EMPTY }),
  withComputed((store) => ({
    activeCertificate: computed(
      () => store.certificates().find((certificate) => certificate.is_active) ?? null,
    ),
    /**
     * Software de la factura electrónica en su ambiente actual; si los ambientes aún no se leyeron, el de
     * habilitación (o el primero que haya).
     */
    invoiceSoftware: computed(() => {
      const rows = (store.software() ?? []).filter(
        (entry) => Number(entry.document_kind) === INVOICE_KIND,
      );
      const current = store
        .environments()
        ?.find((entry) => Number(entry.document_kind) === INVOICE_KIND)?.type_environment_id;
      const environment = current === undefined ? HABILITATION : Number(current);
      return (
        rows.find((entry) => Number(entry.type_environment_id) === environment) ?? rows[0] ?? null
      );
    }),
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

    /**
     * Pide una lectura y la guarda junto con su estado. Si mientras tanto se cambió de empresa, la respuesta se
     * descarta. Devuelve lo leído, o `null` si falló (el error queda en el estado, para mostrarlo con reintento).
     */
    async function read<T>(
      status: StatusKey,
      fetch: (nit: string) => Promise<T>,
      apply: (value: T) => Partial<CompanySetupState>,
    ): Promise<T | null> {
      const company = current();
      patchState(store, { [status]: 'loading' });
      try {
        const value = await fetch(company.nit);
        if (store.company()?.id !== company.id) {
          return null;
        }
        patchState(store, { ...apply(value), [status]: 'ready' });
        return value;
      } catch {
        if (store.company()?.id === company.id) {
          patchState(store, { [status]: 'error' });
        }
        return null;
      }
    }

    const loadReadiness = () =>
      read(
        'readinessStatus',
        (nit) => firstValueFrom(api.invoke(getCompanySetupStatus, { nit }, IN_FORM())),
        (readiness) => ({
          readiness,
          ...(readiness.environments ? { environments: readiness.environments } : {}),
        }),
      );

    const loadSoftware = () =>
      read(
        'softwareStatus',
        (nit) => firstValueFrom(api.invoke(getCompanySoftware, { nit }, IN_FORM())),
        (response) => ({ software: response.software ?? [] }),
      );

    const loadResolutions = () =>
      read(
        'resolutionsStatus',
        (nit) => firstValueFrom(api.invoke(listCompanyResolutions, { nit }, IN_FORM())),
        (response) => ({ resolutions: response.resolutions ?? [] }),
      );

    /** Después de guardar, el estado del paso a producción puede haber cambiado. */
    const afterSave = () => void loadReadiness();

    return {
      /** Fija la empresa con la que se trabaja; si es otra, olvida lo de la anterior. */
      use(company: CompanyRef): void {
        if (store.company()?.id === company.id) {
          patchState(store, { company });
          return;
        }
        patchState(store, { company, ...EMPTY });
      },

      /** Datos de la empresa; el resultado sirve para llenar el formulario. */
      loadCompany(): Promise<CompanyResponse | null> {
        return read(
          'companyStatus',
          (nit) => firstValueFrom(api.invoke(getCompany, { nit }, IN_FORM())),
          (companyData) => ({
            companyData,
            ...(companyData.environments ? { environments: companyData.environments } : {}),
          }),
        );
      },

      loadEnvironments(): Promise<unknown> {
        return read(
          'environmentsStatus',
          (nit) => firstValueFrom(api.invoke(getCompanyEnvironments, { nit }, IN_FORM())),
          (response) => ({ environments: response.environments ?? [] }),
        );
      },

      loadSoftware,
      loadResolutions,
      loadReadiness,

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
          companyData: response.company,
          environments: response.company.environments ?? store.environments(),
        });
        // La razón social se muestra en el menú y en la selección de empresa.
        void directory.reload();
        afterSave();
        return response.company;
      },

      async setEnvironments(body: SetEnvironmentCommand): Promise<EnvironmentResponse[]> {
        const response = await firstValueFrom(
          api.invoke(setCompanyEnvironment, { nit: current().nit, body }, IN_FORM()),
        );
        const environments = response.environments ?? [];
        patchState(store, { environments, environmentsStatus: 'ready' });
        afterSave();
        return environments;
      },

      async saveSoftware(body: SetSoftwareCommand): Promise<SoftwareResponse[]> {
        const response = await firstValueFrom(
          api.invoke(setCompanySoftware, { nit: current().nit, body }, IN_FORM()),
        );
        // La respuesta no dice si hay PIN guardado: se vuelve a leer el software.
        void loadSoftware();
        afterSave();
        return response.software ?? [];
      },

      async saveResolution(body: UpsertResolutionCommand): Promise<ResolutionResponse> {
        const response = await firstValueFrom(
          api.invoke(saveCompanyResolution, { nit: current().nit, body }, IN_FORM()),
        );
        // La lista trae el próximo número y lo que queda del rango: se vuelve a leer.
        void loadResolutions();
        afterSave();
        return response.resolution;
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
        afterSave();
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
