# catalog-export

Regenera los JSON de semilla de los catálogos DIAN (las listas de códigos que publica la Dirección de Impuestos y Aduanas Nacionales en el anexo técnico de facturación electrónica) a partir de ApiDian, el sistema PHP original. Escribe un archivo por tabla en `backend/src/ImpiFact.Infrastructure/Seed/`.

## Requisitos

- La copia local de ApiDian en marcha (`backend/tools/golden-capture`, ver su README): el script lee la base de datos del contenedor `apidian-reference-db-1` con consultas de solo lectura.
- Docker en el `PATH` y Python 3.10 o superior. Solo usa la biblioteca estándar.

## Uso

Desde la carpeta `backend/`:

```sh
sh tools/golden-capture/start-reference.sh                    # si la copia de referencia no está en marcha
python -I tools/catalog-export/export_catalogs.py             # reescribe los JSON
python -I tools/catalog-export/export_catalogs.py --check     # solo compara; sale con código 1 si algo cambió
```

`--container`, `--database`, `--user` y `--password` cambian la conexión; los valores por defecto son los de `tools/golden-capture/docker-compose.yml`.

La salida es determinista: filas ordenadas por `id`, claves en orden fijo, un objeto por línea, UTF-8 sin BOM y saltos de línea LF. Dos corridas sobre la misma base dan archivos idénticos byte a byte. El script falla si el número de filas exportadas no coincide con `COUNT(*)`, si los `id` no son enteros únicos o si un valor trae `\r`, `\n` o tabulador (síntoma de un clon de ApiDian con saltos de línea CRLF).

## De dónde salen los datos

El estado final de cada catálogo en el PHP resulta de tres fuentes, en este orden:

1. Las migraciones (`database/migrations`), que crean las tablas y agregan o corrigen filas.
2. El seeder `ConfigurationSeeder`, que carga `public/csv/<tabla>.csv` con `LOAD DATA ... IGNORE` (no pisa filas que una migración ya insertó).
3. `ActualizarTablas()` (`app/Traits/DocumentTrait.php`), que se ejecuta antes de cada documento y vuelve a escribir algunos catálogos.

Por eso se exporta la base de datos y no los CSV: varias migraciones insertan filas con `create` y luego les cambian el `id`, así que el `id` real solo se ve en la base. La copia de referencia ya emitió documentos, de modo que refleja también el paso 3.

## Tablas

Las 29 tablas de catálogo de la paridad Community que lista `docs/Modelo de datos detallado.md` (sección 3.1). Se omite `health_type_document_identifications`: es de RIPS (el registro de prestación de servicios de salud), una función Enterprise.

## Columnas

Todas las tablas exportan `id`, `code` y `name`. Las extras, con su nombre en ImpiFact:

| Tabla | Columnas extra | Columna PHP |
| --- | --- | --- |
| `type_documents` | `cufe_algorithm`, `prefix` | igual |
| `taxes` | `description` | igual |
| `type_document_identifications` | `rips_code` | `code_rips` |
| `type_item_identifications` | `code_agency` | igual; no figura en el modelo de datos, pero el XML lo usa como `schemeAgencyID` |
| `departments` | `country_id` | igual |
| `municipalities` | `department_id`, `codefacturador` | igual |

No se exportan `created_at` ni `updated_at`. Tampoco `is_active`, `valid_from` ni `valid_to`: el PHP no los tiene y la semilla de ImpiFact usa sus valores por defecto (`true`, `null`, `null`).

Los valores se copian tal cual, incluidos los espacios finales de algunos nombres (219 municipios y 3 medios de pago) y la diferencia entre `""` y `null` en `type_documents`.

La única excepción son las correcciones de `CORRECTIONS` en `export_catalogs.py`: errores de datos del PHP que ImpiFact corrige a propósito, como el código de Suecia (`SI` en el PHP, `SE` según ISO 3166). Cada corrección guarda el valor que se espera encontrar en el PHP; si ese valor cambia, el exportador se detiene para que alguien la revise. Por eso `--check` informa `countries` como distinto de la base de referencia: es esperado.
