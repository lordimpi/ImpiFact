import { signal } from '@angular/core';
import { InactivityMonitor } from '@impifact/core';
import { fireEvent, render, screen, waitFor } from '@testing-library/angular';
import { fakeSession, provideAuthTestingI18n } from '../testing/auth-testing';
import { InactivityWarningComponent } from './inactivity-warning.component';

describe('InactivityWarningComponent after a logout from the dialog', () => {
  it('closing a later warning (X / Escape) still counts as staying connected', async () => {
    const fake = fakeSession({ authenticated: true });
    const monitor = {
      warning: signal(true),
      minutesLeft: signal(5),
      stayConnected: vi.fn(() => monitor.warning.set(false)),
    };
    const { fixture } = await render(InactivityWarningComponent, {
      providers: [
        provideAuthTestingI18n(),
        fake.provider,
        { provide: InactivityMonitor, useValue: monitor },
      ],
    });

    // First warning: the person picks "Cerrar sesión"; the real monitor stops and hides the warning.
    fireEvent.click(await screen.findByRole('button', { name: 'Cerrar sesión' }));
    expect(fake.session.logout).toHaveBeenCalledOnce();
    monitor.warning.set(false);
    fixture.detectChanges();
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());

    // Same SPA (the shell keeps this component mounted): log in again, 25 minutes later a new warning.
    monitor.warning.set(true);
    fixture.detectChanges();
    fireEvent.click(
      await screen.findByRole('button', { name: 'Cerrar el aviso y seguir conectado' }),
    );

    await waitFor(() => expect(monitor.stayConnected).toHaveBeenCalledOnce());
  });
});
