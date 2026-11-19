import { signal } from '@angular/core';
import { InactivityMonitor } from '@impifact/core';
import { fireEvent, render, screen, waitFor } from '@testing-library/angular';
import { fakeSession, provideAuthTestingI18n } from '../testing/auth-testing';
import { InactivityWarningComponent } from './inactivity-warning.component';

describe('InactivityWarningComponent', () => {
  async function renderWarning(open: boolean) {
    const fake = fakeSession({ authenticated: true });
    const monitor = {
      warning: signal(open),
      minutesLeft: signal(5),
      stayConnected: vi.fn(() => monitor.warning.set(false)),
    };
    await render(InactivityWarningComponent, {
      providers: [
        provideAuthTestingI18n(),
        fake.provider,
        { provide: InactivityMonitor, useValue: monitor },
      ],
    });
    return { monitor, session: fake.session };
  }

  it('stays hidden while there is no warning', async () => {
    await renderWarning(false);

    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('warns with the minutes left and both choices', async () => {
    await renderWarning(true);

    const dialog = await screen.findByRole('dialog', {
      name: 'Su sesión se cerrará por inactividad',
    });
    expect(dialog.textContent).toContain('la sesión se cerrará en 5 minutos');
    expect(screen.getByRole('button', { name: 'Seguir conectado' })).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Cerrar sesión' })).toBeTruthy();
  });

  it('keeps the session when the user chooses to stay connected', async () => {
    const { monitor, session } = await renderWarning(true);

    fireEvent.click(await screen.findByRole('button', { name: 'Seguir conectado' }));

    expect(monitor.stayConnected).toHaveBeenCalledOnce();
    expect(session.logout).not.toHaveBeenCalled();
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  });

  it('closes the session when the user chooses to', async () => {
    const { monitor, session } = await renderWarning(true);

    fireEvent.click(await screen.findByRole('button', { name: 'Cerrar sesión' }));

    expect(session.logout).toHaveBeenCalledExactlyOnceWith();
    expect(monitor.stayConnected).not.toHaveBeenCalled();
  });

  it('counts closing the dialog as staying connected', async () => {
    const { monitor } = await renderWarning(true);

    fireEvent.click(
      await screen.findByRole('button', { name: 'Cerrar el aviso y seguir conectado' }),
    );

    await waitFor(() => expect(monitor.stayConnected).toHaveBeenCalledOnce());
  });
});
