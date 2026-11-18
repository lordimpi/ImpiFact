import { UiTableLabels } from '@impifact/ui';
import { TranslocoService } from '@jsverse/transloco';
import { MasterListToolbarLabels } from '../components/master-list-toolbar.component';
import { mastersText } from '../masters-i18n';

/** Textos de `ui-table` de un listado; `prefix` es `customers.list` o `products.list`. */
export function listLabels(transloco: TranslocoService, prefix: string): UiTableLabels {
  const text = (key: string) => mastersText(transloco, key);
  return {
    loading: text(`${prefix}.loading`),
    emptyTitle: text(`${prefix}.emptyTitle`),
    emptyDescription: text(`${prefix}.emptyDescription`),
    errorTitle: text(`${prefix}.errorTitle`),
    errorDescription: text(`${prefix}.errorDescription`),
    retry: text('common.retry'),
    paginator: {
      navigation: text('common.paginator.navigation'),
      previous: text('common.paginator.previous'),
      next: text('common.paginator.next'),
      page: text('common.paginator.page'),
    },
  };
}

/** Textos de la barra de búsqueda y estado. */
export function toolbarLabels(
  transloco: TranslocoService,
  searchKey: string,
): MasterListToolbarLabels {
  const text = (key: string) => mastersText(transloco, key);
  return {
    search: text(searchKey),
    status: text('common.status.label'),
    statuses: {
      active: text('common.status.active'),
      inactive: text('common.status.inactive'),
      all: text('common.status.all'),
    },
  };
}
