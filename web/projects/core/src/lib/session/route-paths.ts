import { InjectionToken } from '@angular/core';

/**
 * Rutas a las que core manda a la persona: inicio de sesión (sin sesión o al cerrarla), inicio (sin el rol pedido)
 * y selección de empresa (sin empresa activa). Coinciden con la sección 7 de la arquitectura; `provideCore` permite
 * cambiarlas.
 */
export interface CoreRoutePaths {
  login: string;
  home: string;
  selectCompany: string;
}

export const DEFAULT_CORE_ROUTE_PATHS: CoreRoutePaths = {
  login: '/login',
  home: '/',
  selectCompany: '/empresas/seleccionar',
};

export const CORE_ROUTE_PATHS = new InjectionToken<CoreRoutePaths>('CORE_ROUTE_PATHS', {
  providedIn: 'root',
  factory: () => DEFAULT_CORE_ROUTE_PATHS,
});
