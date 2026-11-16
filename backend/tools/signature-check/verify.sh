#!/usr/bin/env bash
# Verifica las firmas XAdES-EPES producidas por ImpiFact.Dian con xmlsec1 (libxmlsec1 + OpenSSL),
# una implementación independiente de .NET.
# Uso: verify.sh <directorio con signer.pem y signed/*.xml>
set -euo pipefail

IMAGE="alpine:3.20"

if [[ $# -ne 1 || ! -f "$1/signer.pem" || ! -d "$1/signed" ]]; then
  echo "Uso: $0 <directorio con signer.pem y signed/*.xml>" >&2
  exit 2
fi

sample_dir="$(cd "$1" && (pwd -W 2>/dev/null || pwd))"

# MSYS_NO_PATHCONV evita que Git Bash en Windows reescriba las rutas del contenedor.
MSYS_NO_PATHCONV=1 docker run --rm -v "${sample_dir}:/work:ro" "$IMAGE" sh -euc '
  apk add --no-cache xmlsec >/dev/null
  xmlsec1 --version

  verify() {
    # xades:SignedProperties lleva un atributo "Id" simple que hay que indicarle a xmlsec1.
    xmlsec1 --verify --trusted-pem /work/signer.pem \
      --id-attr:Id "http://uri.etsi.org/01903/v1.3.2#:SignedProperties" "$1"
  }

  count=0
  for file in /work/signed/*.xml; do
    name="${file##*/}"
    echo "== $name"
    verify "$file"

    # Una copia con cbc:PayableAmount alterado debe ser rechazada.
    sed -E "s#(<cbc:PayableAmount[^>]*>)#\11#" "$file" > /tmp/tampered.xml
    if cmp -s "$file" /tmp/tampered.xml; then
      echo "No se pudo alterar cbc:PayableAmount en $name" >&2
      exit 1
    fi
    if verify /tmp/tampered.xml >/dev/null 2>&1; then
      echo "ERROR: la copia alterada de $name se verifico" >&2
      exit 1
    fi
    echo "Copia alterada rechazada"
    count=$((count + 1))
  done

  if [ "$count" -eq 0 ]; then
    echo "No hay archivos en signed/" >&2
    exit 1
  fi
  echo "Resultado: $count firmas validas; $count copias alteradas rechazadas"
'
