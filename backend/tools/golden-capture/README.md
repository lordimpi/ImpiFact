# golden-capture

Genera los casos golden: el XML y el código único (CUFE, CUDE o CUDS) que produce ApiDian (PHP) para cada JSON de entrada. Son la referencia de paridad de ImpiFact.

## Cómo funciona

1. Levanta ApiDian en Docker, solo en `127.0.0.1:8081`.
2. Configura una empresa de prueba ficticia con:
   - un certificado autofirmado que se genera en cada corrida;
   - el software de facturación y el del documento soporte (`config/softwaresupportdocument`), con PIN distintos; las URL de facturación, documento soporte y eventos apuntan al nginx local;
   - la resolución de pruebas pública de la DIAN (`18760000001`, prefijo `SETP`) para la factura nacional, la de exportación y las de contingencia tipo 03 y 04, la de documento soporte (`SEDS`, sin clave técnica) y las de notas (`NC`, `ND`, `NDS`).
3. Envía cada `input.json` por uno de dos caminos:
   - **query_uuid**: la factura nacional y la del sector salud (solo `InvoiceController` lo admite) van con `"query_uuid": true`. ApiDian arma el XML, lo firma y calcula el CUFE, pero **responde antes de llamar a la DIAN**.
   - **envío fallido**: el resto de tipos no tiene ese modo. ApiDian firma, escribe el XML y luego hace el envío SOAP a la URL del software, que apunta al nginx local. Ese envío falla a propósito y el código se toma del `cbc:UUID` del XML firmado.

   En ningún caso sale tráfico hacia la DIAN.
4. Guarda junto al `input.json`:
   - `expected.xml`: el XML firmado sin el elemento `<ds:Signature>`, byte a byte;
   - `expected.cufe`: el CUFE, CUDE o CUDS;
   - `response.json`: la respuesta de ApiDian (solo el camino query_uuid).
5. Recalcula el código con los valores del propio XML, con la fórmula que exige la DIAN para su `schemeName` (ver más abajo).

La salida es determinista: fecha, hora, consecutivo, clave técnica, software y empresa son fijos. Una segunda corrida deja los mismos bytes.

## Tipos de documento

| Carpeta en `tests/golden` | Ruta de ApiDian | Camino | Código |
| --- | --- | --- | --- |
| `invoice` | `invoice` | query_uuid | CUFE |
| `invoice-health` | `invoice` (con `health_fields`) | query_uuid | CUFE |
| `invoice-export` | `invoice-export` | envío fallido | CUFE |
| `invoice-contingency` | `invoice-contingency` (tipo 03) | envío fallido | CUDE |
| `invoice-contingency-4` | `invoice-contingency-4` (tipo 04) | envío fallido | CUFE en el `schemeName`, calculado con el PIN (ver `case.json`) |
| `invoice-aiu`, `invoice-mandate`, `invoice-transport` | la de su nombre | envío fallido | CUFE |
| `credit-note`, `debit-note` | la de su nombre | envío fallido | CUDE |
| `support-document`, `sd-credit-note` | la de su nombre | envío fallido | CUDS |
| `event` | sin API: `event.php` | script | CUDE del evento |
| `attached-document` | sin API: `attached_document.php` | script | — |

Las variantes de factura tienen su propia carpeta aunque compartan ruta, para que las pruebas de la factura nacional no las tomen como sus casos.

La contingencia tipo 04 queda guardada en la base de datos de ApiDian como pendiente de transmitir, y ApiDian rechaza el mismo número la vez siguiente. Por eso la captura borra ese registro antes de enviarla.

### Metadatos y verificación del código

Los casos de las variantes llevan un `case.json` con:

- `variant`: qué variante cubre;
- `source_request`: la solicitud de la colección de Postman (`backend/tests/ImpiFact.Contract.Tests/collection`) de la que sale la entrada;
- `derived`: qué se cambió (los datos son ficticios);
- `task`: la tarea de `odd/tasks/fase-7a-community.md` que escribe su builder;
- `code_check`: lo escribe `capture.py`.

Para llenar `code_check`, `capture.py` recalcula el código con la fórmula que exige la DIAN según el `schemeName` del `cbc:UUID`:

| Fórmula | `schemeName` | Secreto |
| --- | --- | --- |
| CUFE | `CUFE-SHA384` | clave técnica |
| CUDE | `CUDE-SHA384` | PIN del software |
| CUDS | `CUDS-SHA384` | PIN del software del documento soporte; solo lleva el IVA |
| CUDE del evento | raíz `ApplicationResponse` | PIN del software |

Si el código no coincide, prueba las demás fórmulas y secretos y anota cuál usó el PHP. El golden conserva el valor del PHP tal cual: no se corrige. Los errores del PHP que se encontraron al capturar quedan en `php_bugs`.

`GoldenCodeTests` (en `tests/ImpiFact.Golden.Tests`) repite ese cálculo en cada `dotnet test`, para todos los casos, tengan o no builder.

### Eventos RADIAN (`event.php`)

La API de eventos (`send-event-data`) primero descarga de la DIAN el XML de la factura referenciada, así que no se puede usar sin la DIAN. `event.php` corre dentro del contenedor y repite el resto de `SendEventController::sendeventdata`: arma el emisor (la empresa) y el receptor (el emisor de la factura), crea el ApplicationResponse con la plantilla 88 y lo firma con `SignEvent`. El `input.json` trae:

- `now`: la fecha y hora del evento, que la plantilla toma del reloj;
- `event_id`, y opcionalmente `type_rejection_id` e `issuer_party`;
- `document_reference`: `number` (el `cbc:ID` completo), `prefix`, `uuid` y `type_document_id` de la factura recibida, que es ficticia;
- `provider`: el emisor de esa factura.

Guarda `expected.xml` y `expected.cufe` como los demás tipos.

### AttachedDocument (`attached_document.php`)

El AttachedDocument (contenedor electrónico que se entrega al comprador con el documento firmado y el ApplicationResponse, la respuesta de validación de la DIAN) solo lo arma ApiDian después de que la DIAN acepta el documento, así que no se captura por la API. Para cada caso de `tests/golden/attached-document/<caso>/`, `capture.py` ejecuta `attached_document.php` dentro del contenedor con:

- `input.json`: tipo (`invoice`, `credit-note` o `debit-note`), resolución, número, CUFE/CUDE, fecha y hora de generación (`now`, hora de Colombia) y cliente;
- `signed-document.xml`: el documento que se embebe;
- `application-response.xml`: una respuesta de la DIAN **inventada**.

Guarda `expected-attached-document.xml`: el AttachedDocument firmado por ApiDian sin `<ds:Signature>`. No se llama `expected.xml` porque las pruebas de facturas y notas toman todo `expected.xml` de su carpeta como uno de sus casos.

Los dos scripts reciben su entrada en `storage/app/public/golden-script/` de la copia de ApiDian, que `capture.py` crea y borra en cada caso.

### Lotes (no se capturan)

Los lotes (`add-to-batch/...` y `send-batch`) no tienen caso golden:

- cada documento del lote es el mismo XML firmado que la ruta individual, ya cubierto por los casos de cada tipo;
- lo propio del lote no es determinista: el nombre de cada XML dentro del ZIP sale del año en curso y de un contador de la base de datos (`DocumentTrait::getFileName`), el ZIP lleva marcas de tiempo y el sobre SOAP de `SendBillAsync` lleva las marcas de tiempo de la firma WS-Security.

La referencia de los lotes (T8) es el código PHP (`BatchController`).

## Requisitos

- Docker en marcha, Python 3.10 o superior y `openssl` en el `PATH`.
- El clon de ApiDian al lado de este repositorio (`../apidian`), **con saltos de línea LF**. Con `core.autocrlf=true` (lo normal en Windows), los catálogos de la DIAN se siembran con un `\r` pegado a cada código y el XML sale corrupto. El script lo detecta y explica cómo corregirlo.

## Uso

Desde la carpeta `backend/`:

```sh
sh tools/golden-capture/start-reference.sh      # levanta y prepara ApiDian (la primera vez tarda unos minutos)
python tools/golden-capture/capture.py          # captura todos los casos
python tools/golden-capture/capture.py --type invoice-export
python tools/golden-capture/capture.py --type event
docker compose -f tools/golden-capture/docker-compose.yml down   # apaga ApiDian
```

Cada línea `OK` dice el resultado del recálculo, por ejemplo `CUFE recomputed: match` o `CUFE recomputed: MISMATCH (PHP used CUDE with the software PIN)`.

## Agregar un caso

1. Crea `tests/golden/<tipo>/<caso>/input.json` con un tipo de la tabla de arriba. Parte de la solicitud de Postman de esa variante y cambia los datos por ficticios.
2. Usa la empresa emisora `900000001-2`, datos ficticios para el cliente o el proveedor, y un número dentro del rango de la resolución que no repita el de otro caso. Los rangos son:
   - `SETP`, de 990000000 a 995000000, compartido por todas las facturas;
   - `SEDS`, de 984000000 a 985000000;
   - `NC`, `ND` y `NDS`, de 1 a 99999999.

   El documento soporte valida el dígito de verificación del NIT del proveedor.
3. Para una variante, agrega su `case.json` (`variant`, `source_request`, `derived`, `task`).
4. Corre la captura y revisa el `expected.xml` y el `code_check` antes de hacer commit.
5. Una carpeta de tipo nueva necesita su prueba en `tests/ImpiFact.Golden.Tests` o una entrada pendiente en `VariantGoldenTests`. `Every_golden_type_folder_is_tested_or_pending` falla si falta.

## Advertencias

- ApiDian Community trae dependencias con avisos de seguridad conocidos y la imagen usa PHP 8.2.5. Esta instancia es solo para generar fixtures: no la expongas a la red.
- El `Dockerfile` de ApiDian no se usa: solo agrega `poppler-utils`, sus repositorios de `apt` ya no existen y no hace falta para los golden.
- Los fixtures se guardan sin conversión de saltos de línea (`.gitattributes`): se comparan byte a byte.
- `config/software` y `config/softwaresupportdocument` usan el mismo método de ApiDian, que vuelve a poner la URL de la DIAN en `url` si la solicitud no la trae. `capture.py` repite todas las URL locales en las dos llamadas: no quites ninguna.
- La exportación con `deliveryterms` necesita un `k_supplement` completo y con `FctConvCop`. Sin él, ApiDian arma uno que solo trae la TRM (la consulta en internet si no llega) y la plantilla falla (ver `tests/golden/invoice-export/usd-fob/case.json`).
