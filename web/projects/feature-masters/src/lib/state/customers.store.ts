import { inject } from '@angular/core';
import {
  Api,
  CustomerResponse,
  UpdateCustomerCommand,
  deactivateCustomer,
  listCustomers,
  updateCustomer,
} from '@impifact/api';
import { withServerList } from '@impifact/core';
import { patchState, signalStore, withMethods, withState } from '@ngrx/signals';
import { firstValueFrom } from 'rxjs';
import { DEFAULT_MASTER_FILTERS, MasterFilters, masterListLoader } from './master-list';

/** Datos del cliente para la edición (PUT reemplaza todo): los mismos que llegaron, con el estado pedido. */
export function customerUpdateBody(
  customer: CustomerResponse,
  isActive: boolean,
): UpdateCustomerCommand {
  return {
    type_document_identification_id: customer.type_document_identification_id ?? null,
    identification_number: customer.identification_number ?? null,
    dv: customer.dv ?? null,
    name: customer.name ?? null,
    phone: customer.phone ?? null,
    address: customer.address ?? null,
    email: customer.email ?? null,
    municipality_id: customer.municipality_id ?? null,
    type_organization_id: customer.type_organization_id ?? null,
    type_regime_id: customer.type_regime_id ?? null,
    type_liability_id: customer.type_liability_id ?? null,
    merchant_registration: customer.merchant_registration ?? null,
    is_active: isActive,
  };
}

/**
 * Listado de clientes de la empresa activa (NgRx SignalStore con `withServerList`): página, orden y búsqueda los
 * aplica el Api. Desactivar y reactivar llaman al Api y vuelven a pedir la página visible.
 */
export const CustomersStore = signalStore(
  withServerList<CustomerResponse, MasterFilters>({
    filters: DEFAULT_MASTER_FILTERS,
    sort: { key: 'name', direction: 'asc' },
    loader: masterListLoader(listCustomers),
  }),
  withState({ busyId: null as string | null }),
  withMethods((store) => {
    const api = inject(Api);

    /** Corre una acción sobre una fila y refresca la página; devuelve si salió bien (el error lo avisa core). */
    async function act(id: string, action: () => Promise<unknown>): Promise<boolean> {
      patchState(store, { busyId: id });
      try {
        await action();
        store.refresh();
        return true;
      } catch {
        return false;
      } finally {
        patchState(store, { busyId: null });
      }
    }

    return {
      deactivate(customer: CustomerResponse): Promise<boolean> {
        const id = customer.id ?? '';
        return act(id, () =>
          firstValueFrom(api.invoke(deactivateCustomer, { id }), { defaultValue: null }),
        );
      },
      reactivate(customer: CustomerResponse): Promise<boolean> {
        const id = customer.id ?? '';
        return act(id, () =>
          firstValueFrom(
            api.invoke(updateCustomer, { id, body: customerUpdateBody(customer, true) }),
          ),
        );
      },
    };
  }),
);
