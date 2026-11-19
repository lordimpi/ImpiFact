import { WritableSignal } from '@angular/core';
import { FieldTree, email, form, maxLength, required } from '@angular/forms/signals';
import { CreateUserBody } from '@impifact/api';

/*
 * Formulario de un usuario de una empresa (`createUser` con `company_id`): la plataforma crea el primer dueño o un
 * vendedor. Solo valida forma; la política de contraseñas (largo) la aplica el Api y su error llega en el campo
 * `password`. La contraseña nunca se guarda fuera de este modelo y se borra apenas se envía.
 */

/** Roles de empresa que la plataforma puede crear. */
export const COMPANY_USER_ROLES = ['owner', 'seller'] as const;

export type CompanyUserRole = (typeof COMPANY_USER_ROLES)[number];

export interface CompanyUserModel {
  email: string;
  name: string;
  phone: string;
  password: string;
  role: CompanyUserRole;
}

export const COMPANY_USER_FIELDS: readonly (keyof CompanyUserModel & string)[] = [
  'email',
  'name',
  'phone',
  'password',
  'role',
];

export const emptyCompanyUser = (): CompanyUserModel => ({
  email: '',
  name: '',
  phone: '',
  password: '',
  role: 'owner',
});

export function createCompanyUserForm(
  model: WritableSignal<CompanyUserModel>,
): FieldTree<CompanyUserModel> {
  return form(model, (path) => {
    required(path.email);
    email(path.email);
    maxLength(path.email, 255);
    required(path.name);
    maxLength(path.name, 255);
    maxLength(path.phone, 30);
    required(path.password);
    required(path.role);
  });
}

export function companyUserBody(model: CompanyUserModel, companyId: string): CreateUserBody {
  const phone = model.phone.trim();
  return {
    company_id: companyId,
    email: model.email.trim(),
    name: model.name.trim(),
    phone: phone === '' ? null : phone,
    password: model.password,
    role: model.role,
  };
}
