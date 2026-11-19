import { WritableSignal } from '@angular/core';
import {
  FieldTree,
  ValidationError,
  form,
  hidden,
  maxLength,
  pattern,
  readonly,
  required,
} from '@angular/forms/signals';
import { AppError } from '@impifact/core';
import { CreatePlanCommand, StorePlanCommand } from '@impifact/api';
import { PlatformPlan } from '../state/platform-plans.store';

/*
 * Formularios del feature con Signal Forms. Solo validan forma (obligatorio, largo y números enteros); las reglas
 * (nombre único, plan o empresa que existen) las aplica el Api y sus errores por campo llegan con su texto.
 */

const WHOLE_NUMBER = /^\d+$/;

/** Periodos que acepta el Api: 1 mensual, 2 anual, 3 paquete. */
export const PLAN_PERIODS: readonly number[] = [1, 2, 3];

export interface PlanModel {
  id: string;
  name: string;
  period: string;
  qty_docs_invoice: string;
  qty_docs_payroll: string;
  qty_docs_radian: string;
  qty_docs_ds: string;
  state: boolean;
  observations: string;
}

export const PLAN_FIELDS: readonly (keyof PlanModel & string)[] = [
  'id',
  'name',
  'period',
  'qty_docs_invoice',
  'qty_docs_payroll',
  'qty_docs_radian',
  'qty_docs_ds',
  'observations',
];

export const QUANTITY_FIELDS = [
  'qty_docs_invoice',
  'qty_docs_payroll',
  'qty_docs_radian',
  'qty_docs_ds',
] as const;

/** Plan nuevo: sin id, que lo asigna el Api al crearlo (`createPlan`). */
export function emptyPlan(): PlanModel {
  return {
    id: '',
    name: '',
    period: '1',
    qty_docs_invoice: '',
    qty_docs_payroll: '',
    qty_docs_radian: '',
    qty_docs_ds: '',
    state: true,
    observations: '',
  };
}

const text = (value: number | null) => (value === null ? '' : String(value));

export function planModel(plan: PlatformPlan): PlanModel {
  return {
    id: String(plan.id),
    name: plan.name,
    period: String(plan.period),
    qty_docs_invoice: text(plan.qtyInvoice),
    qty_docs_payroll: text(plan.qtyPayroll),
    qty_docs_radian: text(plan.qtyRadian),
    qty_docs_ds: text(plan.qtyDs),
    state: plan.active,
    observations: plan.observations ?? '',
  };
}

/**
 * `editing` dice si el plan ya existe: entonces su id se muestra sin poder cambiarlo (es la dirección del `PUT`). Al
 * crear, el id no existe todavía: lo asigna el Api.
 */
export function createPlanForm(
  model: WritableSignal<PlanModel>,
  editing: () => boolean,
): FieldTree<PlanModel> {
  return form(model, (path) => {
    hidden(path.id, { when: () => !editing() });
    readonly(path.id);
    required(path.id);
    pattern(path.id, WHOLE_NUMBER);
    required(path.name);
    maxLength(path.name, 100);
    required(path.period);
    for (const name of QUANTITY_FIELDS) {
      pattern(path[name], WHOLE_NUMBER);
    }
    maxLength(path.observations, 500);
  });
}

const optionalNumber = (value: string) => (value.trim() === '' ? null : Number(value));

/** Cuerpo de `savePlan` (`PUT /plans/{id}`), para cambiar un plan que ya existe. */
export function planCommand(model: PlanModel): StorePlanCommand {
  return { id: Number(model.id), ...newPlanCommand(model) };
}

/** Cuerpo de `createPlan` (`POST /plans`): sin id, que lo asigna el Api. */
export function newPlanCommand(model: PlanModel): CreatePlanCommand {
  return {
    name: model.name.trim(),
    period: Number(model.period),
    qty_docs_invoice: optionalNumber(model.qty_docs_invoice),
    qty_docs_payroll: optionalNumber(model.qty_docs_payroll),
    qty_docs_radian: optionalNumber(model.qty_docs_radian),
    qty_docs_ds: optionalNumber(model.qty_docs_ds),
    state: model.state,
    observations: model.observations.trim() === '' ? null : model.observations.trim(),
  };
}

export interface AssignModel {
  nit: string;
  plan_id: string;
}

export function createAssignForm(model: WritableSignal<AssignModel>): FieldTree<AssignModel> {
  return form(model, (path) => {
    required(path.nit);
    pattern(path.nit, WHOLE_NUMBER);
    required(path.plan_id);
  });
}

export const emptyAssign = (): AssignModel => ({ nit: '', plan_id: '' });

type Translate = (key: string, params?: Record<string, unknown>) => string;

/**
 * Mensaje bajo un campo: el primer error, solo después de tocarlo o de intentar enviar. Un error del Api va primero
 * (explica mejor que el del cliente). `formatKey` reemplaza el texto del error de formato cuando el campo tiene uno
 * propio (por defecto, el de número entero).
 */
export function fieldMessage(
  state: { touched(): boolean; errors(): readonly ValidationError[] },
  translate: Translate,
  formatKey = 'common.wholeNumber',
): string | null {
  const errors = state.touched() ? state.errors() : [];
  const error = errors.find((item) => item.kind === 'server') ?? errors[0];
  if (!error) {
    return null;
  }
  switch (error.kind) {
    case 'required':
      return translate('common.required');
    case 'email':
      return translate('common.email');
    case 'pattern':
      return translate(formatKey);
    case 'maxLength':
      return translate('common.maxLength', { max: (error as { maxLength?: number }).maxLength });
    default:
      return error.message ?? null;
  }
}

/** Errores por campo del Api que se ponen junto a su campo (los nombres del modelo son los del JSON del Api). */
export function serverFieldErrors<Model extends object>(
  tree: FieldTree<Model>,
  names: readonly (keyof Model & string)[],
  error: AppError,
) {
  const fields = tree as unknown as Record<string, FieldTree<unknown>>;
  return names.flatMap((name) => {
    const message = error.fieldErrors[name]?.[0];
    const fieldTree = fields[name];
    return message && fieldTree ? [{ kind: 'server', message, fieldTree }] : [];
  });
}
