// Hashes SHA-256 de los scripts en línea de un index.html compilado, en el formato de la política de seguridad de
// contenido ('sha256-...' separados por espacios). Lo corre el build de Dockerfile.web y el resultado llega a
// script-src: así la política sigue al build aunque Angular cambie esos scripts al actualizarse.
// Falla si el HTML trae atributos de eventos en línea (onload="..."), que la política no permite.
//   node docker/csp-script-hashes.mjs dist/app/browser/index.html
import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';

const file = process.argv[2];
if (!file) {
  console.error('Usage: node csp-script-hashes.mjs <index.html>');
  process.exit(2);
}

const html = readFileSync(file, 'utf8');

const handler = /<[^>]+\son[a-z]+\s*=/i.exec(html);
if (handler) {
  console.error(
    `${file}: inline event handler attributes are not allowed by the CSP: ${handler[0]}`,
  );
  process.exit(1);
}

const hashes = [...html.matchAll(/<script(?![^>]*\ssrc=)[^>]*>([\s\S]*?)<\/script>/gi)]
  .map((match) => match[1] ?? '')
  .filter((body) => body.length > 0)
  .map((body) => `'sha256-${createHash('sha256').update(body, 'utf8').digest('base64')}'`);

process.stdout.write([...new Set(hashes)].join(' '));
