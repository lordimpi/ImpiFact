import { WritableSignal } from '@angular/core';
import {
  FieldTree,
  email,
  form,
  maxLength,
  pattern,
  readonly,
  required,
  validate,
} from '@angular/forms/signals';
import { CreateCompanyCommand } from '@impifact/api';
import { NIT_IDENTIFICATION_TYPE_ID } from '@impifact/core';

/*
 * Formulario del alta de una empresa (`createCompany`) con Signal Forms. Los nombres del modelo son los del JSON del
 * Api, así sus errores por campo caen en su campo. Solo valida forma; los catálogos y el correo repetido los valida el
 * Api. El dígito de verificación (DV) no se escribe: la página lo calcula del NIT (`syncCheckDigit` de core) y el
 * Api lo vuelve a revisar. El NIT y el DV van en la ruta (`POST /api/v1/companies/{nit}/{dv}`), no en el cuerpo.
 */

/** Largo máximo de un NIT para el Api. */
const NIT_PATTERN = /^\d{1,15}$/;
const PHONE_PATTERN = /^\d{7,10}$/;

export interface NewCompanyModel {
  nit: string;
  dv: string;
  business_name: string;
  type_document_identification_id: string;
  type_organization_id: string;
  type_regime_id: string;
  type_liability_ids: number[];
  municipality_id: string;
  address: string;
  phone: string;
  email: string;
  merchant_registration: string;
}

/** Campos en el orden del formulario (para los errores del Api y el foco del primer campo inválido). */
export const NEW_COMPANY_FIELDS: readonly (keyof NewCompanyModel & string)[] = [
  'nit',
  'dv',
  'business_name',
  'type_document_identification_id',
  'type_organization_id',
  'type_regime_id',
  'type_liability_ids',
  'municipality_id',
  'address',
  'phone',
  'email',
  'merchant_registration',
];

export function emptyNewCompany(): NewCompanyModel {
  return {
    nit: '',
    dv: '',
    business_name: '',
    type_document_identification_id: String(NIT_IDENTIFICATION_TYPE_ID),
    type_organization_id: '',
    type_regime_id: '',
    type_liability_ids: [],
    municipality_id: '',
    address: '',
    phone: '',
    email: '',
    merchant_registration: '',
  };
}

/** Crea el formulario; debe llamarse en un contexto de inyección. */
export function createNewCompanyForm(
  model: WritableSignal<NewCompanyModel>,
): FieldTree<NewCompanyModel> {
  return form(model, (path) => {
    required(path.nit);
    pattern(path.nit, NIT_PATTERN);
    readonly(path.dv);
    required(path.business_name);
    maxLength(path.business_name, 255);
    required(path.type_document_identification_id);
    required(path.type_organization_id);
    required(path.type_regime_id);
    validate(path.type_liability_ids, ({ value }) =>
      value().length > 0 ? undefined : { kind: 'liabilitiesRequired' },
    );
    required(path.municipality_id);
    required(path.address);
    maxLength(path.address, 255);
    required(path.phone);
    pattern(path.phone, PHONE_PATTERN);
    required(path.email);
    email(path.email);
    maxLength(path.email, 255);
    required(path.merchant_registration);
    maxLength(path.merchant_registration, 50);
  });
}

const catalogId = (value: string) => (value === '' ? null : Number(value));
const text = (value: string) => (value.trim() === '' ? null : value.trim());

/** Cuerpo de `createCompany` (sin NIT ni DV, que van en la ruta). */
export function newCompanyCommand(model: NewCompanyModel): CreateCompanyCommand {
  return {
    business_name: text(model.business_name),
    type_document_identification_id: catalogId(model.type_document_identification_id),
    type_organization_id: catalogId(model.type_organization_id),
    type_regime_id: catalogId(model.type_regime_id),
    type_liability_ids: [...model.type_liability_ids],
    municipality_id: catalogId(model.municipality_id),
    address: text(model.address),
    phone: text(model.phone),
    email: text(model.email),
    merchant_registration: text(model.merchant_registration),
  };
}
