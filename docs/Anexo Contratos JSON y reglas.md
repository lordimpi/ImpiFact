# Anexo A: Contratos JSON (Postman «FacturaLatam ApiDianV2.1») y reglas del manual

Origen: colección pública de Postman (185 requests en 14 carpetas) y manual.facturalatam.com. Tipos abreviados: `n` número, `s` string, `b` boolean. Es el contrato que los *contract tests* (puerta de salida de la fase 4) deben respetar byte a byte. Los tokens/JWT que trae la colección pública NO se copian: las pruebas usan credenciales propias.

## A.1 Factura / documento de venta — POST /ubl2.1/invoice

Campos raíz: `number:n, type_document_id:n, date:s, time:s, resolution_number:s, prefix:s, establishment_name/address/phone:s, establishment_municipality:n, establishment_email:s, send_whatsapp:b`.

- `customer`: identification\_number:n, dv:n, name, phone, address, email, merchant\_registration:s, type\_document\_identification\_id, type\_organization\_id, type\_liability\_id, municipality\_id, type\_regime\_id (todos n).
- `payment_form`: payment\_form\_id, payment\_method\_id (n), payment\_due\_date:s, duration\_measure:s.
- `legal_monetary_totals`: line\_extension\_amount, tax\_exclusive\_amount, tax\_inclusive\_amount, allowance\_total\_amount, payable\_amount (todos s, decimales como texto).
- `tax_totals[]`: tax\_id:n, tax\_amount:s, percent:s, taxable\_amount:s.
- `invoice_lines[]`: unit\_measure\_id:n, invoiced\_quantity:s, line\_extension\_amount:s, free\_of\_charge\_indicator:b, allowance\_charges\[\] (charge\_indicator:b, allowance\_charge\_reason, amount, base\_amount), tax\_totals\[\], description, notes, code:s, type\_item\_identification\_id:n, price\_amount:s, base\_quantity:s.

## A.2 Nota crédito — POST /ubl2.1/credit-note

Igual que factura con: `billing_reference{number, uuid, issue_date}`, `discrepancyresponsecode:n` (1,2,3), `discrepancyresponsedescription`, `resolution_number:n`, `sendmail:b, sendmailtome:b`, `seze:s`, `head_note`, `foot_note`, `credit_note_lines[]` en lugar de invoice\_lines. El cliente no lleva type\_liability\_id.

## A.3 Nota débito — POST /ubl2.1/debit-note

Igual que crédito con `debit_note_lines[]`, `requested_monetary_totals` (en vez de legal\_monetary\_totals) y `allowance_charges[]` a nivel documento con `discount_id`.

## A.4 Nómina — POST /ubl2.1/payroll/{id} (Enterprise, fase 8)

Raíz: type\_document\_id, establishment\_\*, head\_note, foot\_note, `novelty{novelty:b, uuidnov}`, `period{admision_date, settlement_start_date, settlement_end_date, worked_time, issue_date}`, sendmail, sendmailtome, worker\_code, prefix, consecutive:n, payroll\_period\_id:n, notes.

- `worker`: type\_worker\_id, sub\_type\_worker\_id, payroll\_type\_document\_identification\_id, municipality\_id, type\_contract\_id (n), high\_risk\_pension:b, integral\_salarary:b (sic, typo del origen), identification\_number, surname, second\_surname, first\_name, middle\_name, address, salary, email.
- `payment{payment_method_id, bank_name, account_type, account_number}`, `payment_dates[{payment_date}]`.
- `accrued{worked_days, salary, transportation_allowance, accrued_total}`, `deductions{eps_type_law_deductions_id, eps_deduction, pension_type_law_deductions_id, pension_deduction, deductions_total}`.

## A.5 Configuración y utilitarios

| Endpoint | Cuerpo / nota |
| --- | --- |
| POST /ubl2.1/config/{nit}/{dv} | type\_document\_identification\_id, type\_organization\_id, type\_regime\_id, type\_liability\_id (o `type_liability_ids[]` para varias responsabilidades), datos de empresa |
| PUT /ubl2.1/config/resolution | type\_document\_id, from, to, prefix, resolution (+ clave técnica y fechas en la versión completa) |
| PUT /ubl2.1/config/environment | Un entero por producto: factura\_ventas, nomina, … (1 = producción, 2 = habilitación) |
| POST /ubl2.1/regenerate-attached-document | `cufe` **o** `PREFIX`+`NUMBER` |
| POST /ubl2.1/regeneratepdf/{prefix}/{nit}/{hash} | sin cuerpo |
| PUT /templates | type, id (plantilla PDF) |
| PUT /ubl2.1/certificate-end-date, GET /ubl2.1/certificates-listing/{nit} | gestión de certificados |
| POST /ubl2.1/delete/company/{nit}/{dv} | borrado de empresa (acción destructiva: exigir confirmación y rol admin) |
| GET /ubl2.1/plan/query, infoplanuser, queryusersbyplan/{id} | planes y consumo |
| GET/PUT /whatsapp-config, POST /whatsapp-config/send-message (Enterprise, fase 8) | url\_whatsapp, token\_whatsapp; number, message |
| POST /extractor/company/stream, GET /extractor/company/jobs/{job\_id} | url, tipo, fechaDesde, fechaHasta; trabajo asíncrono con polling (Enterprise) |
| Consultas | GET /information/{nit}/{desde}/{hasta}, /total\_documents/{nit}/{desde}/{hasta}, /customer/{tipo}/{nit}, /ubl2.1/SearchCompany/{nit}, /ubl2.1/name-by-nit/{nit}, /ubl2.1/invoice/current\_number/{tipo}, /ubl2.1/emailconfig |
| Descargas | /ubl2.1/download/{nit}/{archivo}/BASE64, /invoice/{nit}/{pdf}, /receivedfile/{nit}/{xml}, POST /ubl2.1/xml/document/{hash}, POST /ubl2.1/status/events-document/{hash} |
| Contingencia | POST /ubl2.1/invoice-contingency-4/send\_pendings |

### A.5.1 Consultas de documentos (ImpiFact, fase 3)

Todas piden una credencial (API key o JWT) con alcance `documents:read`; sin credencial, 401; sin el alcance, 403. El PHP dejaba `information` y `total_documents` públicas («php-rules», sección 5.1).

| Ruta | Alias | Respuesta |
| --- | --- | --- |
| GET /api/information/{nit}[/{desde}[/{hasta}]] | /api/v1/information/... | `{success, message: "NIT Encontrado", data: [{type_document_id, name, count, documents}], company}` con los tipos 1, 4, 5 y 15 |
| GET /api/information/{nit}/page/{page}/page | /api/v1/information/... | igual, sin `type_document_id` ni filtro de fechas; 100 por tipo y página, del más antiguo al más nuevo; `page` menor que 1 es la primera |
| GET /api/total\_documents/{nit}[/{desde}[/{hasta}]] | /api/v1/total\_documents/... | `{success, message, data: [{name, count}], company}` con los 8 tipos del PHP en su orden: 1, 4, 5, 11, 13, 15, 26, 25 |
| GET /api/v1/documents | — | `{items: [resumen], next_cursor}` |
| GET /api/v1/documents/{id} | — | detalle |
| GET /api/v1/documents/by-cufe/{cufe} | — | detalle |

- **Fechas** (`desde`, `hasta`, `from`, `to`): `yyyy-MM-dd`, días de calendario en hora Colombia, ambos incluidos. Con una sola fecha se consulta ese día; sin fechas, de 1900-01-01 a 2100-01-01, como el PHP. Una fecha inválida responde 422 («desde no es una fecha válida.»), donde el PHP fallaba con un 500. Las dos rutas cuentan el día `hasta` completo (el `total_documents` del PHP comparaba contra la medianoche y lo dejaba por fuera).
- **Estado**: `information` y `total_documents` solo muestran los documentos validados por la DIAN (`state_document_id = 1`: `Accepted` y `AcceptedWithWarnings`, Modelo de datos, sección 4).
- **NIT**: debe ser el de la empresa de la credencial. Si no, 403 `company.forbidden` con el mismo cuerpo exista o no el NIT. `company` es la razón social.
- **Documento heredado** (`documents[]`): `id`, `identification_number` (NIT del emisor), `state_document_id`, `type_document_id`, `prefix`, `number`, `cufe`, `customer` (identificación del adquiriente), `client` (adquiriente con los nombres del objeto `customer` de la emisión), `currency_id`, `date_issue` (`yyyy-MM-dd HH:mm:ss`, hora Colombia), `sale` y `total` (total a pagar), `total_discount`, `total_tax` (con impuestos menos sin impuestos), `subtotal` (base de las líneas), `ambient_id`. No trae los nombres de archivo `xml`/`pdf` del PHP.
- **Listado** (`/api/v1/documents`): del más reciente al más antiguo. Filtros `document_type_id`, `status` (nombre del estado sin distinguir mayúsculas: `accepted`, `rejected`…), `from`, `to`, `limit` (50 por defecto, de 1 a 200) y `cursor`. `next_cursor` es un texto opaco con la posición `(issued_at, id)` del último documento de la página; se envía tal cual en `?cursor=` y es nulo en la última página. No usa OFFSET, así no repite ni salta documentos aunque entren nuevos.
- **Resumen**: `id`, `document_type_id`, `type_environment_id`, `prefix`, `number`, `full_number`, `status` (nombre), `cufe`, `issue_date`, `issue_time` (hora Colombia), `issued_at` (UTC), `currency_id`, `customer` (copia del adquiriente al emitir), `totals` (`line_extension_amount`, `tax_exclusive_amount`, `tax_inclusive_amount`, `allowance_total_amount`, `charge_total_amount`, `pre_paid_amount`, `payable_amount`).
- **Detalle**: el resumen más `uuid_algorithm`, `resolution_id`, `reference_document_id`, `type_operation_id`, `exchange_rate`, `discrepancy_code`, `discrepancy_description`, `notes`, `head_note`, `foot_note`, `track_id`, `dian_status_code`, `dian_status_description`, `sent_at`, `accepted_at`, `taxes` (`tax_id`, `percent`, `taxable_amount`, `tax_amount`, `per_unit_amount`, `unit_measure_id`) y `lines` (`line_number`, `code`, `description`, `notes`, `type_item_identification_id`, `unit_measure_id`, `invoiced_quantity`, `base_quantity`, `price_amount`, `line_extension_amount`, `free_of_charge_indicator`, `reference_price_id`, `tax_totals`). Un documento de otra empresa responde 404 `document.not_found`, igual que uno que no existe.

### A.5.2 Emisión de factura y notas (ImpiFact, fase 3)

Piden una credencial de empresa con alcance `documents:write` (sin credencial, 401; sin el alcance o con una llave de plataforma, 403). La empresa es siempre la de la credencial: el cuerpo heredado no trae el NIT del emisor. En la fase 3 no se envía nada a la DIAN: el documento queda firmado y en `Queued` con el mensaje `SendDocumentToDian` en la bandeja de salida (supuesto B7).

| Ruta heredada | Alias canónico | Tipo |
| --- | --- | --- |
| POST /api/ubl2.1/invoice[/{testSetId}] | POST /api/v1/documents/invoices[/test-set/{testSetId}] | 1 |
| POST /api/ubl2.1/credit-note[/{testSetId}] | POST /api/v1/documents/credit-notes[/test-set/{testSetId}] | 4 |
| POST /api/ubl2.1/debit-note[/{testSetId}] | POST /api/v1/documents/debit-notes[/test-set/{testSetId}] | 5 |

- **Cuerpo**: el de A.1 a A.3. Los números llegan como texto o número y se conservan tal cual para el XML; un opcional vacío para PHP (`""`, `"0"`, `0`, `false`) no se escribe. `payment_form` puede ser un objeto o una lista de un elemento (por defecto forma 1 y medio 10). Los valores por defecto del cliente son los del PHP (identificación 3, organización 2, régimen 2, responsabilidad 117, impuesto 1, país 46, idioma 79). Los textos pierden CR, LF y `|`, como en las plantillas del PHP. `smtp_parameters` no se guarda con la solicitud.
- **Número** (supuesto B2): sin `number`, lo asigna el consecutivo de la resolución (salta números que ya usen otros documentos con el mismo prefijo). Con `number`, debe estar en el rango y libre; si ya lo usa un documento emitido con el mismo cuerpo (mismo SHA-256 de la solicitud guardada), se responde ese documento (`document.replayed: true`); si no, 409 `document.number_in_use`. Un número o una fecha fuera de la resolución responde 422 con los textos del PHP (`number tiene que estar entre … - …`, `date debe ser una fecha anterior o igual a …`) sin mover el consecutivo; un rango agotado, 422 en `number`.
- **Idempotency-Key** (opcional, hasta 100 caracteres): la misma llave con el mismo cuerpo responde el documento ya emitido; con otro cuerpo, 409 `idempotency.key_reused`.
- **Resolución**: la activa del tipo, en el ambiente actual y con el `prefix` del cuerpo; `resolution_number` solo elige entre varias y es obligatorio si quedan varias. Sin resolución, 422 en `type_document_id`: «La resolución no está configurada.». Una de factura necesita clave técnica y vigencia.
- **Otros 422**: validación con los mensajes de Laravel y la ruta JSON del campo (`invoice_lines.0.description`); ids de catálogo inexistentes («X es inválido.»); totales que la DIAN rechazaría (`DianTotalsValidator`); `certificate.missing`, `certificate.expired` («El certificado digital ya se encuentra vencido...», también el día en que vence) y `software.missing`. Empresa inactiva: 403 `company.inactive`.
- **Respuesta 200**: `success: true` (solo la factura, como el PHP), `message` («{tipo} #{prefijo}{número} generada con éxito»), `cufe` (factura) o `cude` (notas), `QRStr` (URL del QR del ambiente más el código), `certificate_days_left`, `resolution_days_left` (solo la factura), `ResponseDian: null`, `invoicexml` (XML firmado en base64), `unsignedinvoicexml` (XML sin firmar en base64; nulo cuando se repite un documento ya emitido), `zipinvoicexml: null` y `document` (`id`, `document_type_id`, `type_environment_id`, `prefix`, `number`, `full_number`, `status`, `uuid`, `uuid_algorithm`, `replayed`). No trae `urlinvoicexml`, `urlinvoicepdf`, `send_email_*`, `reqfe`, `rptafe` ni `attacheddocument`, que dependen del envío, el PDF y el correo.
- **Se guarda**: el documento con sus líneas, impuestos (las retenciones incluidas), descuentos, cargos, pago, referencia y copia del cliente; el XML firmado (`document_files` tipo 1) y la solicitud (tipo 4). Una nota cuyo `billing_reference.uuid` es el CUFE de un documento de la empresa queda enlazada en `reference_document_id` (supuesto B8).

## A.6 RIPS (servicio separado, Enterprise)

POST /PaquetesFevRips/CargarFevRips, CargarCapitaInicial, CargarCapitaPeriodo, CargarCapitaFinal. Cuerpo: `rips{numDocumentoIdObligado, numFactura, tipoNota, numNota, usuarios[{consecutivo, tipoDocumentoIdentificacion, numDocumentoIdentificacion, fechaNacimiento, codSexo, tipoUsuario, codPaisResidencia, codPaisOrigen, codMunicipioResidencia, codZonaTerritorialResidencia, incapacidad, servicios{consultas[{consecutivo, codPrestador, … codConsulta, codServicio, vrServicio, valorPagoModerador, conceptoRecaudo, finalidadTecnologiaSalud, grupoServicios, modalidadGrupoServicioTecSal, causaMotivoAtencion, tipoDiagnosticoPrincipal, codDiagnosticoPrincipal, codDiagnosticoRelacionado1..3, numAutorizacion}], …}}]}` + `xmlFevFile:s` (XML de la factura). Fuera del alcance de la fase Community.

## A.7 Catálogos observados en los ejemplos

Solo valores usados en la colección (los catálogos completos salen de las tablas DIAN y se siembran con seeders versionados).

| Campo | Valores vistos |
| --- | --- |
| type\_document\_id | 1, 3, 4, 5, 9, 10, 11, 13, 15, 16, 19, 24, 25, 26 |
| payment\_form\_id | 1 contado, 2 crédito |
| payment\_method\_id | 1, 2, 10, 20, 30, 42 |
| tax\_id | 1, 2, 4, 5, 6, 10, 15, 19, 21, 22 |
| unit\_measure\_id | 70, 94, 852, 1076 |
| type\_item\_identification\_id | 4, 6 |
| type\_document\_identification\_id | 3, 6, 8 |
| type\_organization\_id / type\_regime\_id | 1, 2 |
| type\_liability\_id | 7, 14, 117 |
| discrepancyresponsecode | 1, 2, 3 |
| discount\_id | 1, 10, 11, 12 |
| type\_operation\_id | 5, 8, 10, 24, 28 |
| reference\_price\_id | 1, 3 |
| type\_note | 1, 2 |
| event\_id | 1 |

## A.8 Observaciones para el diseño .NET

- Los importes viajan como **string**; el DTO de entrada los recibe como `string` y el dominio los convierte a `decimal` con redondeo DIAN explícito (2 decimales en importes y precios, 6 en cantidades y 3 en retenciones, `MidpointRounding.AwayFromZero`, como el PHP).
- Hay typos del origen que deben conservarse en v1 (`integral_salarary`, `seze`, `discrepancyresponsecode`); se mapean con `[JsonPropertyName]` y se documentan en OpenAPI.
- `type_liability_id` y `type_liability_ids[]` conviven: aceptar ambos y normalizar a lista.
- Validación con FluentValidation por tipo de documento, con mensajes compatibles con el campo `message` legacy.
- Los totales del cliente no se recalculan: se validan con las reglas y tolerancias de la DIAN y, si pasan, van tal cual al XML, como en el PHP. Lo que la DIAN rechazaría se rechaza antes con 422.
- `send_whatsapp` (factura) se acepta por compatibilidad con el contrato y se ignora hasta que WhatsApp llegue con Enterprise (fase 8).

# Anexo B: Reglas de negocio del manual

| Tema | Regla a respetar |
| --- | --- |
| Puesta en producción | Asistente por pasos: empresa, certificado, resolución, set de pruebas (testSetId) y paso a producción por producto (factura, nómina, etc.). Cada producto tiene su propio ambiente (1 producción, 2 habilitación). |
| Estados del documento | Incluye «Sin confirmar»: la DIAN respondió «En proceso» (código 98) o no hubo respuesta; se reconsulta por trackId durante una ventana de unos 10 minutos antes de marcarlo fallido (ImpiFact amplía la ventana: cada minuto durante \~10 minutos y luego cada 15 minutos hasta 24 h; ver «Arquitectura detallada» sección 7). Regla 90 (documento duplicado) se trata como reconciliación, no como error. |
| Responsabilidades fiscales | `type_liability_ids` múltiples; el `TaxLevelCode` del XML se deriva de ellas según la tabla de responsabilidades del manual. |
| Resoluciones | Prefijo, rango, clave técnica y vigencia; validar que el consecutivo esté dentro del rango y alertar al acercarse al límite. |
| Plantillas PDF | Selección por empresa y tipo de documento (`PUT /templates`); regeneración bajo demanda. |
| Correo y almacenamiento | SMTP por empresa y modos de almacenamiento (local / nube) configurables por empresa; contraseñas SMTP cifradas, nunca en texto plano. |
| Usuarios y roles | Roles de panel con permisos por módulo; token de administración y de configuración inicial con flujos separados. |
| Operación | Monitoreo, respaldos, notificaciones (campana), exportación CSV en el panel; asistente de IA y extractor de compras (Enterprise). |
| Base de datos | El ApiDian actual ya soporta PostgreSQL. ImpiFact no migra datos (ADR-07), así que este dato solo sirve como referencia de tipos y collation. |

Fuentes: documenter.getpostman.com/view/50442293/2sBYHUSfRJ; manual.facturalatam.com (instalación, guías, responsabilidades tributarias, interfaz, actualización).
