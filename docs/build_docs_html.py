"""Build a single tabbed HTML reader from the Markdown documents in this folder.

Usage:
    python build_docs_html.py

The Markdown files stay the source of truth; re-run this script after editing them.
The generated page renders Markdown with marked and diagrams (```mermaid blocks)
with Mermaid, both loaded from a CDN, so it needs internet access when opened.
"""

from __future__ import annotations

import json
from datetime import date
from pathlib import Path

DOCS_DIR = Path(__file__).resolve().parent
OUTPUT = DOCS_DIR / "ImpiFact-docs.html"

# (file name, tab id, tab label) in reading order.
DOCUMENTS = [
    ("PRD.md", "prd", "PRD"),
    ("Arquitectura detallada.md", "arquitectura", "Arquitectura"),
    ("Modelo de datos detallado.md", "datos", "Modelo de datos"),
    ("Arquitectura frontend.md", "frontend", "Frontend"),
    ("Anexo Contratos JSON y reglas.md", "anexo", "Anexo: contratos y reglas"),
    ("php-rules.md", "reglas", "Reglas del PHP"),
]

MARKED_URL = "https://cdn.jsdelivr.net/npm/marked@12.0.2/marked.min.js"
MERMAID_URL = "https://cdn.jsdelivr.net/npm/mermaid@10.9.1/dist/mermaid.min.js"

TEMPLATE = r"""<!doctype html>
<html lang="es">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>ImpiFact Documentation</title>
<style>
:root {
  /* Soft light: warm off-white paper on a muted page, no pure white or pure black. */
  --bg: #e6e3dc; --surface: #f4f2ed; --surface-2: #e9e5dd; --text: #2b2f36;
  --muted: #555b64; --border: #d0cbc1; --accent: #1d6b58; --accent-soft: #dfeae4;
  --code-bg: #ebe7df; --shadow: 0 1px 3px rgba(60,50,30,.08);
}
/* Light is the default; dark only applies when the reader picks it with the toggle. */
:root[data-theme="dark"] {
  /* Dimmed dark: slate grays instead of near-black, softened text. */
  --bg: #22272e; --surface: #2d333b; --surface-2: #373e47; --text: #d3dae3;
  --muted: #a3adb8; --border: #444c56; --accent: #6cc4a8; --accent-soft: #2c4a42;
  --code-bg: #262c34; --shadow: none;
}
* { box-sizing: border-box; }
html { scroll-padding-top: 120px; }
body { margin: 0; background: var(--bg); color: var(--text);
  font: 16px/1.6 system-ui, -apple-system, "Segoe UI", Roboto, sans-serif; }
header.top { position: sticky; top: 0; z-index: 10; background: var(--surface);
  border-bottom: 1px solid var(--border); box-shadow: var(--shadow); }
.top-row { display: flex; align-items: center; gap: 12px; padding: 10px 16px; max-width: 1400px; margin: 0 auto; }
.brand { font-weight: 700; font-size: 17px; }
.brand small { font-weight: 400; color: var(--muted); margin-left: 8px; font-size: 13px; }
.spacer { flex: 1; }
.search { width: min(280px, 40vw); padding: 6px 10px; border: 1px solid var(--border); border-radius: 6px;
  background: var(--bg); color: var(--text); font: inherit; font-size: 14px; }
button.theme { border: 1px solid var(--border); background: var(--bg); color: var(--text);
  border-radius: 6px; width: 36px; height: 34px; padding: 0; cursor: pointer;
  display: inline-flex; align-items: center; justify-content: center; }
button.theme:hover { color: var(--accent); border-color: var(--accent); }
nav.tabs { display: flex; gap: 4px; overflow-x: auto; padding: 0 16px; max-width: 1400px; margin: 0 auto; scrollbar-width: thin; }
nav.tabs button { flex: none; border: 0; background: none; color: var(--muted); padding: 10px 14px;
  font: inherit; font-size: 15px; cursor: pointer; border-bottom: 3px solid transparent; }
nav.tabs button:hover { color: var(--text); }
nav.tabs button[aria-selected="true"] { color: var(--accent); border-bottom-color: var(--accent); font-weight: 600; }
.layout { display: grid; grid-template-columns: 260px minmax(0, 1fr); gap: 32px; max-width: 1400px; margin: 0 auto; padding: 24px 16px 80px; }
aside.toc { position: sticky; top: 120px; align-self: start; max-height: calc(100vh - 140px); overflow-y: auto; font-size: 14px; }
aside.toc h2 { font-size: 12px; text-transform: uppercase; letter-spacing: .06em; color: var(--muted); margin: 0 0 8px; }
aside.toc a { display: block; color: var(--muted); text-decoration: none; padding: 3px 8px; border-left: 2px solid transparent; }
aside.toc a.l3 { padding-left: 20px; font-size: 13px; }
aside.toc a:hover, aside.toc a.active { color: var(--accent); border-left-color: var(--accent); }
main { min-width: 0; }
article.doc { background: var(--surface); border: 1px solid var(--border); border-radius: 10px; padding: 32px 40px; box-shadow: var(--shadow); }
article.doc[hidden] { display: none; }
article h1 { font-size: 28px; line-height: 1.25; margin-top: 0; }
article h2 { font-size: 22px; margin-top: 40px; padding-bottom: 6px; border-bottom: 1px solid var(--border); }
article h3 { font-size: 18px; margin-top: 28px; }
article a { color: var(--accent); }
article a.external::after { content: "\2197"; font-size: .8em; margin-left: 2px; }
article code { background: var(--code-bg); padding: 1px 5px; border-radius: 4px; font-size: .9em;
  font-family: ui-monospace, "Cascadia Code", Consolas, monospace; }
article pre { background: var(--code-bg); border: 1px solid var(--border); border-radius: 8px; padding: 14px 16px; overflow-x: auto; line-height: 1.45; }
article pre code { background: none; padding: 0; font-size: 13.5px; }
.table-wrap { overflow-x: auto; margin: 16px 0; border: 1px solid var(--border); border-radius: 8px; }
article table { border-collapse: collapse; width: 100%; font-size: 14.5px; }
article th, article td { border-bottom: 1px solid var(--border); padding: 8px 12px; text-align: left; vertical-align: top; }
article th { background: var(--surface-2); font-weight: 600; }
article tr:last-child td { border-bottom: 0; }
article blockquote { margin: 16px 0; padding: 8px 16px; border-left: 4px solid var(--accent); background: var(--accent-soft); border-radius: 0 6px 6px 0; }
.diagram { margin: 20px 0; padding: 16px; border: 1px solid var(--border); border-radius: 8px; background: var(--surface); overflow-x: auto; text-align: center; }
.diagram svg { max-width: 100%; height: auto; }
mark { background: #ffe58a; color: #1f2328; border-radius: 2px; }
.search-info { font-size: 13px; color: var(--muted); }
.notice { padding: 12px 16px; border: 1px solid var(--border); border-radius: 8px; background: var(--accent-soft); margin-bottom: 16px; }
footer { color: var(--muted); font-size: 13px; text-align: center; padding: 24px 16px; }
details.toc-mobile { display: none; }
@media (max-width: 900px) {
  .layout { grid-template-columns: minmax(0, 1fr); gap: 16px; }
  aside.toc { display: none; }
  details.toc-mobile { display: block; background: var(--surface); border: 1px solid var(--border); border-radius: 8px; padding: 8px 12px; font-size: 14px; }
  details.toc-mobile a { display: block; color: var(--muted); text-decoration: none; padding: 3px 0; }
  details.toc-mobile a.l3 { padding-left: 14px; }
  article.doc { padding: 20px 16px; }
  .brand small { display: none; }
  .search { width: 40vw; }
}
@media print {
  header.top, aside.toc, details.toc-mobile { display: none; }
  .layout { display: block; }
  article.doc[hidden] { display: block; break-before: page; }
}
</style>
</head>
<body>
<header class="top">
  <div class="top-row">
    <div class="brand">ImpiFact <small>Documentación de la migración · generado __BUILD_DATE__</small></div>
    <div class="spacer"></div>
    <input class="search" id="search" type="search" placeholder="Buscar en todos los documentos" aria-label="Buscar">
    <button class="theme" id="theme" type="button" aria-label="Cambiar a modo oscuro" title="Cambiar a modo oscuro"></button>
  </div>
  <nav class="tabs" id="tabs" role="tablist"></nav>
</header>
<div class="layout">
  <aside class="toc" id="toc"><h2>En este documento</h2><div id="toc-links"></div></aside>
  <main>
    <details class="toc-mobile" id="toc-mobile"><summary>En este documento</summary><div id="toc-mobile-links"></div></details>
    <div id="search-results" hidden></div>
    <div id="docs"></div>
  </main>
</div>
<footer>Fuente: archivos Markdown de <code>docs/</code>. Regenerar con <code>python build_docs_html.py</code>.</footer>

<script id="doc-data" type="application/json">__DOC_DATA__</script>
<script src="__MARKED_URL__"></script>
<script src="__MERMAID_URL__"></script>
<script>
(function () {
  "use strict";
  var docs = JSON.parse(document.getElementById("doc-data").textContent);
  var tabsEl = document.getElementById("tabs");
  var docsEl = document.getElementById("docs");
  var tocEl = document.getElementById("toc-links");
  var tocMobileEl = document.getElementById("toc-mobile-links");
  var searchEl = document.getElementById("search");
  var resultsEl = document.getElementById("search-results");
  var themeBtn = document.getElementById("theme");
  var hasMarked = typeof window.marked !== "undefined";
  var hasMermaid = typeof window.mermaid !== "undefined";
  var rendered = {};
  var active = null;

  function store(key, value) {
    try { if (value === undefined) return localStorage.getItem(key); localStorage.setItem(key, value); }
    catch (e) { return null; }
  }

  function slugify(text) {
    return text.toLowerCase().normalize("NFD").replace(/[̀-ͯ]/g, "")
      .replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "").slice(0, 80) || "seccion";
  }

  function currentTheme() {
    var forced = document.documentElement.getAttribute("data-theme");
    return forced === "dark" ? "dark" : "light";
  }

  function updateThemeButton() {
    var dark = currentTheme() === "dark";
    var sun = '<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"/></svg>';
    var moon = '<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z"/></svg>';
    var label = dark ? "Cambiar a modo claro" : "Cambiar a modo oscuro";
    themeBtn.innerHTML = dark ? sun : moon;
    themeBtn.setAttribute("aria-label", label);
    themeBtn.title = label;
  }

  var savedTheme = store("impifact-theme");
  document.documentElement.setAttribute("data-theme", savedTheme === "dark" ? "dark" : "light");
  updateThemeButton();

  // Diagram colors follow the page palette so they never look brighter or darker than the text around them.
  function mermaidConfig(theme) {
    var dark = theme === "dark";
    return {
      startOnLoad: false, securityLevel: "antiscript", theme: "base",
      themeVariables: dark ? {
        darkMode: true, background: "#2d333b", fontFamily: "system-ui, Segoe UI, sans-serif",
        primaryColor: "#2c4a42", primaryBorderColor: "#6cc4a8", primaryTextColor: "#d3dae3",
        secondaryColor: "#373e47", tertiaryColor: "#262c34", lineColor: "#a3adb8", textColor: "#d3dae3",
        edgeLabelBackground: "#2d333b", clusterBkg: "#262c34", clusterBorder: "#444c56"
      } : {
        darkMode: false, background: "#f4f2ed", fontFamily: "system-ui, Segoe UI, sans-serif",
        primaryColor: "#dfeae4", primaryBorderColor: "#1d6b58", primaryTextColor: "#2b2f36",
        secondaryColor: "#e9e5dd", tertiaryColor: "#ebe7df", lineColor: "#555b64", textColor: "#2b2f36",
        edgeLabelBackground: "#f4f2ed", clusterBkg: "#ebe7df", clusterBorder: "#d0cbc1"
      }
    };
  }

  if (hasMermaid) mermaid.initialize(mermaidConfig(currentTheme()));

  function renderDoc(doc) {
    var article = document.createElement("article");
    article.className = "doc";
    article.id = "doc-" + doc.id;
    article.hidden = true;
    article.setAttribute("role", "tabpanel");
    if (!hasMarked) {
      article.innerHTML = '<div class="notice">No se pudo cargar el renderizador de Markdown (¿sin conexión?). Se muestra el texto original.</div>';
      var pre = document.createElement("pre");
      pre.textContent = doc.markdown;
      article.appendChild(pre);
      return article;
    }
    article.innerHTML = marked.parse(doc.markdown, { gfm: true });
    // External links open in a new tab so the reader never leaves the documentation.
    article.querySelectorAll('a[href^="http://"], a[href^="https://"]').forEach(function (a) {
      a.target = "_blank";
      a.rel = "noopener noreferrer";
      a.classList.add("external");
      a.title = "Se abre en una pestaña nueva";
    });
    article.querySelectorAll("table").forEach(function (table) {
      var wrap = document.createElement("div");
      wrap.className = "table-wrap";
      table.parentNode.insertBefore(wrap, table);
      wrap.appendChild(table);
    });
    article.querySelectorAll("pre > code.language-mermaid").forEach(function (code) {
      var div = document.createElement("div");
      div.className = "diagram";
      div.setAttribute("data-source", code.textContent);
      div.textContent = code.textContent;
      code.parentNode.replaceWith(div);
    });
    var seen = {};
    doc.headings = [];
    article.querySelectorAll("h1, h2, h3").forEach(function (h) {
      var base = doc.id + "-" + slugify(h.textContent);
      var id = base, n = 2;
      while (seen[id]) id = base + "-" + n++;
      seen[id] = true;
      h.id = id;
      if (h.tagName !== "H1") doc.headings.push({ id: id, text: h.textContent, level: h.tagName === "H2" ? 2 : 3 });
    });
    return article;
  }

  function renderDiagrams(article) {
    if (!hasMermaid) return;
    var nodes = Array.prototype.slice.call(article.querySelectorAll(".diagram"));
    nodes.forEach(function (node, i) {
      var id = article.id + "-diagram-" + i + "-" + Date.now();
      mermaid.render(id, node.getAttribute("data-source")).then(function (out) {
        node.innerHTML = out.svg;
      }).catch(function () {
        node.innerHTML = "";
        var pre = document.createElement("pre");
        pre.textContent = node.getAttribute("data-source");
        node.appendChild(pre);
      });
    });
  }

  function buildToc(doc) {
    var html = doc.headings.map(function (h) {
      return '<a class="l' + h.level + '" href="#' + h.id + '">' + escapeHtml(h.text) + "</a>";
    }).join("");
    tocEl.innerHTML = html;
    tocMobileEl.innerHTML = html;
  }

  function escapeHtml(s) {
    return s.replace(/[&<>"]/g, function (c) { return { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]; });
  }

  function show(id, anchor) {
    var doc = docs.filter(function (d) { return d.id === id; })[0] || docs[0];
    active = doc;
    docs.forEach(function (d) {
      var el = document.getElementById("doc-" + d.id);
      el.hidden = d.id !== doc.id;
      var tab = document.getElementById("tab-" + d.id);
      tab.setAttribute("aria-selected", d.id === doc.id ? "true" : "false");
    });
    if (!rendered[doc.id]) { renderDiagrams(document.getElementById("doc-" + doc.id)); rendered[doc.id] = true; }
    buildToc(doc);
    store("impifact-tab", doc.id);
    if (anchor) {
      var target = document.getElementById(anchor);
      if (target) target.scrollIntoView();
    } else {
      window.scrollTo(0, 0);
    }
    if (location.hash !== "#" + (anchor || doc.id)) history.replaceState(null, "", "#" + (anchor || doc.id));
  }

  docs.forEach(function (doc) {
    var tab = document.createElement("button");
    tab.type = "button";
    tab.id = "tab-" + doc.id;
    tab.setAttribute("role", "tab");
    tab.textContent = doc.label;
    tab.addEventListener("click", function () { clearSearch(); show(doc.id); });
    tabsEl.appendChild(tab);
    docsEl.appendChild(renderDoc(doc));
  });

  function fromHash() {
    var hash = decodeURIComponent(location.hash.replace(/^#/, ""));
    if (!hash) return show(store("impifact-tab") || docs[0].id);
    var owner = docs.filter(function (d) { return hash === d.id || hash.indexOf(d.id + "-") === 0; })[0];
    if (owner) show(owner.id, hash === owner.id ? null : hash);
    else show(docs[0].id);
  }

  document.addEventListener("click", function (e) {
    var a = e.target.closest && e.target.closest('a[href^="#"]');
    if (!a) return;
    e.preventDefault();
    var hash = decodeURIComponent(a.getAttribute("href").slice(1));
    var owner = docs.filter(function (d) { return hash.indexOf(d.id + "-") === 0; })[0];
    if (owner) { clearSearch(); show(owner.id, hash); document.getElementById("toc-mobile").open = false; }
  });

  window.addEventListener("hashchange", fromHash);

  // Highlight the current section in the sidebar.
  var ticking = false;
  window.addEventListener("scroll", function () {
    if (ticking || !active) return;
    ticking = true;
    requestAnimationFrame(function () {
      ticking = false;
      var current = null;
      active.headings.forEach(function (h) {
        var el = document.getElementById(h.id);
        if (el && el.getBoundingClientRect().top < 140) current = h.id;
      });
      tocEl.querySelectorAll("a").forEach(function (a) { a.classList.toggle("active", a.getAttribute("href") === "#" + current); });
    });
  });

  themeBtn.addEventListener("click", function () {
    var next = currentTheme() === "dark" ? "light" : "dark";
    document.documentElement.setAttribute("data-theme", next);
    store("impifact-theme", next);
    updateThemeButton();
    if (hasMermaid) {
      mermaid.initialize(mermaidConfig(next));
      rendered = {};
      if (active) { renderDiagrams(document.getElementById("doc-" + active.id)); rendered[active.id] = true; }
    }
  });

  // Cross-document search: lists matching sections and jumps to them.
  function clearSearch() { searchEl.value = ""; resultsEl.hidden = true; resultsEl.innerHTML = ""; docsEl.hidden = false; }

  var searchTimer = null;
  searchEl.addEventListener("input", function () {
    clearTimeout(searchTimer);
    searchTimer = setTimeout(runSearch, 150);
  });

  function runSearch() {
    var q = searchEl.value.trim().toLowerCase();
    if (q.length < 2) { resultsEl.hidden = true; docsEl.hidden = false; return; }
    var hits = [];
    docs.forEach(function (doc) {
      var article = document.getElementById("doc-" + doc.id);
      var section = { id: doc.id, text: doc.label };
      Array.prototype.forEach.call(article.children, function (el) {
        var heading = el.matches("h1, h2, h3") ? el : null;
        if (heading) section = { id: heading.id, text: heading.textContent };
        var text = el.textContent;
        var at = text.toLowerCase().indexOf(q);
        if (at >= 0) {
          var start = Math.max(0, at - 60);
          var snippet = (start > 0 ? "…" : "") + text.slice(start, at + q.length + 80) + "…";
          hits.push({ doc: doc, section: section, snippet: snippet });
        }
      });
    });
    var re = new RegExp(q.replace(/[.*+?^${}()|[\]\\]/g, "\\$&"), "gi");
    resultsEl.innerHTML = '<article class="doc"><p class="search-info">' + hits.length + " coincidencias para «" + escapeHtml(searchEl.value.trim()) + "»</p>" +
      hits.slice(0, 200).map(function (h) {
        return '<p><a href="#' + h.section.id + '"><strong>' + escapeHtml(h.doc.label) + "</strong> › " + escapeHtml(h.section.text) +
          "</a><br>" + escapeHtml(h.snippet).replace(re, function (m) { return "<mark>" + m + "</mark>"; }) + "</p>";
      }).join("") + "</article>";
    resultsEl.hidden = false;
    docsEl.hidden = true;
  }

  fromHash();
})();
</script>
</body>
</html>
"""


def main() -> None:
    data = []
    missing = []
    for file_name, doc_id, label in DOCUMENTS:
        path = DOCS_DIR / file_name
        if not path.exists():
            missing.append(file_name)
            continue
        data.append({"id": doc_id, "label": label, "file": file_name,
                     "markdown": path.read_text(encoding="utf-8")})
    if missing:
        raise SystemExit(f"Missing documents: {', '.join(missing)}")

    # Escape "</" so document content can never close the JSON script tag.
    payload = json.dumps(data, ensure_ascii=False).replace("</", "<\\/")
    html = (TEMPLATE.replace("__DOC_DATA__", payload)
            .replace("__MARKED_URL__", MARKED_URL)
            .replace("__MERMAID_URL__", MERMAID_URL)
            .replace("__BUILD_DATE__", date.today().isoformat()))
    OUTPUT.write_text(html, encoding="utf-8")
    print(f"Wrote {OUTPUT.name} ({len(data)} documents, {OUTPUT.stat().st_size // 1024} KB)")


if __name__ == "__main__":
    main()
