import { render, screen } from '@testing-library/angular';
import { MeterComponent } from './meter.component';

describe('MeterComponent', () => {
  it('exposes the value as an accessible meter with its own text', async () => {
    const { container } = await render(MeterComponent, {
      inputs: { label: 'Facturas', value: 40, max: 100, valueText: '40 de 100 usados' },
    });

    const meter = screen.getByRole('meter', { name: 'Facturas' });
    expect(meter.getAttribute('aria-valuenow')).toBe('40');
    expect(meter.getAttribute('aria-valuemax')).toBe('100');
    expect(meter.getAttribute('aria-valuetext')).toBe('40 de 100 usados');
    expect(screen.getByText('40 de 100 usados')).toBeTruthy();
    const fill = container.querySelector<HTMLElement>('.ui-meter__fill');
    expect(fill?.style.inlineSize).toBe('40%');
  });

  it('caps the bar at the maximum and marks the near-limit level', async () => {
    const { container } = await render(MeterComponent, {
      inputs: { label: 'Facturas', value: 130, max: 100, valueText: 'Sin cupo' },
    });

    expect(container.querySelector<HTMLElement>('.ui-meter__fill')?.style.inlineSize).toBe('100%');
    expect(container.querySelector('.ui-meter--high')).toBeTruthy();
  });

  it('shows only the text when there is no maximum', async () => {
    const { container } = await render(MeterComponent, {
      inputs: { label: 'Nómina', value: 12, max: null, valueText: '12 usados, sin límite' },
    });

    expect(screen.queryByRole('meter')).toBeNull();
    expect(screen.getByText('12 usados, sin límite')).toBeTruthy();
    expect(container.querySelector('.ui-meter__fill')).toBeNull();
  });
});
