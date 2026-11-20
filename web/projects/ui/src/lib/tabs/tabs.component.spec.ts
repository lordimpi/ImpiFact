import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { screen } from '@testing-library/angular';
import { TabsComponent, UiTab } from './tabs.component';

@Component({
  selector: 'ui-test-tabs-host',
  imports: [TabsComponent],
  template: `<ui-tabs label="Secciones de la empresa" [tabs]="tabs" />`,
})
class TabsHostComponent {
  readonly tabs: UiTab[] = [
    { label: 'Datos', link: '/empresa/datos' },
    { label: 'Resoluciones', link: '/empresa/resoluciones' },
    { label: 'Certificado', link: '/empresa', queryParams: { tab: 'certificado' } },
  ];
}

async function navigate(url: string) {
  const harness = await RouterTestingHarness.create();
  await harness.navigateByUrl(url, TabsHostComponent);
  harness.detectChanges();
  return harness;
}

const link = (name: string) => screen.getByRole('link', { name });

describe('TabsComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'empresa', component: TabsHostComponent },
          { path: 'empresa/datos', component: TabsHostComponent },
          { path: 'empresa/resoluciones', component: TabsHostComponent },
        ]),
      ],
    });
  });

  it('renders a labelled navigation with one link per tab', async () => {
    await navigate('/empresa/datos');

    const nav = screen.getByRole('navigation', { name: 'Secciones de la empresa' });
    const links = Array.from(nav.querySelectorAll('a'));
    expect(links.map((anchor) => anchor.textContent?.trim())).toEqual([
      'Datos',
      'Resoluciones',
      'Certificado',
    ]);
    expect(links[1]?.getAttribute('href')).toBe('/empresa/resoluciones');
    expect(links[2]?.getAttribute('href')).toBe('/empresa?tab=certificado');
  });

  it('marks only the active tab with aria-current="page"', async () => {
    await navigate('/empresa/resoluciones');

    expect(link('Resoluciones').getAttribute('aria-current')).toBe('page');
    expect(link('Datos').getAttribute('aria-current')).toBeNull();
    expect(link('Certificado').getAttribute('aria-current')).toBeNull();
  });

  it('tells apart tabs that differ only by query parameters', async () => {
    await navigate('/empresa?tab=certificado');

    expect(link('Certificado').getAttribute('aria-current')).toBe('page');
    expect(link('Datos').getAttribute('aria-current')).toBeNull();
  });
});
