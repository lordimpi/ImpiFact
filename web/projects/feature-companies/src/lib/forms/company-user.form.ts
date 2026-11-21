import { WritableSignal } from '@angular/core';
import { FieldTree, email, form, maxLength, minLength, required } from '@angular/forms/signals';
import { CreateUserBody } from '@impifact/api';

/*
 * Form of a new user of the active company (`createUser` with `company_id`): the owner creates another owner or a
 * seller. The client only checks the shape; the API applies the password policy again and its field errors land on
 * their field. The password never leaves this model and is cleared as soon as it is sent.
 */

/** Company roles the owner can give. */
export const COMPANY_USER_ROLES = ['owner', 'seller'] as const;

export type CompanyUserRole = (typeof COMPANY_USER_ROLES)[number];

/** Shortest password the API accepts. */
export const PASSWORD_MIN_LENGTH = 10;

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
  role: 'seller',
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
    minLength(path.password, PASSWORD_MIN_LENGTH);
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
