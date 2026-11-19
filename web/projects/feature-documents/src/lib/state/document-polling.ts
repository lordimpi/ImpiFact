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
 * Seguimiento del estado de un documento en la DIAN (sección 9 de la arquitectura del frontend): se vuelve a pedir
 * con retroceso (2 s, 4 s, 8 s… hasta 30 s) mientras siga en proceso, nunca con la pestaña oculta, y se detiene con
 * un estado final o cuando la página se cierra (quien se suscribe usa `takeUntilDestroyed`).
 */

const FIRST_DELAY_MS = 2_000;
const MAX_DELAY_MS = 30_000;

/** Espera antes de la consulta número `attempt + 1` (desde 0). */
export function pollingDelay(attempt: number): number {
  return Math.min(FIRST_DELAY_MS * 2 ** attempt, MAX_DELAY_MS);
}

/**
 * Emite cada respuesta de `fetch` y vuelve a llamarlo mientras `isPending` sea verdadero. Si la pestaña está oculta
 * cuando vence la espera, aguarda a que vuelva a estar visible antes de consultar.
 */
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

/** Si la pestaña está visible, con cada cambio (`visibilitychange`). Se llama en un contexto de inyección. */
export function pageVisibility(): Observable<boolean> {
  const document = inject(DOCUMENT);
  const isVisible = () => document.visibilityState !== 'hidden';
  return fromEvent(document, 'visibilitychange').pipe(
    map(isVisible),
    startWith(isVisible()),
    distinctUntilChanged(),
  );
}
