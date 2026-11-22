import { WritableSignal, signal } from '@angular/core';
import {
  FieldTree,
  applyEach,
  email,
  form,
  maxLength,
  pattern,
  required,
} from '@angular/forms/signals';
import { DocumentTotalsCalculation, IssueSupportDocument$Params } from '@impifact/api';
import {
  CREDIT_PAYMENT_FORM,
  InvoiceLineModel,
  emailRequest,
  PaymentModel,
  emptyLine,
  lineRules,
  linesBody,
  paymentEntry,
  taxEntry,
} from './invoice.form';

/*
 * Formulario del documento soporte en adquisiciones a no obligados a facturar (tipo de documento 11) con Signal
 * Forms: la empresa compra a un proveedor que no factura, así que el tercero es el vendedor (`seller`) y no el
 * cliente. Como en la factura, aquí no se calcula ningún valor fiscal: las líneas van a `calculateDocumentTotals` y
 * el cuerpo lleva los totales del servidor. El CUDS (código único del documento soporte) lo calcula el Api.
 */

/** Tipo de documento del documento soporte en las tablas de la DIAN. */
const SUPPORT_DOCUMENT_TYPE_ID = 11;

/** Tipos de identificación que el Api acepta para el proveedor (`seller.type_document_identification_id`). */
export const SELLER_IDENTIFICATION_TYPES: readonly number[] = [4, 5, 6, 7, 8, 9, 11];

/** Operación: 23 residente (código `10`) o 24 no residente (código `11`). */
export const SUPPORT_OPERATIONS = ['23', '24'] as const;
export const NON_RESIDENT_OPERATION = '24';

/** Forma de generación y transmisión de cada línea: 1 por operación, 2 acumulado semanal. */
export const GENERATION_MODES = ['1', '2'] as const;

export interface SellerModel {
  /** Id del tipo de identificación, como texto de la lista desplegable. */
  identificationTypeId: string;
  identificationNumber: string;
  /** Dígito de verificación (opcional; si llega, el Api lo revisa). */
  dv: string;
  name: string;
  address: string;
  phone: string;
  email: string;
  merchantRegistration: string;
  postalZoneCode: string;
  /** País del proveedor no residente (id del catálogo `countries`). */
  countryId: string;
  stateName: string;
  municipalityName: string;
}

export interface SupportDocumentModel extends PaymentModel {
  operation: string;
  /** Forma de generación y transmisión, la misma para todas las líneas. */
  generationMode: string;
  /** Fecha de la compra o del inicio del periodo acumulado (`YYYY-MM-DD`), la misma para todas las líneas. */
  startDate: string;
  notes: string;
  /** Pedir al Api que envíe el documento al correo del proveedor cuando la DIAN lo acepte (`sendmail`). */
  sendEmail: boolean;
  seller: SellerModel;
  lines: InvoiceLineModel[];
}

export type SupportDocumentForm = FieldTree<SupportDocumentModel>;

export function emptySeller(): SellerModel {
  return {
    identificationTypeId: '',
    identificationNumber: '',
    dv: '',
    name: '',
    address: '',
    phone: '',
    email: '',
    merchantRegistration: '',
    postalZoneCode: '',
    countryId: '',
    stateName: '',
    municipalityName: '',
  };
}

/** Línea nueva del documento soporte: sin IVA, porque el proveedor no obligado a facturar casi nunca lo cobra. */
export function emptySupportLine(): InvoiceLineModel {
  return { ...emptyLine(), taxPercent: '0' };
}

/** Hoy en Colombia (`YYYY-MM-DD`), el día que usa el Api para la emisión. */
export function todayInColombia(now: Date = new Date()): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Bogota' }).format(now);
}

export function emptySupportDocument(today: string = todayInColombia()): SupportDocumentModel {
  return {
    operation: '23',
    generationMode: '1',
    startDate: today,
    paymentFormId: '1',
    paymentMethodId: '10',
    paymentDueDate: '',
    durationDays: null,
    notes: '',
    sendEmail: true,
    seller: emptySeller(),
    lines: [],
  };
}

/** Solo dígitos, como pide el Api para el código postal. */
export const POSTAL_CODE = /^\d{1,10}$/;

/** Crea el formulario; debe llamarse en un contexto de inyección (inicializador de un campo del componente). */
export function createSupportDocumentForm(
  model: WritableSignal<SupportDocumentModel> = signal(emptySupportDocument()),
): SupportDocumentForm {
  return form(model, (path) => {
    required(path.operation);
    required(path.generationMode);
    required(path.startDate);
    // Same payment rules as the invoice: form and method, and on credit the due date and the term.
    required(path.paymentFormId);
    required(path.paymentMethodId);
    const onCredit = ({ valueOf }: { valueOf: (p: typeof path.paymentFormId) => string }) =>
      valueOf(path.paymentFormId) === CREDIT_PAYMENT_FORM;
    required(path.paymentDueDate, { when: onCredit });
    required(path.durationDays, { when: onCredit });
    const seller = path.seller;
    required(seller.identificationTypeId);
    required(seller.identificationNumber);
    pattern(seller.identificationNumber, /^[A-Za-z0-9]{1,15}$/);
    pattern(seller.dv, /^\d$/);
    required(seller.name);
    maxLength(seller.name, 300);
    required(seller.address);
    maxLength(seller.address, 300);
    required(seller.phone);
    maxLength(seller.phone, 20);
    email(seller.email);
    maxLength(seller.email, 200);
    required(seller.merchantRegistration);
    maxLength(seller.merchantRegistration, 50);
    required(seller.postalZoneCode);
    pattern(seller.postalZoneCode, POSTAL_CODE);
    required(seller.countryId, {
      when: ({ valueOf }) => valueOf(path.operation) === NON_RESIDENT_OPERATION,
    });
    maxLength(seller.stateName, 100);
    maxLength(seller.municipalityName, 100);
    applyEach(path.lines, lineRules);
  });
}

/** Texto sin espacios a los lados; `undefined` si queda vacío. */
function text(value: string): string | undefined {
  const trimmed = value.trim();
  return trimmed ? trimmed : undefined;
}

/**
 * Datos del proveedor para el cuerpo (`seller`), sin los campos vacíos; el país, el departamento y la ciudad solo
 * van en la operación con un no residente.
 */
export function sellerBody(
  seller: SellerModel,
  nonResident: boolean,
): Record<string, number | string> {
  const body: Record<string, number | string | undefined> = {
    type_document_identification_id: Number(seller.identificationTypeId),
    identification_number: text(seller.identificationNumber),
    dv: text(seller.dv),
    name: text(seller.name),
    address: text(seller.address),
    phone: text(seller.phone),
    email: text(seller.email),
    merchant_registration: text(seller.merchantRegistration),
    // Text, not a number: a postal code can start with 0 (050001, Medellín).
    postal_zone_code: text(seller.postalZoneCode),
  };
  if (nonResident) {
    body['country_id'] = seller.countryId ? Number(seller.countryId) : undefined;
    body['state_name'] = text(seller.stateName);
    body['municipality_name'] = text(seller.municipalityName);
  }
  return Object.fromEntries(
    Object.entries(body).filter(
      (entry): entry is [string, number | string] => entry[1] !== undefined,
    ),
  );
}

type SupportDocumentBody = IssueSupportDocument$Params['body'];

/**
 * Cuerpo de `issueSupportDocument`: el proveedor, la operación, el pago y las líneas con los totales, impuestos y
 * valores que calculó el servidor (`calculation`); cada línea lleva la forma de generación y la fecha de la compra.
 * Con `sendmail` el Api envía el documento aceptado al correo del proveedor (`seller.email`).
 */
export function buildSupportDocumentBody(
  model: SupportDocumentModel,
  calculation: DocumentTotalsCalculation,
): SupportDocumentBody {
  const nonResident = model.operation === NON_RESIDENT_OPERATION;
  const body: SupportDocumentBody = {
    type_document_id: SUPPORT_DOCUMENT_TYPE_ID,
    type_operation_id: Number(model.operation),
    seller: sellerBody(model.seller, nonResident),
    payment_form: [paymentEntry(model)],
    legal_monetary_totals: calculation.legal_monetary_totals,
    tax_totals: calculation.tax_totals.flatMap((total) => total.tax_subtotals.map(taxEntry)),
    invoice_lines: linesBody(model.lines, calculation).map((line) => ({
      ...line,
      type_generation_transmition_id: Number(model.generationMode),
      start_date: model.startDate,
    })),
  };
  const notes = text(model.notes);
  if (notes) {
    body.notes = notes;
  }
  return { ...body, ...emailRequest(model.sendEmail, { email: model.seller.email }) };
}
