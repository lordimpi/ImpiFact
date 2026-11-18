import { HttpContext } from '@angular/common/http';
import { inject } from '@angular/core';
import { Api, PlanResponse, StorePlanCommand, listPlans, savePlan } from '@impifact/api';
import { SKIP_ERROR_TOAST } from '@impifact/core';
import { patchState, signalStore, withMethods, withProps, withState } from '@ngrx/signals';
import { firstValueFrom } from 'rxjs';

/** Plan de la plataforma con los números ya convertidos (el Api los puede mandar como número o como texto). */
export interface PlatformPlan {
  id: number;
  name: string;
  period: number;
  qtyInvoice: number | null;
  qtyPayroll: number | null;
  qtyRadian: number | null;
  qtyDs: number | null;
  active: boolean;
  observations: string | null;
}

const toNumber = (value: number | string | null | undefined): number | null => {
  if (value === null || value === undefined || value === '') {
    return null;
  }
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
};

export function toPlatformPlan(plan: PlanResponse): PlatformPlan {
  return {
    id: toNumber(plan.id) ?? 0,
    name: plan.name ?? '',
    period: toNumber(plan.period) ?? 0,
    qtyInvoice: toNumber(plan.qty_docs_invoice),
    qtyPayroll: toNumber(plan.qty_docs_payroll),
    qtyRadian: toNumber(plan.qty_docs_radian),
    qtyDs: toNumber(plan.qty_docs_ds),
    active: plan.state !== false,
    observations: plan.observations ?? null,
  };
}

interface PlatformPlansState {
  plans: PlatformPlan[];
  loading: boolean;
  error: unknown;
  loaded: boolean;
}

/**
 * Planes de la plataforma (NgRx SignalStore del feature), compartidos por la lista de planes y la asignación a una
 * empresa. El Api devuelve todos los planes en una sola respuesta, sin paginar (son pocos); `data` es una lista.
 */
export const PlatformPlansStore = signalStore(
  { providedIn: 'root' },
  withState<PlatformPlansState>({ plans: [], loading: false, error: null, loaded: false }),
  withProps(() => ({ _api: inject(Api) })),
  withMethods((store) => ({
    async load(): Promise<void> {
      patchState(store, { loading: true, error: null });
      try {
        const response = await firstValueFrom(store._api.invoke(listPlans));
        const data: unknown = response.data;
        const plans = Array.isArray(data) ? (data as PlanResponse[]).map(toPlatformPlan) : [];
        patchState(store, {
          plans: plans.sort((a, b) => a.id - b.id),
          loading: false,
          loaded: true,
        });
      } catch (error) {
        patchState(store, { loading: false, error });
      }
    },
    /**
     * Crea o actualiza (`PUT /plans/{id}`). Lanza el error sin aviso automático: el formulario lo muestra junto a
     * su campo.
     */
    async save(id: number, body: StorePlanCommand): Promise<void> {
      await firstValueFrom(
        store._api.invoke(savePlan, { id, body }, new HttpContext().set(SKIP_ERROR_TOAST, true)),
      );
    },
    /** El siguiente id libre, para proponerlo al crear un plan. */
    nextId(): number {
      return store.plans().reduce((max, plan) => Math.max(max, plan.id), 0) + 1;
    },
  })),
);
