import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CustomerResponse } from '@impifact/api';
import { CatalogsStore, ToastStore } from '@impifact/core';
import {
  BadgeComponent,
  ButtonComponent,
  ButtonLinkDirective,
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
import { NIT_IDENTIFICATION_TYPE } from '../models/catalogs';
import { CustomersStore } from '../state/customers.store';
import { bindFiltersToUrl } from '../state/master-list-url';
import { listLabels, toolbarLabels } from './list-labels';

/**
 * Listado de clientes (`/clientes`). Búsqueda y estado viven en la URL; página y orden los aplica el Api. Cada fila
 * se edita, se desactiva (con confirmación) o se reactiva.
 */
@Component({
  selector: 'feat-customer-list-page',
  imports: [
    ButtonLinkDirective,
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
  providers: [provideMastersI18n(), CustomersStore],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './customer-list.page.html',
  styleUrl: './masters-page.css',
})
export class CustomerListPage {
  protected readonly store = inject(CustomersStore);
  private readonly transloco = inject(TranslocoService);
  private readonly toasts = inject(ToastStore);
  /** Tipos de identificación del catálogo de la DIAN, para el nombre en cada fila. */
  private readonly identificationTypes = inject(CatalogsStore).items('identification-types');

  /** Búsqueda en la URL (`?q=`). */
  readonly q = input<string>();
  /** Estado en la URL (`?estado=active|inactive|all`). */
  readonly estado = input<string>();

  protected readonly url = bindFiltersToUrl(this.store, { q: this.q, estado: this.estado });

  /** Cliente que espera la confirmación de desactivar. */
  protected readonly toDeactivate = signal<CustomerResponse | null>(null);
  protected readonly confirmOpen = signal(false);

  protected readonly columns = computed<UiTableColumn[]>(() => [
    {
      key: 'identification_number',
      header: this.text('customers.list.identification'),
      sortable: true,
    },
    { key: 'name', header: this.text('customers.list.name'), sortable: true },
    { key: 'email', header: this.text('customers.list.email') },
    { key: 'is_active', header: this.text('common.status.label') },
    { key: 'actions', header: this.text('common.actions') },
  ]);

  protected readonly tableLabels = computed(() => listLabels(this.transloco, 'customers.list'));
  protected readonly toolbarLabels = computed(() =>
    toolbarLabels(this.transloco, 'customers.list.searchLabel'),
  );
  protected readonly confirmLabels = computed(() => ({
    header: this.text('customers.list.confirmTitle'),
    body: this.text('customers.list.confirmBody', { name: this.toDeactivate()?.name ?? '' }),
    confirm: this.text('common.deactivate'),
    cancel: this.text('common.cancel'),
    close: this.text('common.close'),
  }));

  protected readonly trackById = (_: number, row: CustomerResponse) => row.id;

  /** «NIT 900123456-8» o «Cédula de ciudadanía 1020304050». */
  protected identification(row: CustomerResponse): string {
    const type = Number(row.type_document_identification_id);
    const name = this.identificationTypes().find((item) => item.id === type)?.name ?? '';
    const dv = type === NIT_IDENTIFICATION_TYPE && row.dv ? `-${row.dv}` : '';
    return `${name} ${row.identification_number ?? ''}${dv}`.trim();
  }

  protected rowText(key: string, row: CustomerResponse): string {
    return this.text(key, { name: row.name ?? '' });
  }

  protected askDeactivate(row: CustomerResponse): void {
    this.toDeactivate.set(row);
    this.confirmOpen.set(true);
  }

  protected async deactivate(): Promise<void> {
    const row = this.toDeactivate();
    if (!row) {
      return;
    }
    if (await this.store.deactivate(row)) {
      this.toasts.show({ kind: 'success', text: this.text('customers.list.deactivated') });
    }
    this.confirmOpen.set(false);
  }

  protected async reactivate(row: CustomerResponse): Promise<void> {
    if (await this.store.reactivate(row)) {
      this.toasts.show({ kind: 'success', text: this.text('customers.list.reactivated') });
    }
  }

  private text(key: string, params?: Record<string, unknown>): string {
    return mastersText(this.transloco, key, params);
  }
}
