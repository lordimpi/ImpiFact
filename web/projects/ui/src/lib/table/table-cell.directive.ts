import { Directive, TemplateRef, inject, input } from '@angular/core';

/** Contexto de la plantilla de celda: `let-row` recibe la fila. */
export interface UiTableCellContext<T> {
  $implicit: T;
}

/**
 * Plantilla propia para las celdas de una columna de `ui-table`, por ejemplo acciones o estados:
 * `<ng-template uiTableCell="status" let-row>…</ng-template>`.
 */
@Directive({ selector: 'ng-template[uiTableCell]' })
export class TableCellDirective<T = unknown> {
  /** `key` de la columna que usa esta plantilla. */
  readonly key = input.required<string>({ alias: 'uiTableCell' });
  readonly template = inject<TemplateRef<UiTableCellContext<T>>>(TemplateRef);
}
