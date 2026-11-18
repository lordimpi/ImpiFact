import { Injector, runInInjectionContext, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import {
  NIT_IDENTIFICATION_TYPE_ID,
  syncCheckDigit,
  digitsOnly,
  isNitType,
  isNumericIdentificationType,
  nitCheckDigit,
} from './identification';

describe('nitCheckDigit', () => {
  it.each([
    ['800197268', '4'],
    ['900123456', '8'],
    ['860002964', '4'],
    ['1', '8'],
    ['12', '9'],
    ['4', '1'],
    ['0', '0'],
    ['900000000', '5'],
    ['999999999999999', '2'],
  ])('computes the DIAN modulo 11 check digit of %s', (nit, dv) => {
    expect(nitCheckDigit(nit)).toBe(dv);
  });

  it.each(['', '90012345a', '9001-2345', '1234567890123456', ' 900123456'])(
    'has no check digit for %j, which is not 1 to 15 digits',
    (nit) => {
      expect(nitCheckDigit(nit)).toBeNull();
    },
  );
});

describe('digitsOnly', () => {
  it('keeps only the digits of pasted or typed text', () => {
    expect(digitsOnly('900.123.456-8')).toBe('9001234568');
    expect(digitsOnly('(602) 820 1234')).toBe('6028201234');
    expect(digitsOnly('abc')).toBe('');
  });
});

describe('identification types', () => {
  it('restricts NIT and the Colombian cédula-like documents to digits, not the passport', () => {
    expect(isNumericIdentificationType(String(NIT_IDENTIFICATION_TYPE_ID))).toBe(true);
    expect(isNumericIdentificationType('3')).toBe(true);
    expect(isNumericIdentificationType('7')).toBe(false);
    expect(isNumericIdentificationType('9')).toBe(false);
    expect(isNumericIdentificationType('')).toBe(false);
  });

  it('knows the NIT type by its seeded id', () => {
    expect(isNitType('6')).toBe(true);
    expect(isNitType('3')).toBe(false);
  });
});

describe('syncCheckDigit', () => {
  it('keeps the check digit of the model in step with its NIT', () => {
    const model = signal({ type: '6', nit: '', dv: '' });
    runInInjectionContext(TestBed.inject(Injector), () =>
      syncCheckDigit(model, (current) => (current.type === '6' ? current.nit : null)),
    );

    model.update((current) => ({ ...current, nit: '900123456' }));
    TestBed.tick();
    expect(model().dv).toBe('8');

    model.update((current) => ({ ...current, nit: '90012345x' }));
    TestBed.tick();
    expect(model().dv).toBe('');

    model.set({ type: '3', nit: '900123456', dv: '8' });
    TestBed.tick();
    expect(model().dv).toBe('');
  });
});
