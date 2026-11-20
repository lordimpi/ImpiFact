// Revisa el index.html compilado antes de copiarlo a la imagen de nginx (Dockerfile.web). La CSP solo permite los
// scripts y estilos en línea que llevan el nonce de la respuesta: nginx cambia el marcador IMPIFACT_CSP_NONCE por un
// valor nuevo en cada solicitud. Falla si un <script> o <style> en línea no trae el marcador (Angular lo agrega a
// todos cuando <app-root> tiene ngCspNonce) o si hay atributos de eventos en línea (onload="..."), que ningún nonce
// permite.
//   node docker/check-csp-nonce.mjs dist/app/browser/index.html
import { readFileSync } from 'node:fs';

const PLACEHOLDER = 'IMPIFACT_CSP_NONCE';
const file = process.argv[2];
if (!file) {
  console.error('Usage: node check-csp-nonce.mjs <index.html>');
  process.exit(2);
}

const html = readFileSync(file, 'utf8');
const problems = [];

if (!new RegExp(`<app-root[^>]*\\sngcspnonce="${PLACEHOLDER}"`, 'i').test(html)) {
  problems.push(`<app-root> has no ngCspNonce="${PLACEHOLDER}" attribute`);
}
for (const [tag] of html.matchAll(/<(?:script|style)\b[^>]*>/gi)) {
  if (!tag.includes(`nonce="${PLACEHOLDER}"`)) {
    problems.push(`inline element without the CSP nonce: ${tag}`);
  }
}
const handler = /<[^>]+\son[a-z]+\s*=/i.exec(html);
if (handler) {
  problems.push(`inline event handler attributes are not allowed by the CSP: ${handler[0]}`);
}

if (problems.length > 0) {
  console.error(`${file}:\n  ${problems.join('\n  ')}`);
  process.exit(1);
}
console.log(`${file}: every inline script and style carries the CSP nonce placeholder.`);
