import { inject } from '@angular/core';
import {
  Api,
  ProductResponse,
  UpdateProductCommand,
  deactivateProduct,
  listProducts,
  updateProduct,
} from '@impifact/api';
import { withServerList } from '@impifact/core';
import { patchState, signalStore, withMethods, withState } from '@ngrx/signals';
import { firstValueFrom } from 'rxjs';
import { DEFAULT_MASTER_FILTERS, MasterFilters, masterListLoader } from './master-list';

/** Datos del producto para la edición (PUT reemplaza todo): los mismos que llegaron, con el estado pedido. */
export function productUpdateBody(
  product: ProductResponse,
  isActive: boolean,
): UpdateProductCommand {
  return {
    code: product.code ?? null,
    name: product.name ?? null,
    description: product.description ?? null,
    type_item_identification_id: product.type_item_identification_id ?? null,
    unit_measure_id: product.unit_measure_id ?? null,
    unit_price: product.unit_price ?? null,
    tax_id: product.tax_id ?? null,
    tax_percent: product.tax_percent ?? null,
    tax_per_unit_amount: product.tax_per_unit_amount ?? null,
    is_active: isActive,
  };
}

/**
 * Listado de productos de la empresa activa (NgRx SignalStore con `withServerList`): página, orden y búsqueda los
 * aplica el Api. Desactivar y reactivar llaman al Api y vuelven a pedir la página visible.
 */
export const ProductsStore = signalStore(
  withServerList<ProductResponse, MasterFilters>({
    filters: DEFAULT_MASTER_FILTERS,
    sort: { key: 'name', direction: 'asc' },
    loader: masterListLoader(listProducts),
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
      deactivate(product: ProductResponse): Promise<boolean> {
        const id = product.id ?? '';
        return act(id, () =>
          firstValueFrom(api.invoke(deactivateProduct, { id }), { defaultValue: null }),
        );
      },
      reactivate(product: ProductResponse): Promise<boolean> {
        const id = product.id ?? '';
        return act(id, () =>
          firstValueFrom(api.invoke(updateProduct, { id, body: productUpdateBody(product, true) })),
        );
      },
    };
  }),
);
