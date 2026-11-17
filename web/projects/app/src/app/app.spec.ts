import { provideRouter } from '@angular/router';
import { Title } from '@angular/platform-browser';
import { TestBed } from '@angular/core/testing';
import { Translation, TranslocoLoader, provideTransloco } from '@jsverse/transloco';
import { ToastStore } from '@impifact/core';
import { fireEvent, render, screen, within } from '@testing-library/angular';
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

  it('renders the core toasts with the support code and lets the user close them', async () => {
    await renderShell();
    await screen.findByText('ImpiFact');
    const toasts = TestBed.inject(ToastStore);

    toasts.show({ kind: 'error', text: 'No se pudo guardar', correlationId: 'abc-123' });
    const toast = (await screen.findByText('No se pudo guardar')).closest('li') as HTMLElement;
    expect(within(toast).getByText('abc-123')).toBeTruthy();

    fireEvent.click(within(toast).getByRole('button', { name: 'Cerrar' }));
    expect(toasts.toasts()).toEqual([]);
  });

  it('copies the support code and confirms it', async () => {
    const writeText = vi.fn(async () => undefined);
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } });
    await renderShell();
    await screen.findByText('ImpiFact');

    TestBed.inject(ToastStore).show({ kind: 'error', text: 'Falló', correlationId: 'abc-123' });
    fireEvent.click(await screen.findByRole('button', { name: 'Copiar código' }));

    expect(writeText).toHaveBeenCalledWith('abc-123');
    expect(await screen.findByText('Código copiado.')).toBeTruthy();
  });

  it('shows the home page on the root route', async () => {
    const { navigate } = await renderShell();
    await navigate('/');

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Panel de ImpiFact' }),
    ).toBeTruthy();
  });
});
