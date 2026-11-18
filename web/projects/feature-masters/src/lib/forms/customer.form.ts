import { WritableSignal, signal } from '@angular/core';
import {
  FieldTree,
  email,
  form,
  hidden,
  maxLength,
  pattern,
  required,
} from '@angular/forms/signals';
import { CreateCustomerCommand, CustomerResponse } from '@impifact/api';
import { NIT_IDENTIFICATION_TYPE } from '../models/catalogs';
import { asText, catalogId, optionalText } from './form-messages';

/*
 * Formulario del cliente con Signal Forms (`@angular/forms/signals`, estable desde Angular 22). Los nombres del
 * modelo son los del JSON del Api, así los errores por campo del servidor caen en su campo. Solo valida forma; el
 * dígito de verificación (DV) del NIT lo comprueba el Api y su error se muestra junto al campo.
 */
export interface CustomerModel {
  type_document_identification_id: string;
  identification_number: string;
  dv: string;
  name: string;
  type_organization_id: string;
  type_regime_id: string;
  type_liability_id: string;
  municipality_id: string;
  address: string;
  email: string;
  phone: string;
  merchant_registration: string;
}

export const CUSTOMER_FIELDS: readonly (keyof CustomerModel)[] = [
  'type_document_identification_id',
  'identification_number',
  'dv',
  'name',
  'type_organization_id',
  'type_regime_id',
  'type_liability_id',
  'municipality_id',
  'address',
  'email',
  'phone',
  'merchant_registration',
];

export type CustomerForm = FieldTree<CustomerModel>;

export function emptyCustomer(): CustomerModel {
  return {
    type_document_identification_id: '',
    identification_number: '',
    dv: '',
    name: '',
    type_organization_id: '',
    type_regime_id: '',
    type_liability_id: '',
    municipality_id: '',
    address: '',
    email: '',
    phone: '',
    merchant_registration: '',
  };
}

export const isNit = (type: string) => type === String(NIT_IDENTIFICATION_TYPE);

/** Crea el formulario; debe llamarse en un contexto de inyección (inicializador de un campo del componente). */
export function createCustomerForm(
  model: WritableSignal<CustomerModel> = signal(emptyCustomer()),
): CustomerForm {
  return form(model, (path) => {
    required(path.type_document_identification_id);
    required(path.identification_number);
    pattern(path.identification_number, /^[A-Za-z0-9]+$/);
    maxLength(path.identification_number, 15);
    // El DV solo existe con NIT: oculto, no valida ni cuenta como cambio.
    hidden(path.dv, {
      when: ({ valueOf }) => !isNit(valueOf(path.type_document_identification_id)),
    });
    required(path.dv);
    pattern(path.dv, /^\d$/);
    required(path.name);
    maxLength(path.name, 255);
    maxLength(path.address, 255);
    email(path.email);
    maxLength(path.email, 200);
    maxLength(path.phone, 20);
    maxLength(path.merchant_registration, 50);
  });
}

/** Cuerpo del alta y de la edición (la edición agrega `is_active`). */
export function customerCommand(model: CustomerModel): CreateCustomerCommand {
  return {
    type_document_identification_id: catalogId(model.type_document_identification_id),
    identification_number: optionalText(model.identification_number),
    dv: isNit(model.type_document_identification_id) ? optionalText(model.dv) : null,
    name: optionalText(model.name),
    phone: optionalText(model.phone),
    address: optionalText(model.address),
    email: optionalText(model.email),
    municipality_id: catalogId(model.municipality_id),
    type_organization_id: catalogId(model.type_organization_id),
    type_regime_id: catalogId(model.type_regime_id),
    type_liability_id: catalogId(model.type_liability_id),
    merchant_registration: optionalText(model.merchant_registration),
  };
}

export function customerModel(customer: CustomerResponse): CustomerModel {
  return {
    type_document_identification_id: asText(customer.type_document_identification_id),
    identification_number: asText(customer.identification_number),
    dv: asText(customer.dv),
    name: asText(customer.name),
    type_organization_id: asText(customer.type_organization_id),
    type_regime_id: asText(customer.type_regime_id),
    type_liability_id: asText(customer.type_liability_id),
    municipality_id: asText(customer.municipality_id),
    address: asText(customer.address),
    email: asText(customer.email),
    phone: asText(customer.phone),
    merchant_registration: asText(customer.merchant_registration),
  };
}
