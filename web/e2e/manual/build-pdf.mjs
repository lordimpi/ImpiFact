// Genera el PDF del manual de uso a partir de «docs/manual/Manual de uso.md» con el Chromium de
// Playwright (`page.pdf()`), sin dependencias nuevas: convierte a HTML el subconjunto de Markdown que usa el manual
// (títulos, párrafos, negrita, cursiva, código, listas, imágenes, tablas, notas y enlaces), incrusta las capturas
// de docs/manual/img y arma portada, índice con números de página y pie «Página N de M». El PDF queda en
// projects/app/public/ayuda/manual-de-uso.pdf: el panel lo sirve en /ayuda/manual-de-uso.pdf.
//
// Uso, desde web/: `npm run manual:pdf` (solo el PDF) o `npm run manual` (capturas y PDF).
//
// Números de página del índice: Chromium guarda en el PDF un destino con nombre por cada ancla enlazada (`/Dests`).
// La primera pasada deja el índice con números provisionales; se leen las páginas de esos destinos y la segunda
// pasada escribe los números reales (si el índice cambiara de largo, se repite hasta que coincidan).
import { chromium } from '@playwright/test';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const manualDir = path.resolve(here, '..', '..', '..', 'docs', 'manual');
const source = path.join(manualDir, 'Manual de uso.md');
const output = path.resolve(
  here,
  '..',
  '..',
  'projects',
  'app',
  'public',
  'ayuda',
  'manual-de-uso.pdf',
);

const PRIMARY = '#1d5bd6';

// ---------------------------------------------------------------------------------------------------------------
// Markdown → HTML (solo lo que usa el manual).
// ---------------------------------------------------------------------------------------------------------------

const escapeHtml = (text) =>
  text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');

function inline(text) {
  const codes = [];
  let html = escapeHtml(text).replace(/`([^`]+)`/g, (_, code) => {
    codes.push(`<code>${code}</code>`);
    return `\u0000${codes.length - 1}\u0000`;
  });
  html = html
    .replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
    .replace(/(^|[^*])\*([^*\s][^*]*)\*/g, '$1<em>$2</em>')
    .replace(/\[([^\]]+)\]\(([^)]+)\)/g, '<a href="$2">$1</a>');
  return html.replace(/\u0000(\d+)\u0000/g, (_, index) => codes[Number(index)]);
}

function slug(text) {
  return text
    .toLowerCase()
    .normalize('NFD')
    .replace(/\p{Diacritic}/gu, '')
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-|-$/g, '');
}

/** Ancho y alto de un PNG (cabecera IHDR). */
function pngSize(buffer) {
  return { width: buffer.readUInt32BE(16), height: buffer.readUInt32BE(20) };
}

function image(alt, src, figure) {
  const file = readFileSync(path.join(manualDir, src));
  const { width, height } = pngSize(file);
  const portrait = height > width;
  const data = `data:image/png;base64,${file.toString('base64')}`;
  return (
    `<figure class="${portrait ? 'figure figure--phone' : 'figure'}">` +
    `<img src="${data}" alt="${escapeHtml(alt)}">` +
    `<figcaption><span>Figura ${figure}.</span> ${inline(alt)}</figcaption></figure>`
  );
}

const CALLOUTS = {
  advertencia: 'danger',
  importante: 'warning',
  nota: 'info',
  consejo: 'info',
};

/** Convierte el Markdown y devuelve el HTML del cuerpo y los títulos para el índice. */
function convert(markdown) {
  const lines = markdown
    .replace(/<!--[\s\S]*?-->/g, '')
    .replace(/\r\n/g, '\n')
    .split('\n');
  const out = [];
  const headings = [];
  let title = '';
  let chapter = 0;
  let figure = 0;
  let index = 0;

  const isBlockStart = (line) =>
    /^(#{1,6} |> |\| |!\[|\s*[-*] |\s*\d+\. )/.test(line) || line.trim() === '';

  while (index < lines.length) {
    const line = lines[index];

    if (line.trim() === '') {
      index++;
      continue;
    }

    const heading = /^(#{1,6}) (.*)$/.exec(line);
    if (heading) {
      const level = heading[1].length;
      const text = heading[2].trim();
      if (level === 1) {
        title = text;
      } else {
        const id = slug(text);
        if (level === 2) {
          chapter++;
        }
        const number = level === 2 ? `${chapter}` : '';
        headings.push({ level, text, id, number });
        out.push(
          `<h${level} id="${id}">${number ? `<span class="num">${number}</span>` : ''}${inline(text)}</h${level}>`,
        );
      }
      index++;
      continue;
    }

    const picture = /^!\[([^\]]*)\]\(([^)]+)\)\s*$/.exec(line);
    if (picture) {
      figure++;
      out.push(image(picture[1], picture[2], figure));
      index++;
      continue;
    }

    if (line.startsWith('> ')) {
      const quote = [];
      while (index < lines.length && lines[index].startsWith('>')) {
        quote.push(lines[index].replace(/^>\s?/, ''));
        index++;
      }
      const text = quote.join(' ');
      const label = /^\*\*([^*:]+)/.exec(text)?.[1]?.toLowerCase() ?? '';
      const kind = Object.entries(CALLOUTS).find(([key]) => label.startsWith(key))?.[1] ?? 'info';
      out.push(`<aside class="callout callout--${kind}"><p>${inline(text)}</p></aside>`);
      continue;
    }

    if (line.startsWith('|')) {
      const rows = [];
      while (index < lines.length && lines[index].startsWith('|')) {
        rows.push(lines[index]);
        index++;
      }
      const cells = (row) =>
        row
          .replace(/^\||\|$/g, '')
          .split('|')
          .map((cell) => inline(cell.trim()));
      const [head, , ...body] = rows;
      out.push(
        '<table><thead><tr>' +
          cells(head)
            .map((cell) => `<th>${cell}</th>`)
            .join('') +
          '</tr></thead><tbody>' +
          body
            .map(
              (row) =>
                '<tr>' +
                cells(row)
                  .map((cell) => `<td>${cell}</td>`)
                  .join('') +
                '</tr>',
            )
            .join('') +
          '</tbody></table>',
      );
      continue;
    }

    const listItem = /^(\s*)([-*]|\d+\.) (.*)$/;
    if (listItem.test(line)) {
      // Listas con un nivel de anidación (sublista con sangría de 2 o más espacios).
      const items = [];
      const ordered = /\d+\./.test(listItem.exec(line)[2]);
      while (index < lines.length && listItem.test(lines[index])) {
        const [, indent, marker, text] = listItem.exec(lines[index]);
        if (indent.length >= 2 && items.length > 0) {
          const parent = items.at(-1);
          parent.children.ordered = /\d+\./.test(marker);
          parent.children.items.push(text);
        } else {
          items.push({ text, children: { ordered: false, items: [] } });
        }
        index++;
      }
      const tag = ordered ? 'ol' : 'ul';
      out.push(
        `<${tag}>` +
          items
            .map((item) => {
              const sub = item.children.items.length
                ? `<${item.children.ordered ? 'ol' : 'ul'}>${item.children.items
                    .map((child) => `<li>${inline(child)}</li>`)
                    .join('')}</${item.children.ordered ? 'ol' : 'ul'}>`
                : '';
              return `<li>${inline(item.text)}${sub}</li>`;
            })
            .join('') +
          `</${tag}>`,
      );
      continue;
    }

    const paragraph = [];
    while (index < lines.length && !isBlockStart(lines[index])) {
      paragraph.push(lines[index].trim());
      index++;
    }
    if (paragraph.length === 0) {
      paragraph.push(line.trim());
      index++;
    }
    out.push(`<p>${inline(paragraph.join(' '))}</p>`);
  }

  return { title, headings, body: out.join('\n') };
}

// ---------------------------------------------------------------------------------------------------------------
// Documento HTML.
// ---------------------------------------------------------------------------------------------------------------

const STYLE = `
@page { size: A4; margin: 18mm 17mm 20mm; }
* { box-sizing: border-box; }
html { -webkit-print-color-adjust: exact; print-color-adjust: exact; }
body { margin: 0; font-family: system-ui, -apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif;
  font-size: 10.5pt; line-height: 1.5; color: #1f2933; }
a { color: ${PRIMARY}; text-decoration: none; }
code { font-family: ui-monospace, "Cascadia Mono", Consolas, monospace; font-size: 0.92em; background: #eef1f6;
  padding: 0 3px; border-radius: 3px; }

.cover { height: 257mm; display: flex; flex-direction: column; justify-content: space-between; break-after: page; }
.cover__brand { display: flex; align-items: center; gap: 12px; }
.cover__mark { display: inline-grid; place-items: center; width: 44px; height: 44px; border-radius: 10px;
  background: ${PRIMARY}; color: #fff; font-weight: 700; font-size: 18px; letter-spacing: 0.02em; }
.cover__name { font-size: 24px; font-weight: 700; color: #111827; }
.cover__main { border-left: 6px solid ${PRIMARY}; padding: 8px 0 8px 22px; }
.cover__title { font-size: 40pt; line-height: 1.1; margin: 0 0 10px; color: #111827; }
.cover__subtitle { font-size: 15pt; color: #4b5563; margin: 0; }
.cover__foot { color: #6b7280; font-size: 10pt; border-top: 1px solid #d7dce5; padding-top: 10px; }
.cover__date { font-size: 12pt; color: #111827; font-weight: 600; margin-bottom: 4px; }

.toc { break-after: page; }
.toc h2 { break-before: auto; }
.toc ol { list-style: none; margin: 0; padding: 0; }
.toc li a { display: flex; align-items: baseline; gap: 6px; color: #1f2933; padding: 3px 0; }
.toc li.toc--2 { margin-top: 8px; font-weight: 600; }
.toc li.toc--3 a { padding-left: 22px; font-size: 10pt; color: #374151; }
.toc .dots { flex: 1; border-bottom: 1px dotted #b8c0cc; transform: translateY(-3px); }
.toc .page { font-variant-numeric: tabular-nums; min-width: 2.2em; text-align: right; }

h2 { break-before: page; font-size: 20pt; line-height: 1.2; color: #111827; margin: 0 0 14px; padding-bottom: 8px;
  border-bottom: 2px solid ${PRIMARY}; }
h2 .num { display: inline-grid; place-items: center; min-width: 30px; height: 30px; margin-right: 10px;
  border-radius: 8px; background: ${PRIMARY}; color: #fff; font-size: 14pt; vertical-align: 3px; }
h3 { font-size: 13pt; color: ${PRIMARY}; margin: 20px 0 6px; break-after: avoid; }
h2 + h3 { margin-top: 6px; }
p { margin: 0 0 8px; }
ul, ol { margin: 0 0 10px; padding-left: 22px; }
li { margin: 2px 0; }
li > ul, li > ol { margin: 2px 0; }
strong { color: #111827; }

.figure { margin: 10px 0 16px; break-inside: avoid; text-align: center; }
.figure img { display: block; margin: 0 auto; max-width: 100%; border: 1px solid #d7dce5;
  border-radius: 6px; }
.figure--phone img { max-height: 125mm; }
figcaption { margin-top: 5px; font-size: 9pt; color: #4b5563; }
figcaption span { font-weight: 600; color: #1f2933; }

.callout { break-inside: avoid; margin: 10px 0 14px; padding: 10px 14px; border-radius: 6px; border-left: 4px solid; }
.callout p { margin: 0; }
.callout--info { background: #eef3fc; border-color: ${PRIMARY}; }
.callout--warning { background: #fff6e5; border-color: #b45309; }
.callout--danger { background: #fdecec; border-color: #c62828; color: #7f1d1d; }
.callout--danger strong { color: #b71c1c; }

table { width: 100%; border-collapse: collapse; margin: 6px 0 14px; font-size: 9.5pt; break-inside: auto; }
thead { display: table-header-group; }
tr { break-inside: avoid; }
th { background: #eef3fc; color: #111827; text-align: left; font-weight: 600; }
th, td { border: 1px solid #d7dce5; padding: 5px 8px; vertical-align: top; }
td:first-child { font-weight: 600; white-space: nowrap; }
`;

const FOOTER = `
<div style="width:100%;font-family:system-ui,'Segoe UI',Arial,sans-serif;font-size:8pt;color:#6b7280;
  padding:0 17mm;display:flex;justify-content:space-between;">
  <span>ImpiFact · Manual de uso</span>
  <span>Página <span class="pageNumber"></span> de <span class="totalPages"></span></span>
</div>`;

function monthYear(date) {
  return new Intl.DateTimeFormat('es-CO', {
    month: 'long',
    year: 'numeric',
    timeZone: 'America/Bogota',
  }).format(date);
}

function documentHtml({ title, headings, body }, pages) {
  const date = monthYear(new Date());
  const toc = headings
    .map(
      (heading) =>
        `<li class="toc--${heading.level}"><a href="#${heading.id}">` +
        `<span>${heading.number ? `${heading.number}. ` : ''}${inline(heading.text)}</span>` +
        `<span class="dots"></span><span class="page">${pages.get(heading.id) ?? '00'}</span></a></li>`,
    )
    .join('');
  return `<!doctype html><html lang="es-CO"><head><meta charset="utf-8"><title>${escapeHtml(title)}</title>
<style>${STYLE}</style></head><body>
<section class="cover">
  <div class="cover__brand"><span class="cover__mark">IF</span><span class="cover__name">ImpiFact</span></div>
  <div class="cover__main">
    <h1 class="cover__title">Manual de uso</h1>
    <p class="cover__subtitle">Panel de facturación electrónica ante la DIAN</p>
  </div>
  <div class="cover__foot">
    <div class="cover__date">${date.charAt(0).toUpperCase()}${date.slice(1)}</div>
    Las pantallas de este manual muestran datos de ejemplo ficticios.
  </div>
</section>
<nav class="toc"><h2>Contenido</h2><ol>${toc}</ol></nav>
<main>${body}</main>
</body></html>`;
}

// ---------------------------------------------------------------------------------------------------------------
// Lectura de los destinos con nombre del PDF de Chromium (objetos sin comprimir).
// ---------------------------------------------------------------------------------------------------------------

function pdfObject(text, number) {
  const start = text.indexOf(`\n${number} 0 obj`);
  if (start < 0) {
    return '';
  }
  return text.slice(start, text.indexOf('endobj', start));
}

/** Número de página (desde 1) de cada destino con nombre. */
function destinationPages(pdf) {
  const text = pdf.toString('latin1');
  const catalog = /\/Type \/Catalog[\s\S]*?\/Pages (\d+) 0 R[\s\S]*?\/Dests (\d+) 0 R/.exec(text);
  if (!catalog) {
    return new Map();
  }
  const order = [];
  const walk = (number) => {
    const object = pdfObject(text, number);
    if (/\/Type \/Pages/.test(object)) {
      const kids = /\/Kids \[([^\]]*)\]/.exec(object)?.[1] ?? '';
      for (const kid of kids.matchAll(/(\d+) 0 R/g)) {
        walk(Number(kid[1]));
      }
    } else {
      order.push(number);
    }
  };
  walk(Number(catalog[1]));
  const pages = new Map();
  const dests = pdfObject(text, Number(catalog[2]));
  for (const [, name, page] of dests.matchAll(/\/([A-Za-z0-9#_.-]+) \[(\d+) 0 R/g)) {
    pages.set(name, order.indexOf(Number(page)) + 1);
  }
  return pages;
}

// ---------------------------------------------------------------------------------------------------------------

const manual = convert(readFileSync(source, 'utf8'));
const browser = await chromium.launch({ args: ['--lang=es-CO'] });
try {
  const page = await browser.newPage({ locale: 'es-CO' });
  const render = async (pages) => {
    await page.setContent(documentHtml(manual, pages), { waitUntil: 'load' });
    return page.pdf({
      format: 'A4',
      printBackground: true,
      displayHeaderFooter: true,
      headerTemplate: '<span></span>',
      footerTemplate: FOOTER,
      margin: { top: '18mm', bottom: '20mm', left: '17mm', right: '17mm' },
    });
  };

  let pages = new Map();
  let pdf = await render(pages);
  for (let pass = 0; pass < 3; pass++) {
    const found = destinationPages(pdf);
    const same =
      found.size === pages.size && [...found].every(([id, number]) => pages.get(id) === number);
    if (same) {
      break;
    }
    pages = found;
    pdf = await render(pages);
  }
  const missing = manual.headings.filter((heading) => !pages.has(heading.id));
  if (missing.length > 0) {
    throw new Error(`Sin número de página en el índice: ${missing.map((h) => h.id).join(', ')}`);
  }
  mkdirSync(path.dirname(output), { recursive: true });
  writeFileSync(output, pdf);
  const count = (pdf.toString('latin1').match(/\/Type \/Page\b/g) ?? []).length;
  console.log(
    `${path.relative(process.cwd(), output)}: ${count} páginas, ${(pdf.length / 1024).toFixed(0)} KB`,
  );
} finally {
  await browser.close();
}
