import { render, screen } from '@testing-library/angular';
import { StatComponent } from './stat.component';

describe('StatComponent', () => {
  it('pairs the label with its value as a description list', async () => {
    await render(StatComponent, { inputs: { label: 'Facturas emitidas', value: '1.284' } });

    expect(screen.getByRole('term').textContent?.trim()).toBe('Facturas emitidas');
    expect(screen.getByRole('definition').textContent?.trim()).toBe('1.284');
  });

  it('shows the hint and the trend with its tone', async () => {
    const { container } = await render(StatComponent, {
      inputs: {
        label: 'Rechazadas',
        value: '3',
        hint: 'Este mes',
        trend: '+2 frente al mes anterior',
        trendTone: 'negative',
      },
    });

    expect(screen.getByText('Este mes')).toBeTruthy();
    const trend = screen.getByText('+2 frente al mes anterior');
    expect(trend.closest('.ui-stat__trend')?.classList).toContain('ui-stat__trend--negative');
    expect(container.querySelectorAll('dd')).toHaveLength(3);
  });

  it('omits the hint and the trend when they are not given', async () => {
    const { container } = await render(StatComponent, {
      inputs: { label: 'Clientes', value: '42' },
    });

    expect(container.querySelectorAll('dd')).toHaveLength(1);
  });
});
