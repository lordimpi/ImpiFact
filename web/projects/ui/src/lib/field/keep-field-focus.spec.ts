import { keepFieldFocusOnPress } from './keep-field-focus';

function press(target: Element, button = 0): MouseEvent {
  const event = new MouseEvent('mousedown', { bubbles: true, cancelable: true, button });
  Object.defineProperty(event, 'target', { value: target });
  keepFieldFocusOnPress(event);
  return event;
}

describe('keepFieldFocusOnPress', () => {
  let cancel: HTMLButtonElement;

  beforeEach(() => {
    document.body.innerHTML = `
      <ui-field><input id="name" /></ui-field>
      <input id="column-filter" />
      <p id="text">Texto</p>
      <a id="link" href="/productos">Cancelar</a>
      <button id="cancel" type="button">Cancelar</button>`;
    cancel = document.getElementById('cancel') as HTMLButtonElement;
  });

  afterEach(() => (document.body.innerHTML = ''));

  it('keeps the focus in a ui-field control when a button or a link is pressed', () => {
    (document.getElementById('name') as HTMLInputElement).focus();

    expect(press(cancel).defaultPrevented).toBe(true);
    expect(press(document.getElementById('link')!).defaultPrevented).toBe(true);
  });

  it('lets other presses move the focus as usual', () => {
    (document.getElementById('name') as HTMLInputElement).focus();

    expect(press(document.getElementById('text')!).defaultPrevented).toBe(false);
    expect(press(cancel, 2).defaultPrevented).toBe(false);
  });

  it('leaves controls outside a ui-field alone, such as a table column filter that applies on blur', () => {
    (document.getElementById('column-filter') as HTMLInputElement).focus();

    expect(press(cancel).defaultPrevented).toBe(false);
  });
});
