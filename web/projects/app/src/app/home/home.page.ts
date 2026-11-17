import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { PageTitleComponent } from '@impifact/ui';

/** Página de inicio provisional; el tablero real (sección 7 de la arquitectura del frontend) llega con su backend. */
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
