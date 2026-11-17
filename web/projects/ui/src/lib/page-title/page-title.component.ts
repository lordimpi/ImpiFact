import { ChangeDetectionStrategy, Component } from '@angular/core';

/**
 * Título principal de una página. Componente de presentación: recibe el texto ya traducido por proyección.
 * Marcador inicial; la tarea 4 del backlog agrega los componentes base de ui.
 */
@Component({
  selector: 'ui-page-title',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<h1 class="ui-page-title"><ng-content /></h1>`,
  styles: `
    .ui-page-title {
      margin: 0 0 var(--if-space-3);
      font-size: var(--if-font-size-lg);
      color: var(--if-color-text);
    }
  `,
})
export class PageTitleComponent {}
