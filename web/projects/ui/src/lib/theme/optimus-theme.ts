import { CSP_NONCE, inject } from '@angular/core';
import { Optimus } from '@openng/optimus-ui/config';
import { definePreset } from '@openng/optimus-ui-themes';
import base from '@openng/optimus-ui-themes/aura/base';
import breadcrumb from '@openng/optimus-ui-themes/aura/breadcrumb';
import button from '@openng/optimus-ui-themes/aura/button';
import checkbox from '@openng/optimus-ui-themes/aura/checkbox';
import confirmdialog from '@openng/optimus-ui-themes/aura/confirmdialog';
import datepicker from '@openng/optimus-ui-themes/aura/datepicker';
import dialog from '@openng/optimus-ui-themes/aura/dialog';
import fileupload from '@openng/optimus-ui-themes/aura/fileupload';
import inputtext from '@openng/optimus-ui-themes/aura/inputtext';
import menu from '@openng/optimus-ui-themes/aura/menu';
import paginator from '@openng/optimus-ui-themes/aura/paginator';
import select from '@openng/optimus-ui-themes/aura/select';
import skeleton from '@openng/optimus-ui-themes/aura/skeleton';
import tooltip from '@openng/optimus-ui-themes/aura/tooltip';
import type { UiThemeOptions } from './provide-ui-theme';

/*
 * Tema de Optimus UI. Este archivo no sale en la API pública de ui: lo carga provideUiTheme con un import() al
 * arrancar, así el motor de estilos y el preset (unos 60 kB) no cuentan en el bundle inicial y bajan en paralelo
 * con config.json.
 */

const v = (name: string) => `var(--if-${name})`;

/*
 * Escala neutra de Optimus UI («surface») apuntada a nuestros tokens. Optimus stays on a single scheme and never
 * applies its own dark mode: every value is a token, and tokens.css swaps the tokens for the dark theme.
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
 * Preset reducido: la base de Aura y solo los componentes de Optimus UI que envuelve ui (botón, casilla, diálogo,
 * campo de texto, esqueleto, select, selector de fechas, carga de archivos, paginador, diálogo de confirmación,
 * tooltip y migas de pan). El preset completo trae los 80 componentes y pesa unos 145 kB (docs/spikes/optimus-ui.md).
 */
export const impifactPreset = definePreset(
  {
    ...base,
    components: {
      breadcrumb,
      button,
      checkbox,
      confirmdialog,
      datepicker,
      dialog,
      fileupload,
      inputtext,
      menu,
      paginator,
      select,
      skeleton,
      tooltip,
    },
  },
  {
    semantic: {
      primary,
      focusRing: {
        width: v('focus-ring-width'),
        style: 'solid',
        color: v('color-focus'),
        offset: v('focus-ring-offset'),
      },
      content: { borderRadius: v('radius-md') },
      overlay: {
        modal: {
          borderRadius: v('radius-lg'),
          padding: v('space-4'),
          shadow: v('shadow-overlay'),
        },
      },
      formField: {
        paddingX: v('control-padding-x'),
        paddingY: v('control-padding-y'),
        borderRadius: v('radius-sm'),
        sm: {
          fontSize: v('font-size-sm'),
          paddingX: v('space-2'),
          paddingY: v('space-1'),
        },
        lg: {
          fontSize: v('font-size-md'),
          paddingX: v('space-3'),
          paddingY: v('control-padding-y'),
        },
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
            shadow: '0 1px 2px rgb(16 24 40 / 5%)',
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
      // The page breadcrumb sits on the page background, flush with the title; ui-page-title styles its links.
      breadcrumb: {
        root: { padding: '0', background: 'transparent', gap: v('space-2') },
        separator: { color: v('color-text-muted') },
      },
      button: {
        root: { label: { fontWeight: v('font-weight-bold') } },
        colorScheme: {
          light: {
            root: {
              // Secondary: white button with a soft border, the usual neutral action in accounting software.
              secondary: {
                background: v('color-bg'),
                hoverBackground: v('color-surface'),
                activeBackground: v('color-surface-raised'),
                borderColor: v('color-border-emphasis'),
                hoverBorderColor: v('color-border-strong'),
                activeBorderColor: v('color-border-strong'),
                color: v('color-text'),
                hoverColor: v('color-text'),
                activeColor: v('color-text'),
                focusRing: { color: v('color-focus'), shadow: 'none' },
              },
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
            text: {
              primary: {
                hoverBackground: v('color-primary-subtle'),
                activeBackground: v('color-primary-subtle-strong'),
                color: v('color-primary'),
              },
              secondary: {
                hoverBackground: v('color-surface-raised'),
                activeBackground: v('color-border'),
                color: v('color-text'),
              },
            },
          },
        },
      },
      dialog: {
        title: { fontSize: v('font-size-md'), fontWeight: v('font-weight-bold') },
      },
      skeleton: {
        root: { borderRadius: v('radius-sm') },
        colorScheme: {
          light: {
            root: {
              background: v('color-surface-raised'),
              animationBackground: v('color-shimmer'),
            },
          },
        },
      },
    },
  },
);

/**
 * Aplica el preset de ImpiFact a Optimus UI. Corre en el contexto de inyección del arranque.
 * `darkModeSelector: 'none'` keeps Optimus from emitting a dark scheme: the dark theme comes from the tokens.
 *
 * Nonce de la CSP: el de `options` o, si no hay, el de la página (`CSP_NONCE` de Angular, que lee el atributo
 * `ngCspNonce` de `<app-root>`; el servidor del panel lo cambia en cada respuesta). La CSP no permite estilos en línea
 * sin ese nonce, así que Optimus lo pone en cada etiqueta `<style>` que agrega.
 */
export function applyUiTheme(options: UiThemeOptions): void {
  const nonce = options.nonce ?? inject(CSP_NONCE, { optional: true }) ?? undefined;
  inject(Optimus).setConfig({
    ripple: false,
    // Lists and calendars open over the page, so a card (overflow hidden) never cuts them.
    overlayAppendTo: 'body',
    theme: { preset: impifactPreset, options: { darkModeSelector: 'none', cssLayer: false } },
    ...(nonce ? { csp: { nonce } } : {}),
  });
}
