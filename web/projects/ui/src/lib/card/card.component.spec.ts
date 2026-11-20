import { render, screen } from '@testing-library/angular';
import { CardActionsDirective, CardComponent, CardFooterDirective } from './card.component';

const imports = [CardComponent, CardActionsDirective, CardFooterDirective];

describe('CardComponent', () => {
  it('renders the title as a level 2 heading by default, with its subtitle and content', async () => {
    await render(
      `<ui-card title="Facturas del mes" subtitle="Noviembre"><p>Contenido</p></ui-card>`,
      { imports },
    );

    expect(screen.getByRole('heading', { level: 2, name: 'Facturas del mes' })).toBeTruthy();
    expect(screen.getByText('Noviembre')).toBeTruthy();
    expect(screen.getByText('Contenido')).toBeTruthy();
  });

  it('uses the requested heading level', async () => {
    await render(`<ui-card title="Clientes" [headingLevel]="3" />`, { imports });

    expect(screen.getByRole('heading', { level: 3, name: 'Clientes' })).toBeTruthy();
    expect(screen.queryByRole('heading', { level: 2 })).toBeNull();
  });

  it('projects header actions and the footer', async () => {
    await render(
      `<ui-card title="Clientes">
        <button uiCardActions type="button">Nuevo</button>
        <p>Lista</p>
        <p uiCardFooter>Pie</p>
      </ui-card>`,
      { imports },
    );

    const header = screen.getByRole('heading', { name: 'Clientes' }).closest('.ui-card__header');
    expect(header?.contains(screen.getByRole('button', { name: 'Nuevo' }))).toBe(true);
    expect(screen.getByText('Pie').closest('.ui-card__footer')).toBeTruthy();
    expect(screen.getByText('Lista').closest('.ui-card__body')).toBeTruthy();
  });

  it('shows header actions even without a title', async () => {
    const { container } = await render(
      `<ui-card><button uiCardActions type="button">Exportar</button></ui-card>`,
      { imports },
    );

    expect(container.querySelector('.ui-card__header')).toBeTruthy();
    expect(screen.queryByRole('heading')).toBeNull();
  });

  it('has no header or footer when nothing is given for them', async () => {
    const { container } = await render(`<ui-card><p>Solo contenido</p></ui-card>`, { imports });

    expect(container.querySelector('.ui-card__header')).toBeNull();
    expect(container.querySelector('.ui-card__footer')).toBeNull();
    expect(screen.queryByRole('heading')).toBeNull();
  });

  it('removes the body padding with padding="none" (for flush tables)', async () => {
    const { container } = await render(`<ui-card padding="none"><p>Tabla</p></ui-card>`, {
      imports,
    });

    expect(container.querySelector('.ui-card')?.classList).toContain('ui-card--padding-none');
  });
});
