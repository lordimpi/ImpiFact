import { WritableSignal, signal } from '@angular/core';
import { FieldTree, form, maxLength, pattern, required, validate } from '@angular/forms/signals';
import { NumberingRangeItem, UpsertResolutionCommand } from '@impifact/api';
import { HABILITATION_RESOLUTION } from '../models/dian';
import { asText, optionalText } from './form-messages';

/*
 * Formulario de una resolución de numeración con Signal Forms. Los nombres del modelo son los del JSON del Api.
 * Solo valida forma (obligatorios, dígitos y orden del rango); la vigencia y la clave técnica las valida el Api.
 */
export interface ResolutionModel {
  type_document_id: string;
  prefix: string;
  resolution: string;
  resolution_date: string;
  technical_key: string;
  from: string;
  to: string;
  date_from: string;
  date_to: string;
}

export const RESOLUTION_FIELDS: readonly (keyof ResolutionModel)[] = [
  'type_document_id',
  'prefix',
  'resolution',
  'resolution_date',
  'technical_key',
  'from',
  'to',
  'date_from',
  'date_to',
];

export type ResolutionForm = FieldTree<ResolutionModel>;

export function emptyResolution(): ResolutionModel {
  return {
    type_document_id: '1',
    prefix: '',
    resolution: '',
    resolution_date: '',
    technical_key: '',
    from: '',
    to: '',
    date_from: '',
    date_to: '',
  };
}

export function habilitationResolution(): ResolutionModel {
  return { ...HABILITATION_RESOLUTION };
}

/** Modelo con los datos de un rango que devolvió la DIAN, conservando el tipo de documento elegido. */
export function resolutionFromRange(
  current: ResolutionModel,
  range: NumberingRangeItem,
): ResolutionModel {
  return {
    ...current,
    prefix: asText(range.prefix),
    resolution: asText(range.resolution_number),
    resolution_date: asText(range.resolution_date),
    technical_key: asText(range.technical_key),
    from: asText(range.from),
    to: asText(range.to),
    date_from: asText(range.valid_date_from),
    date_to: asText(range.valid_date_to),
  };
}

export function createResolutionForm(
  model: WritableSignal<ResolutionModel> = signal(emptyResolution()),
) {
  return form(model, (path) => {
    required(path.type_document_id);
    maxLength(path.prefix, 4);
    required(path.resolution);
    maxLength(path.resolution, 50);
    required(path.from);
    pattern(path.from, /^\d+$/);
    required(path.to);
    pattern(path.to, /^\d+$/);
    validate(path.to, ({ value, valueOf }) => {
      const from = Number(valueOf(path.from));
      const to = Number(value());
      return value() && valueOf(path.from) && to < from ? { kind: 'rangeOrder' } : undefined;
    });
    required(path.date_from);
    required(path.date_to);
    maxLength(path.technical_key, 255);
  });
}

export function resolutionCommand(model: ResolutionModel): UpsertResolutionCommand {
  return {
    type_document_id: Number(model.type_document_id),
    prefix: optionalText(model.prefix),
    resolution: optionalText(model.resolution),
    resolution_date: optionalText(model.resolution_date),
    technical_key: optionalText(model.technical_key),
    from: Number(model.from),
    to: Number(model.to),
    date_from: optionalText(model.date_from),
    date_to: optionalText(model.date_to),
  };
}
