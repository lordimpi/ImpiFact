import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { DEFAULT_LANG, provideI18n } from './provide-i18n';

describe('provideI18n', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideI18n()],
    });
  });

  it('uses es-CO as the active language and loads its texts from i18n/es-CO.json', async () => {
    const transloco = TestBed.inject(TranslocoService);
    const http = TestBed.inject(HttpTestingController);

    expect(DEFAULT_LANG).toBe('es-CO');
    expect(transloco.getActiveLang()).toBe('es-CO');

    const loaded = firstValueFrom(transloco.load('es-CO'));
    http.expectOne('i18n/es-CO.json').flush({ greeting: 'Hola' });
    await loaded;

    expect(transloco.translate('greeting')).toBe('Hola');
    http.verify();
  });
});
