/*
 * Non-sensitive panel preferences kept in localStorage (docs/Arquitectura frontend.md: theme, density, last company).
 * The storage can be blocked (private mode, site data disabled): reads fall back to the default and writes are
 * skipped, the panel keeps working with the choice for the current page only.
 */

export type Theme = 'light' | 'dark';

export const THEME_KEY = 'impifact.theme';
export const SIDEBAR_KEY = 'impifact.sidebar';

export function readPreference(key: string): string | null {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

export function writePreference(key: string, value: string): void {
  try {
    localStorage.setItem(key, value);
  } catch {
    // Blocked storage: the choice lasts until the page closes.
  }
}

/** Light unless the user chose dark; the system preference (prefers-color-scheme) does not count. */
export function storedTheme(): Theme {
  return readPreference(THEME_KEY) === 'dark' ? 'dark' : 'light';
}

/** The tokens (ui tokens.css) switch on `data-theme` of <html>. */
export function applyTheme(theme: Theme): void {
  document.documentElement.dataset['theme'] = theme;
}
