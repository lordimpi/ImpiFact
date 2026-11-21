/** Controls of a `ui-field` that show their validation message once they lose the focus. */
export const FIELD_CONTROLS = [
  'input:not([type="hidden"]):not([disabled]):not([readonly])',
  'textarea:not([disabled]):not([readonly])',
  '[role="combobox"]:not([aria-disabled="true"])',
].join(', ');

/**
 * Keeps the focus in a form field when a button or link is pressed with the mouse. Without it the press blurs the
 * field first: an empty required field is marked as touched and shows «Este campo es obligatorio», the content
 * grows and the button moves away before the release, so a «Cancelar» does nothing. The click still runs and the
 * keyboard is unchanged. Only controls inside a `ui-field` are kept, since those are the ones that validate on blur
 * (the column filters of `ui-table`, for example, apply their value when they lose the focus).
 *
 * The app calls it from a `document:mousedown` listener.
 */
export function keepFieldFocusOnPress(event: MouseEvent): void {
  const active = document.activeElement;
  const inField =
    active instanceof Element &&
    active.matches(FIELD_CONTROLS) &&
    active.closest('ui-field') !== null;
  if (
    inField &&
    event.button === 0 &&
    (event.target as Element | null)?.closest('button, a[href]')
  ) {
    event.preventDefault();
  }
}
