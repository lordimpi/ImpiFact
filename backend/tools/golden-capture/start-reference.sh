#!/usr/bin/env sh
# Inicia la referencia local de ApiDian y la prepara para la captura de casos golden.
# Uso: sh tools/golden-capture/start-reference.sh   (desde la carpeta backend/)
set -eu

HERE="$(cd "$(dirname "$0")" && pwd)"
APIDIAN="$HERE/../../../../apidian"

if [ ! -f "$APIDIAN/composer.json" ]; then
  echo "ApiDian checkout not found at $APIDIAN" >&2
  exit 1
fi

# Los finales de línea CRLF (git core.autocrlf=true en Windows) filtran "\r" en los códigos de los
# catálogos DIAN sembrados y de ahí al XML. El checkout debe usar LF.
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
  # Resolver para el PHP del contenedor (8.2). "php+" solo ignora los límites superiores: bacon-qr-code
  # 1.x aún declara PHP ^7, la misma excepción que necesita cualquier instalación real de ApiDian.
  if [ ! -f vendor/autoload.php ]; then
    COMPOSER_PROCESS_TIMEOUT=1200 composer update --no-dev --no-interaction --no-progress --ignore-platform-req=php+
  fi
  # mPDF escribe su caché dentro de vendor (un volumen de root); las notas generan un PDF antes de enviarse.
  mkdir -p vendor/mpdf/mpdf/tmp && chmod -R 777 vendor/mpdf/mpdf/tmp
  php urn_on.php
  php artisan migrate:fresh --seed --force
'

echo "ApiDian reference ready at http://127.0.0.1:8081"
