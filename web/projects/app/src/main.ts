import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';
import { applyTheme, storedTheme } from './app/shell/preferences';

// The stored theme goes on <html> before anything renders, so a dark choice never flashes light. index.html keeps
// `data-theme="light"` as the default (the CSP allows no inline script there).
applyTheme(storedTheme());

bootstrapApplication(App, appConfig).catch((err) => console.error(err));
