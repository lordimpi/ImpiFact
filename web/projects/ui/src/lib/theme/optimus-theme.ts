import { CSP_NONCE, inject } from '@angular/core';
import { Optimus } from '@openng/optimus-ui/config';
import { definePreset } from '@openng/optimus-ui-themes';
import base from '@openng/optimus-ui-themes/aura/base';
import button from '@openng/optimus-ui-themes/aura/button';
import dialog from '@openng/optimus-ui-themes/aura/dialog';
import inputtext from '@openng/optimus-ui-themes/aura/inputtext';
import skeleton from '@openng/optimus-ui-themes/aura/skeleton';
import type { UiThemeOptions } from './provide-ui-theme';

/*
 * Tema de Optimus UI. Este archivo no sale en la API pública de ui: lo carga provideUiTheme con un import() al
 * arrancar, así el motor de estilos y el preset (unos 60 kB) no cuentan en el bundle inicial y bajan en paralelo
 * con config.json.
 */

const v = (name: string) => `var(--if-${name})`;

/*
 * Escala neutra de Optimus UI («surface») apuntada a nuestros tokens. Como cada token ya cambia con el
 * tema (light-dark() en tokens.css), Optimus queda en un solo esquema y no aplica su propio modo oscuro.
 */
const surface = {
  0: v('color-bg'),
  50: v('color-surface'),
  100: v('color-surface-raised'),
  200: v('color-border'),
  300: v('color-border'),
  400: v('color-border-strong'),
  500: v('color-text-muted'),
  600: v('color-text-muted'),
  700: v('color-text'),
  800: v('color-text'),
  900: v('color-text'),
  950: v('color-text'),
};

const primary = {
  50: v('color-primary-subtle'),
  100: v('color-primary-subtle-strong'),
  200: v('color-primary-subtle-strong'),
  300: v('color-primary'),
  400: v('color-primary'),
  500: v('color-primary'),
  600: v('color-primary-hover'),
  700: v('color-primary-active'),
  800: v('color-primary-active'),
  900: v('color-primary-active'),
  950: v('color-primary-active'),
};

/**
 * Preset reducido: la base de Aura y solo los componentes de Optimus UI que envuelve ui (botón, diálogo,
 * campo de texto y esqueleto). El preset completo trae los 80 componentes y pesa unos 145 kB
 * (docs/spikes/optimus-ui.md).
 */
export const impifactPreset = definePreset(
  { ...base, components: { button, dialog, inputtext, skeleton } },
  {
    semantic: {
      primary,
      focusRing: {
        width: v('focus-ring-width'),
        style: 'solid',
        color: v('color-focus'),
        offset: v('focus-ring-offset'),
      },
      formField: {
        paddingX: v('control-padding-x'),
        paddingY: v('control-padding-y'),
        borderRadius: v('radius-sm'),
        focusRing: {
          width: v('focus-ring-width'),
          style: 'solid',
          color: v('color-focus'),
          offset: '0',
        },
      },
      colorScheme: {
        light: {
          surface,
          primary: {
            color: v('color-primary'),
            contrastColor: v('color-on-primary'),
            hoverColor: v('color-primary-hover'),
            activeColor: v('color-primary-active'),
          },
          formField: {
            background: v('color-bg'),
            disabledBackground: v('color-surface-raised'),
            borderColor: v('color-border-strong'),
            hoverBorderColor: v('color-text-muted'),
            focusBorderColor: v('color-primary'),
            invalidBorderColor: v('color-danger'),
            color: v('color-text'),
            disabledColor: v('color-text-muted'),
            placeholderColor: v('color-text-muted'),
            invalidPlaceholderColor: v('color-danger'),
          },
          text: {
            color: v('color-text'),
            hoverColor: v('color-text'),
            mutedColor: v('color-text-muted'),
            hoverMutedColor: v('color-text'),
          },
          mask: { background: v('color-overlay'), color: v('color-text') },
          content: {
            background: v('color-bg'),
            hoverBackground: v('color-surface'),
            borderColor: v('color-border'),
            color: v('color-text'),
            hoverColor: v('color-text'),
          },
          overlay: {
            modal: {
              background: v('color-bg'),
              borderColor: v('color-border'),
              color: v('color-text'),
            },
          },
        },
      },
    },
    components: {
      button: {
        colorScheme: {
          light: {
            root: {
              danger: {
                background: v('color-danger'),
                hoverBackground: v('color-danger-hover'),
                activeBackground: v('color-danger-active'),
                borderColor: v('color-danger'),
                hoverBorderColor: v('color-danger-hover'),
                activeBorderColor: v('color-danger-active'),
                color: v('color-on-danger'),
                hoverColor: v('color-on-danger'),
                activeColor: v('color-on-danger'),
                focusRing: { color: v('color-focus'), shadow: 'none' },
              },
            },
          },
        },
      },
    },
  },
);

/**
 * Aplica el preset de ImpiFact a Optimus UI. Corre en el contexto de inyección del arranque.
 * El modo oscuro lo deciden los tokens (prefers-color-scheme y `data-theme`), no Optimus.
 *
 * Nonce de la CSP: el de `options` o, si no hay, el de la página (`CSP_NONCE` de Angular, que lee el atributo
 * `ngCspNonce` de `<app-root>`; el servidor del panel lo cambia en cada respuesta). La CSP no permite estilos en línea
 * sin ese nonce, así que Optimus lo pone en cada etiqueta `<style>` que agrega.
 */
export function applyUiTheme(options: UiThemeOptions): void {
  const nonce = options.nonce ?? inject(CSP_NONCE, { optional: true }) ?? undefined;
  inject(Optimus).setConfig({
    ripple: false,
    theme: { preset: impifactPreset, options: { darkModeSelector: 'none', cssLayer: false } },
    ...(nonce ? { csp: { nonce } } : {}),
  });
}
