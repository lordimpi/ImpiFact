import { WritableSignal, signal } from '@angular/core';
import { FieldTree, form, maxLength, pattern, required } from '@angular/forms/signals';
import { SetSoftwareCommand } from '@impifact/api';
import { optionalText } from './form-messages';

/*
 * Formulario del software de la factura electrónica con Signal Forms: identificador y PIN que la DIAN asignó y el
 * identificador del set de pruebas de habilitación (TestSetId). El PIN se vacía después de cada envío y nunca se lee
 * del Api: si ya hay uno guardado, dejarlo vacío lo conserva.
 */
export interface SoftwareModel {
  id: string;
  pin: string;
  test_set_id: string;
}

/** Software ya guardado en el ambiente actual: su identificador y si tiene PIN. */
export interface SavedSoftware {
  identifier: string;
  hasPin: boolean;
}

export const SOFTWARE_FIELDS: readonly (keyof SoftwareModel)[] = ['id', 'pin', 'test_set_id'];

export type SoftwareForm = FieldTree<SoftwareModel>;

export function emptySoftware(): SoftwareModel {
  return { id: '', pin: '', test_set_id: '' };
}

/** El PIN vacío conserva el guardado solo si el identificador sigue siendo el mismo. */
export function keepsPin(saved: SavedSoftware | null, identifier: string): boolean {
  return !!saved?.hasPin && saved.identifier === identifier.trim();
}

export function createSoftwareForm(
  model: WritableSignal<SoftwareModel> = signal(emptySoftware()),
  saved: () => SavedSoftware | null = () => null,
) {
  return form(model, (path) => {
    required(path.id);
    maxLength(path.id, 64);
    required(path.pin, { when: ({ valueOf }) => !keepsPin(saved(), valueOf(path.id)) });
    pattern(path.pin, /^\d{5}$/);
    maxLength(path.test_set_id, 64);
  });
}

/**
 * Cuerpo de `setCompanySoftware`. Con el PIN vacío no se envían ni el PIN ni el identificador (el Api exige el PIN
 * junto con el identificador), así el software guardado los conserva.
 */
export function softwareCommand(model: SoftwareModel): SetSoftwareCommand {
  const body: SetSoftwareCommand = model.pin ? { id: optionalText(model.id), pin: model.pin } : {};
  const testSetId = optionalText(model.test_set_id);
  if (testSetId) {
    body.test_set_id = testSetId;
  }
  return body;
}
