import { provideRouter } from '@angular/router';
import { Title } from '@angular/platform-browser';
import { TestBed } from '@angular/core/testing';
import { Translation, TranslocoLoader, provideTransloco } from '@jsverse/transloco';
import { render, screen, within } from '@testing-library/angular';
import { of } from 'rxjs';
import esCO from '../../public/i18n/es-CO.json';
import { App } from './app';
import { routes } from './app.routes';

// Cargador en memoria con el archivo real de textos es-CO, para no depender de HTTP en la prueba.
class InMemoryLoader implements TranslocoLoader {
  getTranslation() {
    return of(esCO as Translation);
  }
}

describe('App shell', () => {
  async function renderShell() {
    return render(App, {
      providers: [
        provideRouter(routes),
        provideTransloco({
          config: { availableLangs: ['es-CO'], defaultLang: 'es-CO' },
          loader: InMemoryLoader,
        }),
      ],
    });
  }

  it('renders the layout with texts from the es-CO translations', async () => {
    await renderShell();

    expect(await screen.findByText('ImpiFact')).toBeTruthy();
    const nav = screen.getByRole('navigation', { name: 'Navegación principal' });
    expect(within(nav).getByRole('link', { name: 'Inicio' })).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Saltar al contenido' })).toBeTruthy();
    expect(screen.getByRole('main')).toBeTruthy();
  });

  it('sets the document title from the translations', async () => {
    await renderShell();
    await screen.findByText('ImpiFact');

    expect(TestBed.inject(Title).getTitle()).toBe('ImpiFact · Panel');
  });

  it('shows the home page on the root route', async () => {
    const { navigate } = await renderShell();
    await navigate('/');

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Panel de ImpiFact' }),
    ).toBeTruthy();
  });
});
