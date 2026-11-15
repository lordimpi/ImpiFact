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
