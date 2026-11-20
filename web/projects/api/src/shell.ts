/*
 * Punto de entrada `@impifact/api/shell`: solo lo del cliente generado que `core` usa y que, por eso, viaja en el
 * paquete inicial (sesión, empresas del usuario, avisos, catálogos, configuración). Es código escrito a mano.
 *
 * Por qué existe: con división de código, esbuild asigna cada archivo a los chunks de todos los puntos de entrada que lo
 * alcanzan por sus `import`, se use o no lo importado. El barril `@impifact/api` reexporta todas las funciones
 * generadas, así que si `core` (que está en el inicial) lo importa, toda función que algún feature perezoso use termina
 * en un chunk que carga el inicial. `core` importa de aquí y los features del barril: el resto de funciones queda en
 * chunks perezosos. Si `core` necesita otra función o modelo, se agrega aquí, no se importa del barril.
 */
export { Api } from './generated/api';
export { ApiConfiguration } from './generated/api-configuration';
export { listCatalog } from './generated/fn/catalogos/list-catalog';
export type { ListCatalog$Params } from './generated/fn/catalogos/list-catalog';
export { listMyCompanies } from './generated/fn/identidad/list-my-companies';
export { login } from './generated/fn/identidad/login';
export { logout } from './generated/fn/identidad/logout';
export { refreshSession } from './generated/fn/identidad/refresh-session';
export { listNotifications } from './generated/fn/notificaciones/list-notifications';
export { markAllNotificationsRead } from './generated/fn/notificaciones/mark-all-notifications-read';
export { markNotificationRead } from './generated/fn/notificaciones/mark-notification-read';
export type { CatalogItemResponse } from './generated/models/catalog-item-response';
export type { CatalogListResponse } from './generated/models/catalog-list-response';
export type { MyCompany } from './generated/models/my-company';
export type { NotificationResponse } from './generated/models/notification-response';
export type { PanelSessionTokens } from './generated/models/panel-session-tokens';
export { provideApiRootUrl } from './lib/api-root-url';
