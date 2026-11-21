import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { THEME_KEY, Theme, applyTheme, storedTheme, writePreference } from './preferences';

/**
 * Light or dark theme of the panel, toggled from the top bar. main.ts applies the stored theme before the bootstrap;
 * this store keeps it in a signal, stores every change and follows a change made in another tab.
 */
@Injectable({ providedIn: 'root' })
export class ThemeStore {
  private readonly current = signal<Theme>(storedTheme());
  readonly theme = this.current.asReadonly();

  constructor() {
    const onStorage = (event: StorageEvent) => {
      if (event.key === THEME_KEY) {
        this.set(event.newValue === 'dark' ? 'dark' : 'light');
      }
    };
    window.addEventListener('storage', onStorage);
    inject(DestroyRef).onDestroy(() => window.removeEventListener('storage', onStorage));
  }

  toggle(): void {
    const next = this.current() === 'dark' ? 'light' : 'dark';
    this.set(next);
    writePreference(THEME_KEY, next);
  }

  private set(theme: Theme): void {
    this.current.set(theme);
    applyTheme(theme);
  }
}
