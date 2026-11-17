// @ts-check
// Configuración única de ESLint para todo el workspace (aplicación, librerías, E2E).
// Las fronteras entre proyectos (sección 2 de docs/Arquitectura frontend.md) se verifican con eslint-plugin-boundaries.
const eslint = require('@eslint/js');
const { defineConfig, globalIgnores } = require('eslint/config');
const tseslint = require('typescript-eslint');
const angular = require('angular-eslint');
const boundaries = require('eslint-plugin-boundaries');

/** Prefijo de selector por proyecto: `app-` en la aplicación, `ui-` en ui, etc. */
function selectorPrefix(files, prefix) {
  return {
    files,
    rules: {
      '@angular-eslint/component-selector': [
        'error',
        { type: 'element', prefix, style: 'kebab-case' },
      ],
      '@angular-eslint/directive-selector': [
        'error',
        { type: 'attribute', prefix, style: 'camelCase' },
      ],
    },
  };
}

module.exports = defineConfig([
  globalIgnores([
    'dist/',
    'out-tsc/',
    '.angular/',
    'coverage/',
    'playwright-report/',
    'test-results/',
  ]),
  {
    files: ['**/*.ts'],
    extends: [
      eslint.configs.recommended,
      tseslint.configs.recommended,
      tseslint.configs.stylistic,
      angular.configs.tsRecommended,
    ],
    processor: angular.processInlineTemplates,
    rules: {
      '@typescript-eslint/no-explicit-any': 'error',
      '@angular-eslint/prefer-on-push-component-change-detection': 'error',
      '@angular-eslint/prefer-standalone': 'error',
      '@angular-eslint/prefer-signals': 'error',
    },
  },
  selectorPrefix(['projects/app/**/*.ts'], 'app'),
  selectorPrefix(['projects/ui/**/*.ts'], 'ui'),
  selectorPrefix(['projects/core/**/*.ts'], 'core'),
  selectorPrefix(['projects/api/**/*.ts'], 'api'),
  selectorPrefix(['projects/feature-*/**/*.ts'], 'feat'),
  {
    files: ['**/*.html'],
    extends: [angular.configs.templateRecommended, angular.configs.templateAccessibility],
    rules: {
      '@angular-eslint/template/prefer-control-flow': 'error',
    },
  },

  // Fronteras entre proyectos del workspace.
  {
    files: ['projects/**/*.ts'],
    plugins: { boundaries },
    settings: {
      'import/resolver': {
        typescript: { project: './tsconfig.base.json' },
      },
      'boundaries/elements': [
        { type: 'app', pattern: 'projects/app' },
        { type: 'core', pattern: 'projects/core' },
        { type: 'ui', pattern: 'projects/ui' },
        { type: 'api', pattern: 'projects/api' },
        { type: 'feature', pattern: 'projects/feature-*', capture: ['feature'] },
      ],
    },
    rules: {
      'boundaries/no-unknown-files': 'error',
      'boundaries/dependencies': [
        'error',
        {
          default: 'disallow',
          message:
            '{{ from.element.types.[0] }} no puede importar {{ to.element.types.[0] }} (ver docs/Arquitectura frontend.md, sección 2).',
          policies: [
            // Cada proyecto puede importar sus propios archivos.
            { allow: { dependency: { relationship: { to: 'internal' } } } },
            {
              from: { element: { type: 'app' } },
              allow: { to: { element: { types: ['core', 'ui', 'api', 'feature'] } } },
            },
            // Un feature no importa a otro: se comunican por rutas o por servicios de core.
            {
              from: { element: { type: 'feature' } },
              allow: { to: { element: { types: ['core', 'ui', 'api'] } } },
            },
            { from: { element: { type: 'core' } }, allow: { to: { element: { type: 'api' } } } },
            // ui y api no importan nada del workspace.
          ],
        },
      ],
    },
  },
]);
