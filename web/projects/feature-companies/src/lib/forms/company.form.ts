import { WritableSignal, signal } from '@angular/core';
import {
  FieldTree,
  email,
  form,
  maxLength,
  pattern,
  required,
  validate,
} from '@angular/forms/signals';
import { CompanyResponse, UpdateCompanyCommand } from '@impifact/api';
import { NIT_IDENTIFICATION_TYPE } from '../models/catalogs';
import { asText, catalogId, optionalText } from './form-messages';

/*
 * Formulario de los datos de la empresa con Signal Forms. Los nombres del modelo son los del JSON del Api, así los
 * errores por campo del servidor caen en su campo. Solo valida forma; los catálogos y el correo repetido los valida
 * el Api.
 */
export interface CompanyModel {
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

export const COMPANY_FIELDS: readonly (keyof CompanyModel)[] = [
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

export type CompanyForm = FieldTree<CompanyModel>;

export function emptyCompany(businessName = ''): CompanyModel {
  return {
    business_name: businessName,
    type_document_identification_id: String(NIT_IDENTIFICATION_TYPE),
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
export function createCompanyForm(model: WritableSignal<CompanyModel> = signal(emptyCompany())) {
  return form(model, (path) => {
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
    pattern(path.phone, /^\d{7,10}$/);
    required(path.email);
    email(path.email);
    maxLength(path.email, 255);
    required(path.merchant_registration);
    maxLength(path.merchant_registration, 50);
  });
}

export function companyCommand(model: CompanyModel): UpdateCompanyCommand {
  return {
    business_name: optionalText(model.business_name),
    type_document_identification_id: catalogId(model.type_document_identification_id),
    type_organization_id: catalogId(model.type_organization_id),
    type_regime_id: catalogId(model.type_regime_id),
    type_liability_ids: [...model.type_liability_ids],
    municipality_id: catalogId(model.municipality_id),
    address: optionalText(model.address),
    phone: optionalText(model.phone),
    email: optionalText(model.email),
    merchant_registration: optionalText(model.merchant_registration),
  };
}

/** Modelo a partir de la lectura de la empresa (`getCompany`) o de lo que devolvió el último guardado. */
export function companyModel(company: CompanyResponse): CompanyModel {
  return {
    business_name: asText(company.business_name),
    type_document_identification_id:
      asText(company.type_document_identification_id) || String(NIT_IDENTIFICATION_TYPE),
    type_organization_id: asText(company.type_organization_id),
    type_regime_id: asText(company.type_regime_id),
    type_liability_ids: (company.type_liability_ids ?? []).map(Number),
    municipality_id: asText(company.municipality_id),
    address: asText(company.address),
    phone: asText(company.phone),
    email: asText(company.email),
    merchant_registration: asText(company.merchant_registration),
  };
}
