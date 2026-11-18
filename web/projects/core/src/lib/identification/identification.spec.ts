import {
  NIT_IDENTIFICATION_TYPE_ID,
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
