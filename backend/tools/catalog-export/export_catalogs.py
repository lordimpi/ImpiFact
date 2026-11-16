#!/usr/bin/env python3
"""Exporta los catálogos DIAN de la copia local de ApiDian (PHP) a los JSON de semilla de ImpiFact.

Lee la base de datos MariaDB del stack de referencia (backend/tools/golden-capture) con consultas de
solo lectura y escribe un archivo por tabla en backend/src/ImpiFact.Infrastructure/Seed/. Los id se
conservan tal como están en el PHP. La salida es determinista: dos corridas sobre la misma base
producen archivos idénticos byte a byte.

Solo usa la biblioteca estándar de Python. Uso: python -I export_catalogs.py [--check]
"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
from pathlib import Path

# Columnas comunes de todos los catálogos: (clave JSON de ImpiFact, columna PHP).
BASE = (("id", "id"), ("code", "code"), ("name", "name"))

# Tablas de catálogo de la paridad Community, con las columnas extra en el orden del modelo de datos.
# Se omite health_type_document_identifications: es de RIPS (Enterprise).
TABLES: dict[str, tuple[tuple[str, str], ...]] = {
    "countries": BASE,
    "credit_note_discrepancy_responses": BASE,
    "credit_note_discrepancy_responses_sd": BASE,
    "debit_note_discrepancy_responses": BASE,
    "departments": BASE + (("country_id", "country_id"),),
    "discounts": BASE,
    "events": BASE,
    "incoterms": BASE,
    "languages": BASE,
    "municipalities": BASE + (("department_id", "department_id"), ("codefacturador", "codefacturador")),
    "payment_forms": BASE,
    "payment_methods": BASE,
    "prepaid_payment_types": BASE,
    "reference_prices": BASE,
    "taxes": BASE + (("description", "description"),),
    "type_currencies": BASE,
    "type_discounts": BASE,
    "type_document_identifications": BASE + (("rips_code", "code_rips"),),
    "type_documents": BASE + (("cufe_algorithm", "cufe_algorithm"), ("prefix", "prefix")),
    "type_environments": BASE,
    "type_generation_transmitions": BASE,
    # code_agency no figura en el modelo de datos, pero el XML lo usa (schemeAgencyID): se conserva.
    "type_item_identifications": BASE + (("code_agency", "code_agency"),),
    "type_liabilities": BASE,
    "type_operations": BASE,
    "type_organizations": BASE,
    "type_regimes": BASE,
    "type_rejections": BASE,
    "type_spds": BASE,
    "unit_measures": BASE,
}

# Correcciones a errores de datos del PHP: (tabla, id, columna) -> (valor en el PHP, valor correcto).
# Si el PHP cambia ese valor, el exportador se detiene para que alguien revise la corrección.
CORRECTIONS: dict[tuple[str, int, str], tuple[str, str]] = {
    # El PHP repite el código de Eslovenia (SI) en Suecia; el código ISO 3166 de Suecia es SE.
    ("countries", 218, "code"): ("SI", "SE"),
}

DEFAULT_OUTPUT = Path(__file__).resolve().parents[2] / "src" / "ImpiFact.Infrastructure" / "Seed"

# Credenciales de la copia local, las mismas de backend/tools/golden-capture/docker-compose.yml.
DEFAULT_CONTAINER = "apidian-reference-db-1"
DEFAULT_DATABASE = "db_api"
DEFAULT_USER = "user_api"
DEFAULT_PASSWORD = "password_api"


def run_query(args: argparse.Namespace, sql: str) -> list[str]:
    """Ejecuta una consulta en el contenedor y devuelve las líneas de salida sin formato."""
    command = [
        "docker", "exec", "-e", f"MYSQL_PWD={args.password}", args.container,
        "mariadb", "--default-character-set=utf8mb4", f"--user={args.user}",
        "--batch", "--raw", "--skip-column-names", args.database, "-e", sql,
    ]
    result = subprocess.run(command, capture_output=True, check=False)
    if result.returncode != 0:
        stderr = result.stderr.decode("utf-8", errors="replace").strip()
        sys.exit(f"Query failed ({sql[:60]}...): {stderr}\nIs the reference stack up? See README.md.")
    return [line for line in result.stdout.decode("utf-8").split("\n") if line]


def export_table(args: argparse.Namespace, table: str, columns: tuple[tuple[str, str], ...]) -> list[dict]:
    pairs = ", ".join(f"'{key}', `{column}`" for key, column in columns)
    lines = run_query(args, f"SELECT JSON_OBJECT({pairs}) FROM `{table}` ORDER BY `id`")
    rows = []
    for line in lines:
        raw = json.loads(line)
        rows.append({key: raw[key] for key, _ in columns})
    apply_corrections(table, rows)

    (count,) = run_query(args, f"SELECT COUNT(*) FROM `{table}`")
    if int(count) != len(rows):
        sys.exit(f"{table}: exported {len(rows)} rows but the table has {count}")
    ids = [row["id"] for row in rows]
    if any(not isinstance(i, int) for i in ids) or ids != sorted(set(ids)):
        sys.exit(f"{table}: ids are not unique integers in ascending order")
    for row in rows:
        for key, value in row.items():
            # Un \r pegado a un valor es el síntoma de un clon de ApiDian con saltos de línea CRLF
            # (ver golden-capture). Los espacios finales sí se conservan: así están en el PHP.
            if isinstance(value, str) and any(c in value for c in "\r\n\t"):
                sys.exit(f"{table} id {row['id']}: column {key} has control characters: {value!r}")
    return rows


def apply_corrections(table: str, rows: list[dict]) -> None:
    for (corrected_table, row_id, column), (php_value, fixed_value) in CORRECTIONS.items():
        if corrected_table != table:
            continue
        row = next((r for r in rows if r["id"] == row_id), None)
        if row is None or row[column] != php_value:
            found = None if row is None else row[column]
            sys.exit(f"{table} id {row_id}: expected {column}={php_value!r} in the PHP data, found {found!r}; review CORRECTIONS")
        row[column] = fixed_value


def render(rows: list[dict]) -> bytes:
    """Un objeto por línea, claves en orden fijo, UTF-8 sin BOM y saltos de línea LF."""
    if not rows:
        return b"[]\n"
    body = ",\n".join("  " + json.dumps(row, ensure_ascii=False, separators=(", ", ": ")) for row in rows)
    return f"[\n{body}\n]\n".encode("utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description="Export DIAN catalogs from the ApiDian reference DB to seed JSON files.")
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT, help="seed directory (default: %(default)s)")
    parser.add_argument("--container", default=DEFAULT_CONTAINER)
    parser.add_argument("--database", default=DEFAULT_DATABASE)
    parser.add_argument("--user", default=DEFAULT_USER)
    parser.add_argument("--password", default=DEFAULT_PASSWORD)
    parser.add_argument("--check", action="store_true", help="compare with the existing files instead of writing them")
    args = parser.parse_args()

    differences = 0
    for table in sorted(TABLES):
        rows = export_table(args, table, TABLES[table])
        content = render(rows)
        path = args.output / f"{table}.json"
        if args.check:
            same = path.is_file() and path.read_bytes() == content
            differences += 0 if same else 1
            print(f"{table}: {len(rows)} rows, {'OK' if same else 'DIFFERS'}")
        else:
            args.output.mkdir(parents=True, exist_ok=True)
            path.write_bytes(content)
            print(f"{table}: {len(rows)} rows")

    if args.check and differences:
        print(f"{differences} file(s) differ from the reference DB")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
