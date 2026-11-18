import { Pipe, PipeTransform } from '@angular/core';

/*
 * Formato de pesos colombianos (COP) con 2 decimales y los separadores de es-CO. Es solo para mostrar: el valor
 * llega ya calculado por el Api (como número o texto decimal) y nunca se opera en el navegador.
 */
const COP = new Intl.NumberFormat('es-CO', {
  style: 'currency',
  currency: 'COP',
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

export function formatCop(value: number | string | null | undefined): string {
  if (value === null || value === undefined || value === '') {
    return '';
  }
  const amount = typeof value === 'number' ? value : Number(value);
  return Number.isFinite(amount) ? COP.format(amount) : String(value);
}

@Pipe({ name: 'cop' })
export class CopCurrencyPipe implements PipeTransform {
  transform(value: number | string | null | undefined): string {
    return formatCop(value);
  }
}
