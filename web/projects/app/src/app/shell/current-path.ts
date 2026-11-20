import { Signal, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { filter, map } from 'rxjs';

/** Path (without query or fragment) of the current URL, updated after each navigation. */
export function injectCurrentPath(): Signal<string> {
  const router = inject(Router);
  const pathOf = (url: string) => url.split(/[?#]/)[0] || '/';
  return toSignal(
    router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map((event) => pathOf(event.urlAfterRedirects)),
    ),
    { initialValue: pathOf(router.url) },
  );
}
