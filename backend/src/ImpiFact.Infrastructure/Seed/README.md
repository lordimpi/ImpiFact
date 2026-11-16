# Semillas de catálogos

Un archivo JSON por tabla de catálogo DIAN (las listas de códigos del anexo técnico de facturación electrónica de la Dirección de Impuestos y Aduanas Nacionales): tipos de documento, impuestos, municipios, unidades de medida y demás. Cada archivo es un arreglo de objetos con claves en snake_case, una fila por línea, ordenado por `id`.

- Los `id` son los mismos de ApiDian, el sistema PHP original. No se renumeran: otras tablas y los clientes de la API los usan como referencia.
- Los valores se copian tal cual del PHP, incluidos acentos, espacios finales y la diferencia entre `""` y `null`.
- No se editan a mano. Se regeneran desde la copia local de ApiDian con `python -I tools/catalog-export/export_catalogs.py` (desde `backend/`); ver `backend/tools/catalog-export/README.md`, que explica también qué tablas se incluyen y cómo se nombran las columnas.
- Un cambio de catálogo posterior (un anexo DIAN nuevo) entra como migración de datos con `valid_from`, no editando una fila existente.
