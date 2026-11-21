import { fireEvent, render, screen, within } from '@testing-library/angular';
import { ToastRegionComponent } from './toast-region.component';
import { UiToast, UiToastLabels } from './toast.models';

const labels: UiToastLabels = {
  region: 'Avisos',
  close: 'Cerrar aviso',
  copy: 'Copiar código',
  reference: 'Código de soporte',
};

const toasts: UiToast[] = [
  { id: 1, kind: 'success', text: 'Empresa guardada' },
  { id: 2, kind: 'error', text: 'No se pudo emitir', reference: 'abc-123' },
];

describe('ToastRegionComponent', () => {
  it('keeps both live regions in the page even without toasts', async () => {
    const { container } = await render(ToastRegionComponent, { inputs: { toasts: [], labels } });

    expect(container.querySelector('[aria-live="polite"]')).toBeTruthy();
    expect(container.querySelector('[aria-live="assertive"]')).toBeTruthy();
  });

  it('announces errors assertively and the rest politely', async () => {
    const { container } = await render(ToastRegionComponent, { inputs: { toasts, labels } });

    const polite = container.querySelector('[aria-live="polite"]') as HTMLElement;
    const assertive = container.querySelector('[aria-live="assertive"]') as HTMLElement;
    expect(polite.textContent).toContain('Empresa guardada');
    expect(polite.textContent).not.toContain('No se pudo emitir');
    expect(assertive.textContent).toContain('No se pudo emitir');
    expect(assertive.textContent).toContain('Código de soporte');
    expect(assertive.textContent).toContain('abc-123');
  });

  it('emits dismiss and copyReference with the toast data', async () => {
    const dismiss = vi.fn();
    const copy = vi.fn();
    await render(ToastRegionComponent, {
      inputs: { toasts, labels },
      on: { dismiss, copyReference: copy },
    });
    const error = screen.getByText('No se pudo emitir').closest('li') as HTMLElement;

    fireEvent.click(within(error).getByRole('button', { name: 'Copiar código' }));
    fireEvent.click(within(error).getByRole('button', { name: 'Cerrar aviso' }));

    expect(copy).toHaveBeenCalledWith('abc-123');
    expect(dismiss).toHaveBeenCalledWith(2);
  });

  it('closes with an icon button named for screen readers and shows an icon of the kind', async () => {
    await render(ToastRegionComponent, { inputs: { toasts, labels } });
    const success = screen.getByText('Empresa guardada').closest('li') as HTMLElement;

    const close = within(success).getByRole('button', { name: 'Cerrar aviso' });
    // An icon and no visible text: the name comes from aria-label.
    expect(close.textContent?.trim()).toBe('');
    expect(close.querySelector('svg[aria-hidden="true"]')).toBeTruthy();
    expect(success.querySelector('.ui-toast__icon[aria-hidden="true"]')).toBeTruthy();
  });
});
