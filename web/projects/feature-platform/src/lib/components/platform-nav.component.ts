import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

/** Enlaces entre las secciones de la plataforma (planes y asignación de planes). */
@Component({
  selector: 'feat-platform-nav',
  imports: [RouterLink, RouterLinkActive, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nav class="platform-nav" [attr.aria-label]="'platform.nav.label' | transloco">
      <a
        routerLink="/plataforma/planes"
        routerLinkActive="is-active"
        ariaCurrentWhenActive="page"
        >{{ 'platform.nav.plans' | transloco }}</a
      >
      <a
        routerLink="/plataforma/empresas"
        routerLinkActive="is-active"
        ariaCurrentWhenActive="page"
        >{{ 'platform.nav.companies' | transloco }}</a
      >
    </nav>
  `,
  styles: `
    .platform-nav {
      display: flex;
      flex-wrap: wrap;
      gap: var(--if-space-3);
      padding-block-end: var(--if-space-2);
      border-block-end: 1px solid var(--if-color-border);
    }
    .platform-nav a {
      color: var(--if-color-primary);
    }
    .platform-nav .is-active {
      font-weight: var(--if-font-weight-bold);
    }
  `,
})
export class PlatformNavComponent {}
