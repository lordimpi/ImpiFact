import { DOCUMENT } from '@angular/common';
import { inject } from '@angular/core';
import {
  Observable,
  concat,
  defer,
  distinctUntilChanged,
  filter,
  fromEvent,
  map,
  of,
  startWith,
  switchMap,
  take,
  timer,
} from 'rxjs';

/*
 * Seguimiento del estado de la factura de prueba en la DIAN (sección 9 de la arquitectura del frontend): se vuelve a
 * pedir con retroceso (2 s, 4 s, 8 s… hasta 30 s) mientras siga en proceso, nunca con la pestaña oculta. Es la misma
 * regla del detalle de documentos de feature-documents; un feature no importa a otro, por eso va aquí también.
 */

const FIRST_DELAY_MS = 2_000;
const MAX_DELAY_MS = 30_000;

export function pollingDelay(attempt: number): number {
  return Math.min(FIRST_DELAY_MS * 2 ** attempt, MAX_DELAY_MS);
}

/** Emite cada respuesta de `fetch` y vuelve a llamarlo mientras `isPending` sea verdadero. */
export function pollWhilePending<T>(
  fetch: () => Observable<T>,
  isPending: (value: T) => boolean,
  visible: Observable<boolean>,
): Observable<T> {
  const step = (attempt: number): Observable<T> =>
    fetch().pipe(
      switchMap((value) =>
        isPending(value)
          ? concat(
              of(value),
              timer(pollingDelay(attempt)).pipe(
                switchMap(() => visible.pipe(filter(Boolean), take(1))),
                switchMap(() => step(attempt + 1)),
              ),
            )
          : of(value),
      ),
    );
  return defer(() => step(0));
}

/** Si la pestaña está visible, con cada cambio. Se llama en un contexto de inyección. */
export function pageVisibility(): Observable<boolean> {
  const document = inject(DOCUMENT);
  const isVisible = () => document.visibilityState !== 'hidden';
  return fromEvent(document, 'visibilitychange').pipe(
    map(isVisible),
    startWith(isVisible()),
    distinctUntilChanged(),
  );
}
