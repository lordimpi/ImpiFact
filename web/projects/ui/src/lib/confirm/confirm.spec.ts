import { TestBed } from '@angular/core/testing';
import { fireEvent, screen, waitFor, within } from '@testing-library/angular';
import { UiConfirm, UiConfirmOptions } from './confirm';

const OPTIONS: UiConfirmOptions = {
  header: 'Cambios sin guardar',
  message: 'Hay cambios sin guardar. ¿Desea salir de todos modos?',
  acceptLabel: 'Salir sin guardar',
  rejectLabel: 'Seguir editando',
};

async function ask() {
  const answer = TestBed.inject(UiConfirm).ask(OPTIONS);
  // The first question loads the dialog chunk, which can take longer than the default wait.
  const dialog = await screen.findByRole(
    'alertdialog',
    { name: 'Cambios sin guardar' },
    { timeout: 4000 },
  );
  return { answer, dialog };
}

describe('UiConfirm', () => {
  afterEach(() => {
    // The dialog lives under <body>, outside any test fixture.
    document.body.querySelectorAll('ui-confirm-dialog').forEach((host) => host.remove());
  });

  it('shows the Optimus confirm dialog with the message and both choices, the safe one first', async () => {
    const { dialog } = await ask();

    expect(document.querySelector('p-confirmdialog')).not.toBeNull();
    expect(dialog.classList).toContain('p-confirmdialog');
    expect(screen.getAllByRole('alertdialog')).toEqual([dialog]);
    expect(within(dialog).getByText(OPTIONS.message)).toBeTruthy();
    const buttons = within(dialog)
      .getAllByRole('button')
      .map((button) => button.textContent?.trim());
    // The safe choice comes first: Optimus focuses the first footer button once its opening animation ends (only a
    // real browser runs it, so the E2E test checks the focus).
    expect(buttons).toEqual(['Seguir editando', 'Salir sin guardar']);
  });

  it('resolves true when the person leaves without saving', async () => {
    const { answer, dialog } = await ask();

    within(dialog).getByRole('button', { name: 'Salir sin guardar' }).click();

    await expect(answer).resolves.toBe(true);
  });

  it('resolves false when the person keeps editing', async () => {
    const { answer, dialog } = await ask();

    within(dialog).getByRole('button', { name: 'Seguir editando' }).click();

    await expect(answer).resolves.toBe(false);
  });

  it('resolves false with Escape', async () => {
    const { answer } = await ask();

    fireEvent.keyDown(document, { key: 'Escape', code: 'Escape' });

    await expect(answer).resolves.toBe(false);
  });

  it('reuses the same dialog for the next question', async () => {
    const first = await ask();
    within(first.dialog).getByRole('button', { name: 'Seguir editando' }).click();
    await first.answer;
    await waitFor(() => expect(screen.queryByRole('alertdialog')).toBeNull());

    const second = await ask();
    within(second.dialog).getByRole('button', { name: 'Salir sin guardar' }).click();

    await expect(second.answer).resolves.toBe(true);
    expect(document.body.querySelectorAll('ui-confirm-dialog')).toHaveLength(1);
  });
});
