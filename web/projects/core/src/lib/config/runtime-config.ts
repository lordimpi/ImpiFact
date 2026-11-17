import { HttpBackend, HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/**
 * Configuración en tiempo de ejecución del panel, leída de `config.json` al arrancar. El mismo build sirve para
 * desarrollo, staging y producción: cada ambiente monta su propio `config.json` (sección 13 de la arquitectura).
 */
export interface RuntimeConfig {
  /** URL base del Api, sin barra final. Vacía: rutas relativas al panel (mismo origen). */
  apiBaseUrl: string;
  /** Minutos sin actividad para avisar y para cerrar la sesión. */
  inactivity: { warnMinutes: number; logoutMinutes: number };
  /** Indicadores de *features* (funciones que se encienden o apagan por ambiente). */
  features: Readonly<Record<string, boolean>>;
}

export const DEFAULT_RUNTIME_CONFIG: RuntimeConfig = {
  apiBaseUrl: '',
  inactivity: { warnMinutes: 25, logoutMinutes: 30 },
  features: {},
};

/** Ruta del archivo, relativa al `<base href>` del panel (lo publica la carpeta `public/`). */
export const RUNTIME_CONFIG_URL = 'config.json';

/**
 * Lee `config.json` una sola vez y lo deja en un signal. Usa `HttpBackend` directamente para no pasar por los
 * interceptores: todavía no hay URL del Api ni sesión cuando se lee.
 */
@Injectable({ providedIn: 'root' })
export class RuntimeConfigService {
  private readonly http = new HttpClient(inject(HttpBackend));
  private readonly state = signal<RuntimeConfig>(DEFAULT_RUNTIME_CONFIG);
  private loading?: Promise<RuntimeConfig>;

  /** Configuración vigente; vale los valores por defecto hasta que termina `load()`. */
  readonly config = this.state.asReadonly();

  load(): Promise<RuntimeConfig> {
    this.loading ??= firstValueFrom(this.http.get<unknown>(RUNTIME_CONFIG_URL)).then(
      (raw) => this.apply(normalize(raw)),
      (error: unknown) => {
        // Sin configuración el panel arranca con rutas relativas; se deja el rastro en la consola para soporte.
        console.error(
          `No se pudo leer ${RUNTIME_CONFIG_URL}; se usan los valores por defecto.`,
          error,
        );
        return this.apply(DEFAULT_RUNTIME_CONFIG);
      },
    );
    return this.loading;
  }

  isEnabled(feature: string): boolean {
    return this.state().features[feature] === true;
  }

  private apply(config: RuntimeConfig): RuntimeConfig {
    this.state.set(config);
    return config;
  }
}

function normalize(raw: unknown): RuntimeConfig {
  const source = isRecord(raw) ? raw : {};
  const apiBaseUrl =
    typeof source['apiBaseUrl'] === 'string' ? source['apiBaseUrl'].trim().replace(/\/+$/, '') : '';
  return {
    apiBaseUrl,
    inactivity: normalizeInactivity(source['inactivity']),
    features: normalizeFeatures(source['features']),
  };
}

function normalizeInactivity(raw: unknown): RuntimeConfig['inactivity'] {
  if (!isRecord(raw)) {
    return DEFAULT_RUNTIME_CONFIG.inactivity;
  }
  const warn = raw['warnMinutes'];
  const logout = raw['logoutMinutes'];
  // El aviso tiene que llegar antes del cierre; si no, se descartan los dos valores.
  const valid = typeof warn === 'number' && typeof logout === 'number' && warn > 0 && logout > warn;
  return valid ? { warnMinutes: warn, logoutMinutes: logout } : DEFAULT_RUNTIME_CONFIG.inactivity;
}

function normalizeFeatures(raw: unknown): Readonly<Record<string, boolean>> {
  if (!isRecord(raw)) {
    return {};
  }
  return Object.fromEntries(
    Object.entries(raw).filter(
      (entry): entry is [string, boolean] => typeof entry[1] === 'boolean',
    ),
  );
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
