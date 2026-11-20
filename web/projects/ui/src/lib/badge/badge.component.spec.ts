import { render, screen } from '@testing-library/angular';
import { BadgeComponent, UiBadgeTone } from './badge.component';

describe('BadgeComponent', () => {
  it('shows the projected text with the neutral tone by default', async () => {
    await render(`<ui-badge>Borrador</ui-badge>`, { imports: [BadgeComponent] });

    const badge = screen.getByText('Borrador').closest('ui-badge');
    expect(badge?.getAttribute('data-tone')).toBe('neutral');
  });

  it.each<UiBadgeTone>(['info', 'success', 'warning', 'danger'])(
    'applies the %s tone',
    async (tone) => {
      await render(`<ui-badge [tone]="tone">Estado</ui-badge>`, {
        imports: [BadgeComponent],
        componentProperties: { tone },
      });

      expect(screen.getByText('Estado').closest('ui-badge')?.getAttribute('data-tone')).toBe(tone);
    },
  );
});
