import { signal } from '@angular/core';
import { render, screen, waitFor } from '@testing-library/angular';
import { DialogComponent } from './dialog.component';

async function renderDialog(visible: boolean) {
  const open = signal(visible);
  const closed = vi.fn();
  await render(
    `<ui-dialog header="Anular factura" closeLabel="Cerrar" [(visible)]="open" (closed)="closed()">
      <p>La factura SETP-1 quedará anulada.</p>
      <div uiDialogFooter><button type="button">Confirmar</button></div>
    </ui-dialog>`,
    { imports: [DialogComponent], componentProperties: { open, closed } },
  );
  return { open, closed };
}

describe('DialogComponent', () => {
  it('renders nothing while hidden', async () => {
    await renderDialog(false);

    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('is a modal dialog named by its header with content and footer', async () => {
    await renderDialog(true);

    const dialog = await screen.findByRole('dialog', { name: 'Anular factura' });
    expect(dialog.getAttribute('aria-modal')).toBe('true');
    expect(screen.getByText('La factura SETP-1 quedará anulada.')).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Confirmar' })).toBeTruthy();
  });

  it('closes from its close button, updating the visible model and emitting closed', async () => {
    const { open, closed } = await renderDialog(true);

    (await screen.findByRole('button', { name: 'Cerrar' })).click();

    await waitFor(() => expect(open()).toBe(false));
    await waitFor(() => expect(closed).toHaveBeenCalledTimes(1));
  });
});
