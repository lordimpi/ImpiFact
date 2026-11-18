import { WritableSignal, signal } from '@angular/core';
import { FieldTree, form, maxLength, pattern, required } from '@angular/forms/signals';
import { SetSoftwareCommand } from '@impifact/api';
import { optionalText } from './form-messages';

/*
 * Formulario del software de la factura electrónica con Signal Forms: identificador y PIN que la DIAN asignó y el
 * identificador del set de pruebas de habilitación (TestSetId). El PIN se vacía después de cada envío.
 */
export interface SoftwareModel {
  id: string;
  pin: string;
  test_set_id: string;
}

export const SOFTWARE_FIELDS: readonly (keyof SoftwareModel)[] = ['id', 'pin', 'test_set_id'];

export type SoftwareForm = FieldTree<SoftwareModel>;

export function emptySoftware(): SoftwareModel {
  return { id: '', pin: '', test_set_id: '' };
}

export function createSoftwareForm(model: WritableSignal<SoftwareModel> = signal(emptySoftware())) {
  return form(model, (path) => {
    required(path.id);
    maxLength(path.id, 64);
    required(path.pin);
    pattern(path.pin, /^\d{5}$/);
    maxLength(path.test_set_id, 64);
  });
}

export function softwareCommand(model: SoftwareModel): SetSoftwareCommand {
  const body: SetSoftwareCommand = { id: optionalText(model.id), pin: model.pin };
  const testSetId = optionalText(model.test_set_id);
  if (testSetId) {
    body.test_set_id = testSetId;
  }
  return body;
}
