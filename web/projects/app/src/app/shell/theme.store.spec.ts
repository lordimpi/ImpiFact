import { TestBed } from '@angular/core/testing';
import { THEME_KEY } from './preferences';
import { ThemeStore } from './theme.store';

describe('ThemeStore', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
    document.documentElement.dataset['theme'] = 'light';
  });

  it('starts from the stored theme', () => {
    localStorage.setItem(THEME_KEY, 'dark');

    expect(TestBed.inject(ThemeStore).theme()).toBe('dark');
  });

  it('switches between light and dark, applies the theme and stores the choice', () => {
    const store = TestBed.inject(ThemeStore);
    expect(store.theme()).toBe('light');

    store.toggle();
    expect(store.theme()).toBe('dark');
    expect(document.documentElement.dataset['theme']).toBe('dark');
    expect(localStorage.getItem(THEME_KEY)).toBe('dark');

    store.toggle();
    expect(store.theme()).toBe('light');
    expect(document.documentElement.dataset['theme']).toBe('light');
    expect(localStorage.getItem(THEME_KEY)).toBe('light');
  });

  it('still switches the theme when the storage is blocked', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('blocked', 'SecurityError');
    });
    const store = TestBed.inject(ThemeStore);

    store.toggle();

    expect(store.theme()).toBe('dark');
    expect(document.documentElement.dataset['theme']).toBe('dark');
  });

  it('follows a theme chosen in another tab', () => {
    const store = TestBed.inject(ThemeStore);

    window.dispatchEvent(new StorageEvent('storage', { key: THEME_KEY, newValue: 'dark' }));

    expect(store.theme()).toBe('dark');
    expect(document.documentElement.dataset['theme']).toBe('dark');
  });
});
