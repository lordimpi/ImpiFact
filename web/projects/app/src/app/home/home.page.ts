import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { PageTitleComponent } from '@impifact/ui';

/**
 * Página de inicio para quien no ve el tablero (cliente, plataforma): el propietario y el vendedor abren en `/` el
 * tablero de feature-dashboard (sección 7 de la arquitectura del frontend).
 */
@Component({
  selector: 'app-home-page',
  imports: [PageTitleComponent, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-page-title>{{ 'home.title' | transloco }}</ui-page-title>
    <p>{{ 'home.description' | transloco }}</p>
  `,
})
export class HomePage {}
