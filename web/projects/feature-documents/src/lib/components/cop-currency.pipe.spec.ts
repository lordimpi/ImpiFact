import { formatCop, formatPercent } from './cop-currency.pipe';

describe('number formatting for documents', () => {
  it('shows pesos with two decimals and es-CO separators', () => {
    expect(formatCop('1785000.00')).toBe(formatCop(1785000));
    expect(formatCop('1785000.00')).toContain('1.785.000,00');
  });

  it('shows a tax rate without trailing zeros and with a decimal comma', () => {
    expect(formatPercent('19.00')).toBe('19');
    expect(formatPercent(5)).toBe('5');
    expect(formatPercent('8.50')).toBe('8,5');
    expect(formatPercent(null)).toBe('');
    expect(formatPercent('abc')).toBe('abc');
  });
});
