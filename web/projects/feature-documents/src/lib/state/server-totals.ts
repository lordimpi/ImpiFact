import { HttpContext } from '@angular/common/http';
import { Signal, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import {
  Api,
  CalculateDocumentTotalsRequest,
  DocumentTotalsCalculation,
  calculateDocumentTotals,
} from '@impifact/api';
import { SKIP_ERROR_TOAST } from '@impifact/core';
import {
  Observable,
  Subject,
  catchError,
  debounceTime,
  distinctUntilChanged,
  map,
  merge,
  of,
  startWith,
  switchMap,
} from 'rxjs';
import { TotalsState } from '../components/totals-panel.component';

/** Pausa tras el último cambio de las líneas antes de pedir el cálculo. */
export const CALCULATE_DEBOUNCE_MS = 300;

/** Resultado del último cálculo pedido al servidor. */
type Calculation =
  | { status: 'empty' }
  | { status: 'loading'; key: string }
  | { status: 'error'; key: string }
  | { status: 'ready'; key: string; result: DocumentTotalsCalculation };

export interface ServerTotals {
  /** Estado del panel: «calculando» también mientras espera la pausa tras un cambio. */
  state: Signal<TotalsState>;
  /** Cálculo del servidor si corresponde a las líneas de este momento; si no, `null`. */
  calculated: Signal<DocumentTotalsCalculation | null>;
  /** Impuestos agrupados del cálculo, para el panel. */
  taxes: Signal<DocumentTotalsCalculation['tax_totals'][number]['tax_subtotals']>;
  /** Repite el cálculo vigente («Calcular de nuevo»). */
  retry(): void;
}

function keyOf(request: CalculateDocumentTotalsRequest | null): string {
  return request === null ? '' : JSON.stringify(request);
}

/**
 * Totales e impuestos de un documento calculados siempre por el Api (`calculateDocumentTotals`, con las reglas de
 * redondeo de la DIAN): cada cambio en `request` pide un cálculo nuevo tras una pausa corta y una respuesta vieja
 * nunca se muestra como vigente. La comparten la factura y las notas. Debe llamarse en un contexto de inyección.
 */
export function serverTotals(request: Signal<CalculateDocumentTotalsRequest | null>): ServerTotals {
  const api = inject(Api);
  const key = computed(() => keyOf(request()));
  const retry = new Subject<void>();
  const calculation = signal<Calculation>({ status: 'empty' });

  const calculate = (body: CalculateDocumentTotalsRequest | null): Observable<Calculation> => {
    if (body === null) {
      return of({ status: 'empty' });
    }
    const current = keyOf(body);
    return api
      .invoke(calculateDocumentTotals, { body }, new HttpContext().set(SKIP_ERROR_TOAST, true))
      .pipe(
        map((result): Calculation => ({ status: 'ready', key: current, result })),
        catchError(() => of<Calculation>({ status: 'error', key: current })),
        startWith<Calculation>({ status: 'loading', key: current }),
      );
  };

  merge(
    toObservable(request).pipe(
      debounceTime(CALCULATE_DEBOUNCE_MS),
      distinctUntilChanged((previous, next) => keyOf(previous) === keyOf(next)),
    ),
    retry.pipe(map(() => request())),
  )
    .pipe(switchMap(calculate), takeUntilDestroyed())
    .subscribe((next) => calculation.set(next));

  const state = computed<TotalsState>(() => {
    const current = calculation();
    if (request() === null) return 'empty';
    if (current.status === 'ready' && current.key === key()) return 'ready';
    if (current.status === 'error' && current.key === key()) return 'error';
    return 'loading';
  });
  const calculated = computed(() => {
    const current = calculation();
    return state() === 'ready' && current.status === 'ready' ? current.result : null;
  });

  return {
    state,
    calculated,
    taxes: computed(() => (calculated()?.tax_totals ?? []).flatMap((total) => total.tax_subtotals)),
    retry: () => retry.next(),
  };
}
