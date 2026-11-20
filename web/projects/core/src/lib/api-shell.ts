/*
 * Lo que `core` usa del cliente generado, tomado de `api/src/shell.ts` y no del barril `@impifact/api`.
 *
 * `core` está en el paquete inicial y esbuild mete en él todo archivo que alcance por sus `import`: importar el barril
 * (que reexporta todas las funciones generadas) metía en el inicial cada función que usara algún feature perezoso.
 * Los archivos de `core` importan de aquí; los features siguen usando el barril. Una regla de ESLint
 * (`eslint.config.js`) impide importar el barril desde `core`.
 */
export * from '@impifact/api/shell';
