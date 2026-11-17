import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Title } from '@angular/platform-browser';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';

/** Shell del panel: encabezado, menú y zona de contenido. El menú por rol llega con la tarea 3 del backlog. */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  constructor() {
    const title = inject(Title);
    inject(TranslocoService)
      .selectTranslate<string>('app.documentTitle')
      .pipe(takeUntilDestroyed())
      .subscribe((text) => title.setTitle(text));
  }
}
