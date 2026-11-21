import { THEME_KEY, applyTheme, readPreference, storedTheme, writePreference } from './preferences';

// Blocked storage (private mode, site data disabled): every access throws.
function blockStorage() {
  vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
    throw new DOMException('blocked', 'SecurityError');
  });
  vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
    throw new DOMException('blocked', 'SecurityError');
  });
}

describe('panel preferences', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
    document.documentElement.dataset['theme'] = 'light';
  });

  it('defaults to the light theme without a stored choice', () => {
    expect(storedTheme()).toBe('light');
  });

  it('reads the stored dark theme', () => {
    localStorage.setItem(THEME_KEY, 'dark');

    expect(storedTheme()).toBe('dark');
  });

  it('ignores an unknown stored theme', () => {
    localStorage.setItem(THEME_KEY, 'sepia');

    expect(storedTheme()).toBe('light');
  });

  it('falls back to the light theme when the storage is blocked', () => {
    blockStorage();

    expect(storedTheme()).toBe('light');
    expect(readPreference(THEME_KEY)).toBeNull();
  });

  it('stores a preference and survives a blocked storage', () => {
    writePreference(THEME_KEY, 'dark');
    expect(localStorage.getItem(THEME_KEY)).toBe('dark');

    blockStorage();
    expect(() => writePreference(THEME_KEY, 'light')).not.toThrow();
  });

  it('applies the theme on the root element', () => {
    applyTheme('dark');
    expect(document.documentElement.dataset['theme']).toBe('dark');

    applyTheme('light');
    expect(document.documentElement.dataset['theme']).toBe('light');
  });
});
