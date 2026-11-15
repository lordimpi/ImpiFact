#!/usr/bin/env sh
# Start the local ApiDian reference and prepare it for golden capture.
# Usage: sh tools/golden-capture/start-reference.sh   (from the repository root)
set -eu

HERE="$(cd "$(dirname "$0")" && pwd)"
APIDIAN="$HERE/../../../apidian"

if [ ! -f "$APIDIAN/composer.json" ]; then
  echo "ApiDian checkout not found at $APIDIAN" >&2
  exit 1
fi

# CRLF line endings (git core.autocrlf=true on Windows) leak "\r" into the seeded DIAN
# catalog codes and from there into the XML. The checkout must use LF.
if grep -q "$(printf '\r')" "$APIDIAN/public/csv/type_regimes.csv"; then
  echo "ApiDian checkout has CRLF line endings. Fix it with:" >&2
  echo "  git -C <apidian> config core.autocrlf false && git -C <apidian> config core.eol lf" >&2
  echo "  git -C <apidian> rm -rq --cached . && git -C <apidian> reset -q --hard" >&2
  exit 1
fi

cd "$HERE"
docker compose up -d

docker compose exec -T php_api sh -c '
  set -e
  mkdir -p storage/framework/cache/data storage/framework/sessions storage/framework/views \
           storage/logs storage/app/public bootstrap/cache
  chmod -R 777 storage bootstrap/cache
  # Resolve for the container PHP (8.2). "php+" ignores only upper bounds: bacon-qr-code 1.x
  # still declares PHP ^7, the same exception any real ApiDian install needs.
  if [ ! -f vendor/autoload.php ]; then
    COMPOSER_PROCESS_TIMEOUT=1200 composer update --no-dev --no-interaction --no-progress --ignore-platform-req=php+
  fi
  # mPDF writes its cache inside vendor (a root-owned volume); notes render a PDF before sending.
  mkdir -p vendor/mpdf/mpdf/tmp && chmod -R 777 vendor/mpdf/mpdf/tmp
  php urn_on.php
  php artisan migrate:fresh --seed --force
'

echo "ApiDian reference ready at http://127.0.0.1:8081"
