import { Pipe, PipeTransform } from '@angular/core';

/*
 * Date and time of an API instant (ISO 8601 with offset) in Colombia, as people read it there: «21/11/2026, 10:05».
 * The DIAN counts its deadlines in Colombian days, so the panel never shows the browser's time zone here.
 */
const FORMAT = new Intl.DateTimeFormat('es-CO', {
  day: '2-digit',
  month: '2-digit',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
  hourCycle: 'h23',
  timeZone: 'America/Bogota',
});

export function formatColombiaTime(value: string | null | undefined): string {
  if (!value) {
    return '';
  }
  const instant = new Date(value);
  return Number.isNaN(instant.getTime()) ? value : FORMAT.format(instant);
}

@Pipe({ name: 'colombiaTime' })
export class ColombiaTimePipe implements PipeTransform {
  transform(value: string | null | undefined): string {
    return formatColombiaTime(value);
  }
}
