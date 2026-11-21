import {
  ElementRef,
  ModelSignal,
  Signal,
  afterRenderEffect,
  computed,
  inject,
} from '@angular/core';
import { FieldComponent } from './field.component';

export interface FieldControlLink {
  /** Finds the focusable element inside the wrapper (for example `[role="combobox"]`). */
  control: string;
  /** More ids that describe the control (for example a status message), after those of the field. */
  describedBy?: () => string | null;
  /** Elements that only decorate (an arrow named in English, not focusable): hidden from assistive technology. */
  decorative?: string;
}

/**
 * Links the focusable element of an Optimus UI control, rendered inside a ui wrapper, to the enclosing `ui-field`:
 * `aria-describedby` (explanation, hint and error of the field, plus `describedBy`) and `aria-invalid` while the
 * field shows an error. Optimus has no inputs for these attributes, so they are written on the element after each
 * render. The id and `aria-labelledby` go through the Optimus inputs (`inputId`, `ariaLabelledBy`). Call it in the
 * constructor of the wrapper.
 */
export function linkToField(field: FieldComponent | null, link: FieldControlLink): void {
  const host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  afterRenderEffect({
    write: () => {
      const ids = [field?.describedBy() ?? null, link.describedBy?.() ?? null].filter((id) => !!id);
      const invalid = field?.invalid() ?? false;
      const control = host.querySelector(link.control);
      setAttribute(control, 'aria-describedby', ids.length > 0 ? ids.join(' ') : null);
      setAttribute(control, 'aria-invalid', invalid ? 'true' : null);
      if (link.decorative) {
        host
          .querySelectorAll(link.decorative)
          .forEach((element) => element.setAttribute('aria-hidden', 'true'));
      }
    },
  });
}

/**
 * Pass-through (`[pt]`) of an Optimus select inside `ui-field`. Optimus writes these attributes on its own elements,
 * also on the option list, which it renders only while open: the combobox gets the description of the field
 * (explanation, hint and error, plus `describedBy`) and `aria-invalid` while the field shows an error; the list is
 * labelled by the field instead of the generic «Opciones»; the arrow, which only decorates (it is not focusable and is
 * named in English), is hidden from assistive technology.
 */
export function selectPassThrough(
  field: FieldComponent | null,
  describedBy: () => string | null = () => null,
) {
  return computed(() => {
    const ids = [field?.describedBy() ?? null, describedBy()].filter((id) => !!id);
    return {
      label: {
        'aria-describedby': ids.length > 0 ? ids.join(' ') : null,
        'aria-invalid': field?.invalid() ? 'true' : null,
      },
      list: { 'aria-labelledby': field?.labelId ?? null },
      dropdown: { 'aria-hidden': 'true' },
    };
  });
}

/** Sets an attribute, or removes it with `null`. */
export function setAttribute(element: Element | null, name: string, value: string | null): void {
  if (!element) return;
  if (value === null) element.removeAttribute(name);
  else element.setAttribute(name, value);
}

/** The ControlValueAccessor side of an Optimus control (select, date picker). */
export interface OptimusAccessor {
  writeValue(value: unknown): void;
  registerOnChange(fn: (value: unknown) => void): void;
  registerOnTouched(fn: () => void): void;
}

/** How a text model maps to the value of an Optimus control. By default the value is the text itself. */
export interface AccessorMapping {
  /** Text of the model for a value of the control ('' for none). */
  toText(value: unknown): string;
  /** Value of the control for a text of the model. */
  fromText(text: string): unknown;
}

const AS_TEXT: AccessorMapping = {
  toText: (value) => (value == null ? '' : String(value)),
  fromText: (text) => (text === '' ? null : text),
};

/**
 * Connects an Optimus control to a text model without Angular forms, through its ControlValueAccessor: the model goes
 * in with `writeValue` and what the person chooses comes out as text. `touched` runs when Optimus reports the control
 * touched (it lost the focus with its panel closed). Call it in the constructor of the wrapper.
 */
export function connectAccessor(
  accessor: Signal<OptimusAccessor>,
  value: ModelSignal<string>,
  touched: () => void,
  mapping: AccessorMapping = AS_TEXT,
): void {
  let connected: OptimusAccessor | null = null;
  // What the control shows, as text: a value it emitted is not written back (that would reset its search box).
  let shown: string | null = null;
  afterRenderEffect({
    write: () => {
      const control = accessor();
      const current = value();
      if (connected !== control) {
        connected = control;
        shown = null;
        control.registerOnChange((next) => {
          shown = mapping.toText(next);
          value.set(shown);
        });
        control.registerOnTouched(touched);
      }
      if (current !== shown) {
        shown = current;
        control.writeValue(mapping.fromText(current));
      }
    },
  });
}
