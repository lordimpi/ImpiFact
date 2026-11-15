# golden-capture

Genera los casos golden: el XML y el CUFE/CUDE que produce ApiDian (PHP) para cada JSON de entrada. Son la referencia de paridad de ImpiFact.

## Cómo funciona

1. Levanta ApiDian en Docker, solo en `127.0.0.1:8081`.
2. Configura una empresa de prueba ficticia con un certificado autofirmado que se genera en cada corrida, la resolución de pruebas pública de la DIAN (`18760000001`, prefijo `SETP`) y resoluciones para notas.
3. Envía cada `input.json`. Las facturas van con `"query_uuid": true`: ApiDian arma el XML, lo firma y calcula el CUFE, pero **responde antes de llamar a la DIAN**. Las notas no tienen ese modo: ApiDian firma, escribe el XML y luego hace el envío SOAP a la URL del software, que apunta al nginx local; ese envío falla a propósito y el CUDE se toma del `cbc:UUID` del XML firmado. En ningún caso sale tráfico hacia la DIAN.
4. Guarda junto al `input.json`:
   - `expected.xml`: el XML firmado sin el elemento `<ds:Signature>`, byte a byte;
   - `expected.cufe`: el CUFE o CUDE;
   - `response.json`: la respuesta de ApiDian (solo facturas).

La salida es determinista: fecha, hora, consecutivo, clave técnica, software y empresa son fijos.

## Requisitos

- Docker en marcha, Python 3.10 o superior y `openssl` en el `PATH`.
- El clon de ApiDian al lado de este repositorio (`../apidian`), **con saltos de línea LF**. Con `core.autocrlf=true` (lo normal en Windows), los catálogos de la DIAN se siembran con un `\r` pegado a cada código y el XML sale corrupto. El script lo detecta y explica cómo corregirlo.

## Uso

Desde la raíz del repositorio:

```sh
sh tools/golden-capture/start-reference.sh      # levanta y prepara ApiDian (la primera vez tarda unos minutos)
python tools/golden-capture/capture.py          # captura todos los casos
python tools/golden-capture/capture.py --type invoice
docker compose -f tools/golden-capture/docker-compose.yml down   # apaga ApiDian
```

## Agregar un caso

1. Crea `tests/golden/<tipo>/<caso>/input.json`. Los tipos son `invoice`, `credit-note` y `debit-note`.
2. Usa la empresa emisora `900000001-2`, datos ficticios para el cliente, y un número dentro del rango de la resolución (`SETP` 990000000 a 995000000; `NC` y `ND` de 1 a 99999999) que no repita el de otro caso.
3. Corre la captura y revisa el `expected.xml` antes de hacer commit.

## Advertencias

- ApiDian Community trae dependencias con avisos de seguridad conocidos y la imagen usa PHP 8.2.5. Esta instancia es solo para generar fixtures: no la expongas a la red.
- El `Dockerfile` de ApiDian no se usa: solo agrega `poppler-utils`, sus repositorios de `apt` ya no existen y no hace falta para los golden.
- Los fixtures se guardan sin conversión de saltos de línea (`.gitattributes`): se comparan byte a byte.
