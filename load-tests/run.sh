#!/usr/bin/env bash
# Runs the ImpiFact load tests against a throwaway local stack (docs/Pruebas de carga.md).
#
# Usage, from the repository root (bash; on Windows, Git Bash):
#   LOAD_DIR=<folder outside the repository> load-tests/run.sh <command> [args]
#
# Commands, in the usual order:
#   secrets            create throwaway secrets and <LOAD_DIR>/load.env (never reuses real ones)
#   up                 build and start the stack (PostgreSQL; DB=sqlserver for SQL Server)
#   seed               create the demo company and save its API key in <LOAD_DIR>/api-key
#   k6 <script> [...]  run a k6 scenario, for example: k6 scenarios/issue-invoices.js -e VUS=20
#   fake-dian-stats    requests the fake DIAN received, by operation and by wsa:To host
#   egress-check       prove that the project network cannot reach the real DIAN
#   sql <file>         run a SQL file on the stack's database (psql on PostgreSQL, sqlcmd on SQL Server)
#   lock-watch [secs]  SQL Server only: sample outbox locks every second (sql/lock-watch-sqlserver.sql), as CSV
#   compose [...]      any other docker compose command on this stack (logs, ps, stop worker…)
#   down               stop the stack and delete its volumes and the images it built
#
# Environment: LOAD_DIR (required), DB (postgres by default, or sqlserver), PROJECT (impifact-t10 by default).
set -euo pipefail

# Git Bash would turn container paths such as /load-tests into Windows paths.
export MSYS_NO_PATHCONV=1

# pwd -W (Git Bash) gives C:/… paths, which docker understands; elsewhere plain pwd.
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && (pwd -W 2>/dev/null || pwd))"
LOAD_DIR="${LOAD_DIR:?Set LOAD_DIR to a folder outside the repository for the secrets and the results}"
PROJECT="${PROJECT:-impifact-t10}"
DB="${DB:-postgres}"

files=(-f "$ROOT/deploy/docker-compose.yml" -f "$ROOT/load-tests/compose.load.yml")
profiles=(--profile app --profile load)
if [ "$DB" = "sqlserver" ]; then
  files+=(-f "$ROOT/load-tests/compose.sqlserver.yml")
  profiles+=(--profile sqlserver)
fi

compose() {
  docker compose -p "$PROJECT" --env-file "$LOAD_DIR/load.env" "${files[@]}" "${profiles[@]}" "$@"
}

case "${1:-}" in
  secrets)
    mkdir -p "$LOAD_DIR/results"
    if [ -f "$LOAD_DIR/load.env" ]; then
      echo "$LOAD_DIR/load.env already exists; delete it to create new secrets." >&2
      exit 1
    fi
    openssl rand -base64 32 > "$LOAD_DIR/master.key"
    jwt="$(openssl ecparam -name prime256v1 -genkey -noout | openssl pkcs8 -topk8 -nocrypt | awk 'BEGIN { ORS = "\\n" } { print }')"
    {
      echo "POSTGRES_PASSWORD=$(openssl rand -hex 24)"
      # SQL Server asks for upper and lower case letters, digits and symbols.
      echo "MSSQL_SA_PASSWORD=Lt$(openssl rand -hex 16)!9a"
      echo "JWT_SIGNING_KEY=\"$jwt\""
      echo "IMPIFACT_MASTER_KEY_FILE=$LOAD_DIR/master.key"
      echo "K6_RESULTS_DIR=$LOAD_DIR/results"
      # seed-demo creates fictitious data; Development would also open Scalar. Production is what a server runs.
      echo "APP_ENVIRONMENT=Production"
    } > "$LOAD_DIR/load.env"
    echo "Throwaway secrets written to $LOAD_DIR (load.env, master.key)."
    ;;
  up)
    compose build migrator api worker
    compose up -d --wait migrator api worker
    compose ps
    ;;
  seed)
    compose run --rm migrator seed-demo --force | tee "$LOAD_DIR/seed-demo.log" | grep -v '^apk_' || true
    grep -o '^apk_[A-Za-z0-9_-]*' "$LOAD_DIR/seed-demo.log" | tail -n 1 > "$LOAD_DIR/api-key"
    sed -i '/^apk_/d' "$LOAD_DIR/seed-demo.log"
    if [ ! -s "$LOAD_DIR/api-key" ]; then
      echo "seed-demo did not print an API key; see $LOAD_DIR/seed-demo.log" >&2
      exit 1
    fi
    echo "API key saved in $LOAD_DIR/api-key."
    ;;
  k6)
    shift
    script="$1"
    shift
    IMPIFACT_API_KEY="$(cat "$LOAD_DIR/api-key")" compose run --rm -e IMPIFACT_API_KEY k6 run "$@" "$script"
    ;;
  fake-dian-stats)
    compose exec -T fake-dian python -c "import urllib.request; print(urllib.request.urlopen('http://127.0.0.1:8080/stats').read().decode())"
    ;;
  egress-check)
    network="${PROJECT}_default"
    docker network inspect "$network" --format 'network {{.Name}}: internal={{.Internal}}'
    for url in https://vpfe-hab.dian.gov.co/WcfDianCustomerServices.svc https://vpfe.dian.gov.co/WcfDianCustomerServices.svc; do
      if docker run --rm --network "$network" python:3.13.13-alpine3.23 python -c \
        "import urllib.request,sys; urllib.request.urlopen(sys.argv[1], timeout=5)" "$url" >/dev/null 2>&1; then
        echo "FAIL: $url is reachable from $network" >&2
        exit 1
      fi
      echo "ok: $url is not reachable from $network"
    done
    ;;
  sql)
    if [ "$DB" = "sqlserver" ]; then
      compose exec -T sqlserver bash -c '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -d impifact -W' < "$2"
    else
      compose exec -T postgres psql -U impifact -d impifact < "$2"
    fi
    ;;
  lock-watch)
    [ "$DB" = "sqlserver" ] || { echo "lock-watch needs DB=sqlserver" >&2; exit 1; }
    end=$(( $(date +%s) + ${2:-600} ))
    echo "at_utc,pending,processing,escalation_attempts,escalations,table_locks,row_page_locks,blocked,max_block_ms,blocked_on"
    while [ "$(date +%s)" -lt "$end" ]; do
      compose exec -T sqlserver bash -c '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -d impifact -h -1 -W -s ","'         < "$ROOT/load-tests/sql/lock-watch-sqlserver.sql" || true
      sleep 1
    done
    ;;
  compose)
    shift
    compose "$@"
    ;;
  down)
    compose down -v --remove-orphans
    docker image rm impifact-t10-migrator:local impifact-t10-api:local impifact-t10-worker:local 2>/dev/null || true
    ;;
  *)
    sed -n '2,20p' "$0"
    exit 1
    ;;
esac
