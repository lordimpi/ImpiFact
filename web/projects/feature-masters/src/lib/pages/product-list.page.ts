import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ProductResponse } from '@impifact/api';
import { ToastStore } from '@impifact/core';
import {
  BadgeComponent,
  ButtonComponent,
  CardComponent,
  PageTitleComponent,
  TableCellDirective,
  TableComponent,
  UiTableColumn,
} from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { ConfirmDialogComponent } from '../components/confirm-dialog.component';
import { MasterListToolbarComponent } from '../components/master-list-toolbar.component';
import { mastersText, provideMastersI18n } from '../masters-i18n';
import { bindFiltersToUrl } from '../state/master-list-url';
import { ProductsStore } from '../state/products.store';
import { listLabels, toolbarLabels } from './list-labels';

/** Precio en pesos colombianos con los separadores de es-CO (solo formato: el valor es el que guardó el Api). */
const PRICE_FORMAT = new Intl.NumberFormat('es-CO', {
  style: 'currency',
  currency: 'COP',
  minimumFractionDigits: 2,
  maximumFractionDigits: 6,
});

/**
 * Listado de productos y servicios (`/productos`). Búsqueda y estado viven en la URL; página y orden los aplica el
 * Api. Cada fila se edita, se desactiva (con confirmación) o se reactiva.
 */
@Component({
  selector: 'feat-product-list-page',
  imports: [
    RouterLink,
    TranslocoPipe,
    BadgeComponent,
    ButtonComponent,
    CardComponent,
    PageTitleComponent,
    TableComponent,
    TableCellDirective,
    ConfirmDialogComponent,
    MasterListToolbarComponent,
  ],
  providers: [provideMastersI18n(), ProductsStore],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './product-list.page.html',
  styleUrl: './masters-page.css',
})
export class ProductListPage {
  protected readonly store = inject(ProductsStore);
  private readonly transloco = inject(TranslocoService);
  private readonly toasts = inject(ToastStore);

  /** Búsqueda en la URL (`?q=`). */
  readonly q = input<string>();
  /** Estado en la URL (`?estado=active|inactive|all`). */
  readonly estado = input<string>();

  protected readonly url = bindFiltersToUrl(this.store, { q: this.q, estado: this.estado });

  /** Producto que espera la confirmación de desactivar. */
  protected readonly toDeactivate = signal<ProductResponse | null>(null);
  protected readonly confirmOpen = signal(false);

  protected readonly columns = computed<UiTableColumn[]>(() => [
    { key: 'code', header: this.text('products.list.code'), sortable: true },
    { key: 'name', header: this.text('products.list.name'), sortable: true },
    { key: 'unit_price', header: this.text('products.list.unitPrice'), align: 'end' },
    { key: 'is_active', header: this.text('common.status.label') },
    { key: 'actions', header: this.text('common.actions') },
  ]);

  protected readonly tableLabels = computed(() => listLabels(this.transloco, 'products.list'));
  protected readonly toolbarLabels = computed(() =>
    toolbarLabels(this.transloco, 'products.list.searchLabel'),
  );
  protected readonly confirmLabels = computed(() => ({
    header: this.text('products.list.confirmTitle'),
    body: this.text('products.list.confirmBody', { name: this.toDeactivate()?.name ?? '' }),
    confirm: this.text('common.deactivate'),
    cancel: this.text('common.cancel'),
    close: this.text('common.close'),
  }));

  protected readonly trackById = (_: number, row: ProductResponse) => row.id;

  protected price(row: ProductResponse): string {
    const value = Number(row.unit_price);
    return Number.isFinite(value) ? PRICE_FORMAT.format(value) : String(row.unit_price ?? '');
  }

  protected rowText(key: string, row: ProductResponse): string {
    return this.text(key, { name: row.name ?? '' });
  }

  protected askDeactivate(row: ProductResponse): void {
    this.toDeactivate.set(row);
    this.confirmOpen.set(true);
  }

  protected async deactivate(): Promise<void> {
    const row = this.toDeactivate();
    if (!row) {
      return;
    }
    if (await this.store.deactivate(row)) {
      this.toasts.show({ kind: 'success', text: this.text('products.list.deactivated') });
    }
    this.confirmOpen.set(false);
  }

  protected async reactivate(row: ProductResponse): Promise<void> {
    if (await this.store.reactivate(row)) {
      this.toasts.show({ kind: 'success', text: this.text('products.list.reactivated') });
    }
  }

  private text(key: string, params?: Record<string, unknown>): string {
    return mastersText(this.transloco, key, params);
  }
}
