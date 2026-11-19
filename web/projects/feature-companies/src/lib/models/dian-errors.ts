import { AppError } from '@impifact/core';

/*
 * Textos de los errores que el Api devuelve cuando llama a la DIAN (Dirección de Impuestos y Aduanas Nacionales):
 * caída del servicio, certificado faltante o vencido y rechazo de la DIAN. El resto muestra el mensaje del Api.
 */

type Translate = (key: string, params?: Record<string, unknown>) => string;

const SERVICE_UNAVAILABLE = 503;

export function dianErrorText(error: unknown, text: Translate): string {
  if (!(error instanceof AppError)) {
    return text('common.errors.unexpected');
  }
  if (error.status === SERVICE_UNAVAILABLE || error.code === 'dian.unavailable') {
    return text('common.errors.dianUnavailable');
  }
  if (error.code === 'certificate.not_found' || error.code === 'certificate.missing') {
    return text('common.errors.noCertificate');
  }
  if (error.code === 'certificate.expired') {
    return text('common.errors.certificateExpired');
  }
  const detail = Object.values(error.fieldErrors)[0]?.[0] ?? error.message;
  return error.code === 'dian.fault'
    ? text('resolutions.ranges.dianMessage', { message: detail })
    : detail;
}
