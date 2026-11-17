import { render, screen } from '@testing-library/angular';
import { PageTitleComponent } from './page-title.component';

describe('PageTitleComponent', () => {
  it('renders the projected text as the level 1 heading', async () => {
    await render('<ui-page-title>Documentos</ui-page-title>', { imports: [PageTitleComponent] });

    expect(screen.getByRole('heading', { level: 1, name: 'Documentos' })).toBeTruthy();
  });
});
