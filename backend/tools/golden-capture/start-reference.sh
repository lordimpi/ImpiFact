#!/usr/bin/env sh
# Inicia la copia local de ApiDian (el PHP original) y la prepara para capturar los casos de
# referencia: el XML y el CUFE/CUDE que ImpiFact debe reproducir byte a byte (ver capture.py).
# Uso: sh tools/golden-capture/start-reference.sh   (desde la carpeta backend/)
set -eu

HERE="$(cd "$(dirname "$0")" && pwd)"
APIDIAN="$HERE/../../../../apidian"

if [ ! -f "$APIDIAN/composer.json" ]; then
  echo "ApiDian checkout not found at $APIDIAN" >&2
  exit 1
fi

# Si el código de ApiDian se clonó con finales de línea de Windows (CRLF, por git core.autocrlf=true),
# un "\r" queda pegado a los códigos de los catálogos DIAN que se cargan en la base de datos, y de ahí
# pasa al XML. La copia de ApiDian debe usar finales LF.
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
  # Instala las dependencias con Composer, eligiendo versiones compatibles con el PHP del contenedor (8.2).
  # --ignore-platform-req=php+ solo ignora el límite máximo de versión de PHP que declaran los paquetes:
  # bacon-qr-code 1.x aún declara PHP ^7 (solo 7.x), la misma excepción que necesita cualquier
  # instalación real de ApiDian.
  if [ ! -f vendor/autoload.php ]; then
    COMPOSER_PROCESS_TIMEOUT=1200 composer update --no-dev --no-interaction --no-progress --ignore-platform-req=php+
  fi
  # mPDF (la biblioteca que genera PDF) escribe su caché dentro de vendor, un volumen cuyo dueño es root;
  # las notas generan un PDF antes de enviarse, así que esa carpeta debe permitir escritura.
  mkdir -p vendor/mpdf/mpdf/tmp && chmod -R 777 vendor/mpdf/mpdf/tmp
  php urn_on.php
  # Borra y recrea la base de datos y carga los catálogos iniciales.
  php artisan migrate:fresh --seed --force
'

echo "ApiDian reference ready at http://127.0.0.1:8081"
