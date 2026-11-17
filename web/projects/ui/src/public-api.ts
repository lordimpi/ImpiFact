/*
 * API pública de la librería ui: componentes de presentación sin lógica de negocio.
 * Envuelven a Optimus UI (docs/spikes/optimus-ui.md) para poder cambiarlo sin tocar los features.
 * Los design tokens (variables de diseño) están en styles/tokens.css.
 */
export * from './lib/button/button.component';
export * from './lib/dialog/dialog.component';
export * from './lib/field/field.component';
export * from './lib/field/input.directive';
export * from './lib/page-title/page-title.component';
export * from './lib/paginator/pagination';
export * from './lib/paginator/paginator.component';
export * from './lib/states/empty-state.component';
export * from './lib/states/error-state.component';
export * from './lib/states/loading.component';
export * from './lib/table/table-cell.directive';
export * from './lib/table/table.component';
export * from './lib/table/table.models';
export * from './lib/theme/provide-ui-theme';
