# Inventario de reglas del PHP (primera entrega)

Qué hace realmente ApiDian Community en la factura nacional, las notas crédito y débito, la configuración de la empresa y las consultas. Es la base para que ImpiFact alcance la paridad (mismo XML y mismo CUFE) sin heredar los errores del PHP.

| Dato | Valor |
| --- | --- |
| Código analizado | `apidian/`, commit `da4ed76` (14-ago-2026), repositorio Community |
| Alcance | Primera entrega: factura nacional, notas crédito y débito, configuración, autenticación y consultas. Las variantes de la fase 7 (exportación, contingencia, AIU, mandato, transporte, documento soporte, lotes, RADIAN) quedan para otra pasada |
| Método | Lectura estática del código, 14-nov-2026. No se ejecutó el PHP |
| Librería de firma | `ubl21dian/torresoftware`, commit `516e28f`, licencia LGPL-3.0. Se descargó el 14-nov y hay una copia fija fuera del repo, en `IMPI_FACT/reference/`. Las fórmulas de la sección 2.1 están confirmadas en su código |

**Cómo leer las etiquetas.** Cada regla termina con una de estas marcas:

- **Conservar**: comportamiento que ImpiFact debe replicar para la paridad o el contrato.
- **Mejora**: ImpiFact lo hace distinto a propósito, y está documentado en la arquitectura o el modelo de datos.
- **Bug del PHP**: error que no se replica. Si afecta al contrato, el contract test lo deja registrado.

Las rutas de archivo son relativas a `apidian/` y van con su número de línea. Abreviaturas: **IC** `app/Http/Controllers/Api/InvoiceController.php`, **IR** `app/Http/Requests/Api/InvoiceRequest.php`, **CN** `CreditNoteController.php`, **DN** `DebitNoteController.php`, **CR** `CreditNoteRequest.php`, **DR** `DebitNoteRequest.php`, **CC** `ConfigurationController.php`, **DT** `app/Traits/DocumentTrait.php`, **TPL** `resources/templates/xml/`.

## 1. Lo más importante para la paridad

1. **El PHP no calcula nada.** Copia al XML los totales, impuestos y líneas que manda el cliente, sin recalcular ni cuadrar (plantillas en TPL). Si el cliente envía totales incoherentes, el PHP firma un XML incoherente y la DIAN lo rechaza. Ver la decisión abierta de la sección 8.
2. **Redondeo de PHP:** `number_format(x, 2, '.', '')` en importes y precios, 6 decimales en cantidades y **3 en retenciones** (`TPL/_with_holding_tax_totals.blade.php:11-40`). `number_format` redondea la mitad alejándose de cero, así que en .NET corresponde `MidpointRounding.AwayFromZero`. **Conservar.** Comprobado con el golden `invoice/rounding-raw-decimals` (15-nov): `2.185` → `2.19`, `6.965` → `6.97` y `0.34825` → `0.35`. El caso `6.965` vale 6.96499… en `double`, así que ImpiFact debe leer los importes como `decimal` desde el texto. Los porcentajes de retención salen con 3 decimales (`15.000`). El PHP formatea cada total por separado: en ese caso, 4133.69 + 784.43 = 4918.12, pero el total con impuestos y el total a pagar salen 4918.11. Aun así, la DIAN la acepta: la regla FAU06 del Anexo Técnico v1.9 (Resolución 000165 de 2023, sección 8) compara el valor bruto más los impuestos con el total con impuestos usando `round()` de XPath, es decir, a pesos enteros, y 4918.12 y 4918.11 dan 4918. Por eso ImpiFact no la rechaza (decisión 18 del PRD; ver la sección 8).
3. **El CUFE y el CUDE los calcula `torresoftware`**, no el código del repo:
   - factura: `SignInvoice->ConsultarCUFE()` con la clave técnica de la resolución (IC:456-459, 735);
   - notas: `SignCreditNote`/`SignDebitNote->ConsultarCUDE()` con el **PIN del software**, no la clave técnica (CN:310-311, 340).
4. **`success: true` no significa aceptado.** La aceptación real es `SendBillSyncResult->IsValid == 'true'` y `state_document_id = 1`. La respuesta de éxito sale igual cuando la DIAN rechaza (IC:692-710, CN:443-446). **Conservar** en las rutas legadas para el contract test.
5. **El número del documento lo manda el cliente.** No hay autoincremento: se valida solo contra el rango y las fechas de la resolución (IR:138-142). Se aceptan huecos y repetidos. **Mejora** en ImpiFact: consecutivo atómico («Modelo de datos detallado», sección 5).
6. **Las notas no verifican la factura original.** Copian `number`, `uuid` e `issue_date` del request sin buscarla en la base (CN:282-289). **Mejora** en ImpiFact: debe existir, ser de la empresa y estar aceptada («Arquitectura detallada», sección 7).
7. **Hay dos contadores distintos.** El número fiscal (`prefix + number`) y el contador de la tabla `sends` por empresa, año y tipo, que solo sirve para los nombres de archivo DIAN (DT:1121-1146). **Conservar** el formato del nombre; ver la sección 3.6.
8. **Secretos en texto plano y token predecible.** El `api_token` es `sha256(NIT)` (CC:217, 249). La contraseña del certificado, el PIN y la contraseña SMTP no se cifran. **Mejora** (PRD sección 10).

## 2. Superficie de `torresoftware` que hay que portar

Namespace real: `ubl21dian\...` (no `Stenfrank\UBL21dian`).

| Clase | Uso en el repo | Miembros que se llaman |
| --- | --- | --- |
| `XAdES\SignInvoice` | Firma de factura y cálculo del CUFE | constructor `(certPath, certPassword)`; `softwareID`, `pin`, `technicalKey`, `GuardarEn`; `sign(DOMDocument)` devuelve `Sign` con `->xml`; `ConsultarCUFE()`, `ConsultarQRStr()` |
| `XAdES\SignCreditNote`, `XAdES\SignDebitNote` | Firma de notas y cálculo del CUDE | igual que `SignInvoice`, sin `technicalKey`; `ConsultarCUDE()` |
| `XAdES\SignAttachedDocument` | Firma del AttachedDocument | `GuardarEn`; `sign(DOMDocument)->xml` |
| `Templates\SOAP\SendBillSync` | Envío en `store` (habilitación y producción) | `To`, `fileName`, `contentFile`; `signToSend(reqPath)->getResponseToObject(rptaPath)` |
| `Templates\SOAP\SendTestSetAsync` | Envío del set de pruebas | lo mismo más `testSetId` |
| `Templates\SOAP\GetStatus`, `GetStatusZip`, `GetStatusEvents`, `GetXmlByDocumentKey`, `GetNumberingRange` | Consultas | `trackId`, `Nit`, `IDSoftware`, `To` |
| `Templates\SOAP\SendBillAsync` | Se importa pero no se usa | — |

La respuesta se lee como objeto: `Envelope->Body->SendBillSyncResponse->SendBillSyncResult` con `IsValid`, `XmlFileName`, `XmlDocumentKey`, `XmlBase64Bytes` (ApplicationResponse), `Code` y `Message`. Si existe `->html`, la DIAN está caída.

La plantilla deja vacíos `cbc:UUID`, el marcador `-----CUFECUDE-----` del QR y `SoftwareSecurityCode` (`TPL/01.blade.php:29`, `TPL/_ubl_extensions.blade.php:23,28`), y la librería los rellena al firmar (confirmado).

### 2.1 Fórmulas confirmadas en el código de la librería

Leídas de `src/XAdES/SignInvoice.php` (commit `516e28f`, líneas 575-706); `SignCreditNote` y `SignDebitNote` las heredan. Todas son SHA-384 en hexadecimal sobre la concatenación, sin separadores, de los **textos tal como quedaron en el XML** (ya formateados por la plantilla):

| Código | Concatenación |
| --- | --- |
| CUFE (factura) | `ID` + `IssueDate` + `IssueTime` + `LineExtensionAmount` + `01` + IVA + `04` + INC + `03` + ICA + `PayableAmount` + NIT emisor + NIT adquiriente + **clave técnica** + `ProfileExecutionID` |
| CUDE (notas) | lo mismo, con el **PIN del software** en lugar de la clave técnica |
| CUDS (documento soporte, fase 7) | sin INC ni ICA: … `01` + IVA + `PayableAmount` + NITs + PIN + `ProfileExecutionID` |
| `SoftwareSecurityCode` | `softwareID` + PIN + `cbc:ID` |

Detalles que cambian el resultado:

- `IssueTime` entra con su sufijo `-05:00`.
- Cada impuesto es el `TaxAmount` del `TaxTotal` cuyo esquema es 01 (IVA), 04 (INC) o 03 (ICA); si no existe, se usa el texto `0.00`. Las retenciones no entran.
- Los totales salen de `LegalMonetaryTotal`, o de `RequestedMonetaryTotal` en la nota débito.
- `ProfileExecutionID` es el código del ambiente: 1 producción, 2 pruebas.
- Los NIT son los `PartyTaxScheme/CompanyID` del emisor y del adquiriente.
- El CUFE toma el **primer elemento llamado `ID` del documento**, y el CUDE toma el `cbc:ID` de la raíz. Deberían coincidir; los golden lo confirman.
- El parche `urn_on.php` (regla ZE02) solo cambia el namespace `sts` a `urn:dian:gov:co:facturaelectronica:Structures-2-1`; no toca las fórmulas. ImpiFact usa el namespace con `urn:`.

## 3. Factura nacional (`POST /api/ubl2.1/invoice`)

### 3.1 Validación de entrada

1. `type_document_id` obligatorio, `in:1`, con resolución configurada. Error: «La resolución no está configurada.» (IR:113-118; `app/Rules/ResolutionSetting.php`). **Conservar el mensaje.**
2. Elección de la resolución: si hay una sola del tipo, esa; si hay varias, filtra por `resolution_number` (columna `resolution`) y luego por `prefix` (IR:28-37). No filtra por ambiente. **Mejora:** ImpiFact elige por tipo, ambiente y prefijo.
3. `number` entero dentro de `from..to` de la resolución; `date` en formato `Y-m-d` dentro de `date_from..date_to`; `time` en `H:i:s` (IR:138-142). **Conservar.**
4. `prefix` nunca resulta obligatorio porque la regla consulta la columna inexistente `resolution_number` (IR:130-135). **Bug del PHP.**
5. Cliente: `identification_number` alfanumérico de 1 a 15 caracteres; `dv` obligatorio si el tipo de identificación es 6 (NIT), validado con módulo 11 y pesos 3, 7, 13 … 71 (DT:1413-1439). Error: «Error, el digito de verificacion no es valido para este NIT.» **Conservar.**
6. Totales obligatorios: `line_extension_amount`, `tax_exclusive_amount`, `tax_inclusive_amount`, `payable_amount`; `pre_paid_amount` si viene `prepaid_payment` (IR:337-344).
7. Líneas obligatorias: `unit_measure_id`, `invoiced_quantity`, `line_extension_amount`, `free_of_charge_indicator`, `description`, `code`, `type_item_identification_id`, `price_amount`, `base_quantity`; `reference_price_id` si la línea es gratuita (IR:347-374).
8. Impuestos: `percent` obligatorio salvo `tax_id` 2 y 10; con `tax_id` 10 son obligatorios `unit_measure_id`, `per_unit_amount` y `base_unit_measure`; con `tax_id` 15, `tax_name` (IR:294-302, 359-367). Los campos de `tax_totals` son `required_with:allowance_charges`, lo cual no tiene sentido (IR:295). **Bug del PHP.**
9. Forma de pago: con `payment_form_id = 2` (crédito) son obligatorios `payment_due_date` (≥ `date`) y `duration_measure`. Por defecto: forma 1 y medio 10 (IR:281-282; DT:57-60).
10. `email_pos_customer` solo con el cliente 222222222222 (consumidor final) (IC:310-314).
11. Errores de validación con `Accept: application/json`: el `Handler` los convierte en HTTP 500 `{success:false, message:"Error interno del servidor", error}` y se pierde el detalle por campo (`app/Exceptions/Handler.php:51-108`). **Bug del PHP**; ImpiFact responde 422 con `ProblemDetails` y conserva `success` y `message`.

### 3.2 Cálculos y XML

1. `TaxTotal` se agrupa por `tax.id`; `TaxAmount` es la suma de los subtotales y `RoundingAmount` vale siempre `0.00` (`TPL/_tax_totals.blade.php:1-15`). **Conservar.**
2. `BaseUnitMeasure` y `PerUnitAmount` solo para impuestos de valor fijo (`tax_id` 2, 10, 19, 20, 21, 23) (`app/TaxTotal.php:174-177`).
3. `WithholdingTaxTotal` se agrupa por `tax.code` del 01 al 09.
4. `MultiplierFactorNumeric` de cargos y descuentos: el valor enviado o `amount / base_amount * 100`; con `base_amount = 0` el PHP falla con una división por cero (`app/AllowanceCharge.php:243-254`). **Bug del PHP.**
5. `AllowanceTotalAmount`, `ChargeTotalAmount` y `PrepaidAmount` solo se emiten si son distintos de cero (`TPL/_legal_monetary_total.blade.php:17-37`). **Conservar.**
6. Moneda: `PaymentExchangeRate` solo si vienen `idcurrency`, `calculationrate` y `calculationratedate`. Las líneas, impuestos y totales llevan el `currencyID` de la empresa, pero las retenciones y los anticipos usan `idcurrency`. **Bug del PHP** a revisar con la DIAN antes de decidir.
7. `IssueTime` siempre lleva `-05:00` fijo (`TPL/01.blade.php:30-31`). **Conservar.**
8. Valores fijos que se conservan: `AuthorizationProviderID` 800197268; `PaymentID` con índice desde 0; sin `Contact` para el consumidor final; `CorporateRegistrationScheme/ID` igual al prefijo, solo en el emisor.

### 3.3 CUFE

1. La librería lo calcula con `softwareID = software.identifier`, `pin = software.pin` y `technicalKey = resolution.technical_key` (IC:456-459).
2. Fórmula confirmada en la librería (sección 2.1). Coincide con «Arquitectura detallada», sección 6.2.
3. En la base queda el CUFE que devuelve la DIAN si el documento fue válido; si no, el local. La respuesta JSON siempre devuelve el local (IC:532-534, 735-739).

### 3.4 Consecutivo y duplicados

1. `cbc:ID = prefix + number` (`app/Resolution.php:80-83`). `InvoiceControl` solo si la resolución tiene número.
2. Duplicados: solo se rechazan si `VALIDATE_BEFORE_SENDING = true` (por defecto `false`) y ya existe un documento en estado 1. Error: «Este documento ya fue enviado anteriormente, se registra en la base de datos.» (IC:359-369). La tabla `documents` no tiene índice único. **Mejora:** índice único `ux_documents_number` e idempotencia.
3. `current_number` devuelve `max(number) + 1` de los documentos en estado 1 (o de todos con `ignore_state_document_id`), o `resolution.from` si no hay ninguno. No valida el límite `to` (IC:1106-1144). Error con la clave mal escrita `Excepction`. **Conservar** el contrato; **Mejora:** avisar si se pasa del rango.

### 3.5 Ambiente y set de pruebas

1. `store` usa siempre `SendBillSync`, en habilitación y en producción; el ambiente solo cambia `ProfileExecutionID`, `UUID@schemeID`, la URL del QR y `software.url` (IC:475; DT:239-246).
2. `invoice/{testSetId}` usa `SendTestSetAsync`, se salta las validaciones de plan y de empresa activa, no genera AttachedDocument ni correo, no lee el `ZipKey` y deja el documento en estado 0 (IC:802-829, 1024-1031). **Conservar** el contrato; **Mejora:** ImpiFact guarda el `ZipKey` y consulta el estado.
3. Antes de enviar hace un GET a `software.url` (10 s, sin verificar TLS). Si no responde 200: «El servicio de la DIAN no está disponible en este momento. Por favor, inténtelo más tarde.» (DT:1668-1699).

### 3.6 Firma, ZIP y envío

1. Secuencia: renderiza la plantilla Blade `xml.01` en un `DOMDocument` (`formatOutput = true`, `preserveWhiteSpace = false`), firma, guarda y comprime en ZIP en base64 (IC:447-481; DT:287-290, 924-989). El formato del XML (sangrías y espacios) afecta la firma, no el CUFE.
2. Nombre del XML dentro del ZIP: `fv` + NIT a 10 dígitos + `000` + año a 2 dígitos + contador de `sends` a 8 dígitos + `.xml`. El ZIP lleva `z…` con un sufijo `-{uniqid}` que no sigue la norma (DT:933, 1139). **Conservar** el nombre del XML; **Bug del PHP** el sufijo del ZIP.
3. El `fileName` del SOAP es `{prefix}{number}.xml`, no el nombre DIAN (IC:477).
4. Cada envío consume dos valores del contador `sends` (XML y ZIP), aunque la DIAN rechace.
5. No hay reintentos SOAP, ni manejo de la regla 90, ni de «En proceso» en la factura; la regla 90 solo aparece en eventos (`SendEventController.php:862`). **Mejora** en ImpiFact (Worker).
6. AttachedDocument (plantilla `89`): solo si `IsValid`. Lleva el XML firmado y el ApplicationResponse en `CDATA`, y se firma con `SignAttachedDocument`. Su nombre es `XmlFileName` cambiando `fv`/`nc`/`nd` por `ad`. `IssueDate` toma la fecha actual (IC:617-650).

### 3.7 Persistencia

1. El `Document` se guarda **antes** del envío en estado 0, con `request_api` (el JSON completo, incluidas contraseñas SMTP), totales copiados del request, `ambient_id` y `version_ubl_id = 2` (IC:276-294, 492-512).
2. Si la DIAN valida: estado 1 y el CUFE de la DIAN. Si rechaza: queda en 0, sin otro estado (IC:621-623).
3. Clientes: `updateOrCreate` por NIT y empresa, con una contraseña aleatoria en bcrypt (DT:159-221).
4. `GET invoice/state_document/{type}/{number}` pasa a 1 cualquier documento en estado 0 sin consultar a la DIAN (IC:1146-1161). **Bug del PHP**; no se replica.

### 3.8 Respuesta JSON de `store`

Campos en este orden (IC:763-783): `success`, `message`, `send_email_success`, `send_email_date_time`, `urlinvoicexml`, `urlinvoicepdf`, `urlinvoiceattached`, `cufe`, `QRStr`, `certificate_days_left`, `resolution_days_left`, `ResponseDian`, `whatsapp_response`, y en base64 `invoicexml`, `zipinvoicexml`, `unsignedinvoicexml`, `reqfe`, `rptafe`, `attacheddocument`.

- El `message` de éxito dice «AttachedDocument #… generada con éxito» porque la variable del tipo se reasigna al 7 (IC:516, 709). **Conservar** para el contrato.
- `send_email_success` y `send_email_date_time` usan `?? ` sobre un booleano y no reflejan si el correo salió (IC:766-767). **Bug del PHP.**
- Si la DIAN responde HTML, `responseStore` recibe 12 argumentos de 13 y falla con `ArgumentCountError` (IC:525, 612). **Bug del PHP.**
- `set de pruebas`: sin `success`, con `urlinvoiceattached = "Attachment-…"` (IC:1087-1102).
- **ImpiFact:** `urlinvoicexml` y `urlinvoicepdf` con el nombre del PHP (`FES-`, `NCS-` o `NDS-` más el número completo) y `urlinvoiceattached` siempre como `Attachment-{número}.xml`, porque la respuesta es inmediata (decisión C5 de la fase 4) y el nombre DIAN del AttachedDocument se decide al enviarlo; la ruta `download` entiende los tres. Sin `send_email_success`, `send_email_date_time`, `reqfe`, `rptafe` ni `attacheddocument` (el envío, el correo y el AttachedDocument ocurren después, en el Worker) ni `whatsapp_response` (Enterprise).

### 3.9 Efectos secundarios

1. PDF con mPDF, generado antes de la respuesta de la DIAN; el QR es la URL del catálogo (`catalogo-vpfe[-hab].dian.gov.co/document/searchqr?documentkey=…`) más el CUFE (DT:372-443).
2. Correo solo si `sendmail` y el documento quedó en estado 1; no se envía al consumidor final salvo con `email_pos_customer`; copias a `sendmailtome` y `email_cc_list`. Los errores solo quedan en el log (IC:668-690).
3. Cuota del plan solo en `store`, contando documentos en estado 1. El `switch` no tiene `break` y la cuota no se aplica cuando existe `absolut_start_plan_date` (IC:225-270; DT:1543-1561). **Bug del PHP.** ImpiFact (fase 4): mismas reglas y mensajes (plan inactivo; mensual al mes y anual al año; usados contra la cantidad del plan por familia en `plan_quotas`) también en las notas, contando en `plan_usage` al aceptar; sin plan contratado no hay límites; el set de pruebas no revisa ni cuenta; no existe el «plan mixto» (`absolut_plan_documents`).
4. `ActualizarTablas` modifica catálogos en cada petición: borra responsabilidades y operaciones y renombra impuestos (DT:1290-1411). **Bug del PHP**; ImpiFact usa catálogos sembrados y versionados.
5. `preeliminar-view` genera solo el PDF, con el CUFE vacío, y lo escribe en la misma ruta que la factura real, así que puede sobrescribirla (IC:41-188). **Bug del PHP.**

## 4. Notas crédito y débito

Los dos controladores son copias casi idénticas. Se listan solo las diferencias con la factura.

### 4.1 Validación

1. Tipos: nota crédito `in:4,26`, nota débito `in:5,25` (CR:101-106, DR:95-99).
2. `discrepancyresponsecode`: crédito de 1 a 6, débito de 1 a 4 (CR:134, DR:122). El catálogo de crédito solo tiene del 1 al 5, así que el 6 pasa la validación y revienta al armar el XML (`TPL/_discrepancy_response.blade.php:30`). **Bug del PHP.**
3. `billing_reference` opcional; si viene, `number`, `uuid` de 96 caracteres e `issue_date` en `Y-m-d` son obligatorios (CR:138-142).
4. `type_operation_id`: crédito 7, 8, 12, 30, 31, 32 (por defecto 12, código 20); débito 4, 5, 6, 30, 31, 32 (por defecto 6, código 30). `invoice_period` obligatorio para la nota «sin referencia» (crédito 8, débito 5).
5. Totales: crédito usa `legal_monetary_totals`; débito usa `requested_monetary_totals` y emite `RequestedMonetaryTotal`.
6. Solo débito: `customer.email` obligatorio salvo para el consumidor final, y `resolution_number` obligatorio si hay varias resoluciones (DR:102-107, 167).

### 4.2 Referencia a la factura y discrepancia

1. La factura original no se busca en la base: se copian `number`, `uuid` e `issue_date` a `BillingReference` (`TPL/_billing_reference.blade.php:1-14`). **Mejora** en ImpiFact (fase 4): si el CUFE es de un documento de la empresa, ese documento debe estar aceptado por la DIAN (si no, 422 `document.reference_not_accepted`); un CUFE desconocido se acepta sin enlace, como en el PHP, y el set de pruebas no exige la aceptación.
2. `schemeName` del UUID referenciado: `CUFE-SHA384` por defecto (`app/BillingReference.php:62-65`).
3. Se admiten notas sin referencia (sin `billing_reference`, el bloque se omite).
4. `DiscrepancyResponse/Description` sale del **catálogo** por código, no del campo `discrepancyresponsedescription` del request, que se ignora (`TPL/_discrepancy_response.blade.php:15-31`). **Conservar.**

### 4.3 Diferencias de XML entre crédito y débito

| Aspecto | Nota crédito | Nota débito |
| --- | --- | --- |
| Prefijos de archivo | `NC`, `NCS` | `ND`, `NDS` |
| TypeCode | `cbc:CreditNoteTypeCode` 91 | no lleva |
| Totales | `LegalMonetaryTotal` | `RequestedMonetaryTotal` |
| Orden de bloques | InvoicePeriod → DiscrepancyResponse → OrderReference → AdditionalDocumentReference → BillingReference | OrderReference → AdditionalDocumentReference → DiscrepancyResponse → BillingReference |
| Líneas | `CreditedQuantity`, con `FreeOfChargeIndicator`, `BrandName`, `ModelName` | `DebitedQuantity`, sin esos tres |
| `StandardItemIdentification@schemeName` | nombre | código |

`AdditionalDocumentReference` va antes de `BillingReference` en ambas, lo que parece contradecir el orden del XSD de UBL 2.1. **A validar** contra el XSD antes de conservarlo.

### 4.4 CUDE, consecutivo y respuesta

1. CUDE: lo calcula la librería con el PIN del software (CN:310-311). Fórmula confirmada en la sección 2.1.
2. Las notas no emiten `InvoiceControl` (`TPL/_ubl_extensions.blade.php:6-7`).
3. La respuesta de crédito y débito no lleva `success` en el éxito; usa `cude` en vez de `cufe`, y con una excepción en el envío devuelve un **texto plano**, no JSON (CN:447-448). **Bug del PHP** el texto plano.
4. Errores propios de débito: escribe el AttachedDocument como `"{filename}xml"` sin el punto (DN:507) y lee el XML firmado de tres maneras distintas. **Bugs del PHP.**
5. `testSetStore` de las notas no pasa `invoice_period` al XML (CN:779). **Bug del PHP.**

## 5. Configuración, autenticación y consultas

### 5.1 Autenticación

1. `auth:api` compara el Bearer con `users.api_token` en texto plano (`config/auth.php:44-47`).
2. El token se genera al crear la empresa como `sha256(NIT)`, y la contraseña del usuario es el NIT (CC:217, 235, 249). Cualquiera que sepa el NIT obtiene el token. **Bug del PHP (seguridad).**
3. El alta de empresa solo pide `API_KEY` si `USE_PROTECTION_API_KEY` está activo, y por defecto no lo está (`config/app.php:126-127`). **Bug del PHP (seguridad).**
4. Rutas públicas: `signdocument`, `senddocument`, `statusdocument`, `statuszip`, `listing`, `plan`, `administrator`, `information`, `total_documents`, `config/delete/{nit}/{email}`, descargas y otras (`routes/api.php:17-57, 287-438`). **Mejora:** todas exigen API key (ADR-06).
5. El middleware `ApiLog` guarda el body completo de cada petición en `logs`, incluidos certificados y contraseñas (`app/Http/Middleware/ApiLog.php:20-26`). **Bug del PHP (seguridad).**

### 5.2 Empresa

1. `POST config/{nit}/{dv}` crea o actualiza por NIT (idempotente). Obligatorios: `business_name`, `email`, tipo de identificación, organización, régimen, responsabilidad, municipio, matrícula, dirección y teléfono de 7 a 10 dígitos (`ConfigurationRequest.php:53-67`). **Conservar** el comportamiento idempotente.
2. Valores por defecto: idioma 79, país 46, moneda 35, operación 10, todos los ambientes en 2 (habilitación); `tax_id` 15 si el régimen es 2 y 1 en otro caso (CC:227-230, 280-311).
3. Al crear se inserta el cliente por defecto 2222222222 («Cliente por Defecto») (CC:416-425). **Conservar** como dato semilla por empresa.
4. El correo no puede repetirse. Error: «Error de registro, el correo electronico ya existe o es el mismo que tiene registrado actualmente.» (CC:317-323).
5. La respuesta incluye el hash de la contraseña y la contraseña SMTP (CC:429-435). **Bug del PHP (seguridad).**
6. `delete/company/{nit}/{dv}` deja que cualquier usuario autenticado borre cualquier empresa (CC:1013-1026). **Bug del PHP (seguridad).**

### 5.3 Software, certificado y resolución

1. Software: un registro por empresa; PIN numérico de 5 dígitos; las URL por defecto son de habilitación y se **resetean a habilitación** al actualizar el software aunque la empresa esté en producción (CC:456-516). **Bug del PHP.**
2. Certificado: llega en base64 con su contraseña; se lee con `openssl_pkcs12_read` y, si falla (certificados con cifrado antiguo en OpenSSL 3), se manda **el certificado y su contraseña a un servicio externo** `{URL_API_CERT_MODERNIZER}/modernizer-b64` para convertirlo (CC:606-640; `app/Services/CertificateModernizerService.php:20-32`). **Mejora:** ImpiFact convierte en local, sin enviar el certificado a terceros.
3. El certificado se guarda sin cifrar en `storage/app/certificates/{nit}{dv}.p12`, y su contraseña en texto plano (CC:658-681). Antes de cada envío se verifica el vencimiento y se devuelve `certificate_days_left` (DT:1602-1665). **Conservar** el campo de días restantes.
4. Resolución: prefijo de hasta 4 caracteres; para facturas son obligatorios número, fecha, clave técnica y vigencia. Se guarda con `updateOrCreate` por tipo, número, prefijo y ambiente (CC:757-803).
5. `config/environment`: 1 es producción y cualquier otro valor habilitación; cambia las URL del software. No exige set de pruebas aprobado (CC:902-989). **Mejora:** el asistente de paso a producción de ImpiFact lo exige.

### 5.4 Consultas

1. `status/document/{trackId}` y `status/zip/{trackId}`: consultan a la DIAN; si es válido, arman el AttachedDocument, pasan el documento a estado 1 y pueden enviar el correo (`StateController.php:37-436, 442-844`). **ImpiFact:** la API no llama a la DIAN; responde con la última respuesta que guardó el Worker (que ya arma el AttachedDocument y encola el correo al aceptar). `reqzip` siempre es nulo porque no se guardan los sobres enviados; el segmento `GuardarEn` (carpeta del servidor) se ignora. Como el PHP, la respuesta lleva `certificate_days_left` y `attacheddocument` (el AttachedDocument en base64, nulo mientras no exista).
2. `information/{nit}/{desde}/{hasta}` y `total_documents`: públicas; `information` devuelve los documentos en estado 1 de los tipos 1, 4, 5 y 15 (la variante paginada, de 100 en 100); `total_documents` solo cuenta, y cuenta 8 tipos: 1, 4, 5, 11, 13, 15, 26 y 25 (`ResumeController.php:24-232`).
3. `next-consecutive` y `current_number`: misma regla que la sección 3.4.
4. `xml/document/{trackId}`: el PHP pide el XML a la DIAN con GetXmlByDocumentKey (`XmlDocumentController.php`). **ImpiFact:** devuelve el XML firmado guardado con la misma forma (`Code` 100, `XmlBytesBase64`), solo de documentos aceptados.
5. `download/{nit}/{archivo}/{type_response?}`: pública en el PHP, con `BASE64`, `INLINE` o descarga; `Attachment-` y `ZipAttachm-` se traducen al nombre del AttachedDocument (`DownloadController.php:154`, `Utils.php:12`). **ImpiFact:** exige `documents:read` y el NIT de la credencial; un archivo que no existe responde 404 con el mismo texto («No se encontro el archivo: …»).
6. `regeneratepdf`: el PHP consulta GetStatus a la DIAN para validar el CUFE y regenera el PDF en la solicitud, con el cuerpo de una factura en la variante sin ruta (`RegeneratePDFController.php`). **ImpiFact:** no consulta a la DIAN (el estado guardado ya dice si fue aceptado), encola `RegeneratePdf` con los datos guardados del documento y del cuerpo solo lee `cufe`, `prefix` y `number`; responde sin `filebase64` (el PDF se descarga después).
7. `send-email`: el PHP envía en la solicitud a `alternate_email` o al cliente, con `email_cc_list` (`SendEmailController.php:34-125`). **ImpiFact:** encola `SendEmail` con los destinatarios de la solicitud original; `alternate_email`, `email_cc_list`, `only_send_to_cc_list` y `base64graphicrepresentation` se aceptan y se ignoran, y una solicitud emitida sin `sendmail` responde 422.
8. `numbering-range`: GetNumberingRange con el NIT de la empresa como `accountCode` y `accountCodeT` y el `IDSoftware` del cuerpo (`NumberingRangeController.php`). **ImpiFact:** igual, más los rangos leídos del sobre (`numbering_ranges`).
9. `customer/{document}/{nit}`: GetAcquirer firmado con el certificado de la empresa (`CustomerController::getAcquirer`, `GetAdquirerRequest.php`); manda en `identificationType` el id del catálogo tal cual llega; un SOAP Fault responde 500 con `Dian Error: …` y el `StatusCode` 404 de la DIAN, `success: false`. El listado de clientes del PHP pagina de 20 en 20 con `search` en número o nombre (`LIKE`), y el alta valida `identification_number` único **global** (no por empresa) y guarda una contraseña. **ImpiFact:** `GET /api/v1/customers/lookup` manda el **código** DIAN del tipo (31 para NIT; por confirmar en habilitación), un SOAP Fault responde 422 y la falta de respuesta 503; el 404 de la DIAN es `found: false` con 200. Los clientes son únicos por empresa, tipo y número, sin contraseña, y se desactivan sin borrarse.

## 6. Respuestas a los [verificar] del modelo de datos

| Pregunta | Respuesta | Evidencia |
| --- | --- | --- |
| ¿Dónde guarda el PHP la razón social? | En `users.name` (campo `business_name` del request). `companies` no tiene columna de nombre | CC:233, 371; `ResumeController.php:111` |
| ¿Qué valores tiene `state_document_id`? | No hay catálogo. **0** = guardado o enviado sin validar (incluye los rechazados); **1** = validado por la DIAN o documento semilla; **2** = contingencia tipo 4 pendiente | IC:279, 533; `InvoiceContingencyController.php:909` |
| ¿Se valida el `number` del cliente contra el consecutivo? | No. Solo contra el rango y las fechas de la resolución. Acepta huecos y repetidos | IR:138-142; IC:359-369 |
| ¿Cómo se guardan los secretos? | Certificado sin cifrar en disco; contraseña del certificado, PIN y contraseña SMTP en texto plano; `api_token = sha256(NIT)`; contraseña del usuario en bcrypt | CC:235, 240, 249, 467, 661, 679 |

Mapeo propuesto de `state_document_id` a `DocumentStatus` para las respuestas legadas: `Accepted` y `AcceptedWithWarnings` → 1; `ContingencyPending` → 2; todos los demás → 0. Se confirma con el contract test.

## 7. Bugs y riesgos de seguridad que no se replican

| # | Problema en el PHP | Gravedad | Qué hace ImpiFact |
| --- | --- | --- | --- |
| 1 | `api_token = sha256(NIT)` y contraseña = NIT | Crítica | API keys aleatorias con hash (ADR-12) |
| 2 | Alta y baja de empresas sin credenciales por defecto | Crítica | Todo exige autenticación; la baja exige rol de plataforma |
| 3 | `GuardarEn` del request se usa como ruta de disco sin sanear (lectura y escritura arbitraria) | Crítica | Rutas generadas por el servidor en `IBlobStorage` |
| 4 | El certificado y su contraseña se envían a un servicio externo para convertirlos | Alta | Conversión local |
| 5 | Secretos en texto plano y expuestos en respuestas (`mail_password`, contraseña del certificado) | Alta | Cifrado con Data Protection; nunca se devuelven |
| 6 | `ApiLog` guarda cuerpos completos con certificados y contraseñas | Alta | Logs sin secretos; `audit_logs` sin cuerpos |
| 7 | `state_document` pasa documentos a aceptado sin consultar a la DIAN | Alta | Solo la respuesta de la DIAN cambia el estado |
| 8 | `ActualizarTablas` modifica catálogos en cada petición | Media | Catálogos sembrados y versionados |
| 9 | Errores de validación como HTTP 500 sin detalle | Media | 422 con `ProblemDetails`, conservando `success` y `message` |
| 10 | La vista previa sobrescribe el PDF de la factura real | Media | La vista previa no persiste nada |
| 11 | Resolución elegida sin filtrar por ambiente | Media | Se filtra por tipo, ambiente y prefijo |
| 12 | Cuota del plan que no se aplica (`switch` sin `break`) | Media | Cuotas por tipo de documento (`plan_quotas`) |

## 8. Implicaciones para ImpiFact

**Aplicado a los documentos (14-nov):**

- «Modelo de datos detallado»: resueltos los tres [verificar] de la sección 6 y el mapeo de `state_document_id`.
- «Arquitectura detallada», sección 6.5: el redondeo usa 2 decimales en importes y precios, 6 en cantidades y 3 en retenciones.
- PRD: actualizados el riesgo de certificados antiguos y el estado del inventario (revisión crítica #3).

**Decisión (14-nov): totales del cliente.** El PHP firma los totales que manda el cliente aunque no cuadren. ImpiFact los valida antes de tomar el consecutivo, con las mismas reglas y tolerancias que aplica la DIAN, y rechaza con 422 solo lo que la DIAN rechazaría. Si están dentro de la tolerancia, el XML lleva los valores del cliente, igual que el PHP, así la paridad se mantiene. Rechazar con coincidencia exacta se descartó porque podría bloquear facturas que la DIAN acepta (los ERP redondean distinto). Las reglas salen del Anexo Técnico v1.9 (Resolución 000165 de 2023, sección 8) y las aplica `DianTotalsValidator`:

- Se exigen las reglas de rechazo sin ambigüedad, comparando a pesos enteros con `round()` de XPath (la mitad sube hacia infinito positivo) después de escribir cada importe con 2 decimales, como en el XML. Factura: FAU02 (valor bruto = suma de las líneas), FAU06 (valor bruto + impuestos de la cabecera = total con impuestos), FAU08 y FAU10 (totales de descuentos y cargos = suma de los globales), FAU12 (anticipos), FAU14 (total con impuestos − descuentos + cargos = valor a pagar; un total que falta vale 0.00 y los anticipos no entran), FAS02 y FAX02 (cada `TaxTotal` = suma de sus subtotales), FAX07 (impuesto de la línea = base × tarifa / 100, o valor por unidad × cantidad) y VLR01 (ningún importe negativo). Las notas usan las mismas con CA y DA; la nota crédito no tiene CAU12 y la débito valida `RequestedMonetaryTotal`.
- No se exigen, porque el anexo es ambiguo, y se confirman en la fase 1b contra la DIAN: FAV06 (la fórmula usa `Price/BaseQuantity`), FAS07 en la cabecera (no divide la tarifa entre 100), FAU04 (`TaxTotal[1]` frente a todos los subtotales), los márgenes de ±2.00 y de ±5 pesos en el IVA (solo aparecen como texto, sin fórmula) y `PayableRoundingAmount` (sin regla propia).

**Pendiente:**

1. Hecho el 14-nov: fórmulas del CUFE, CUDE y `SoftwareSecurityCode` confirmadas en el código de `torresoftware` (sección 2.1). El 15-nov, el CUFE del primer caso golden (`backend/tests/golden/invoice/two-lines-discounts-iva19`) se recalculó desde el XML con esta fórmula y coincide.
2. Validar contra el XSD de UBL 2.1 el orden de `AdditionalDocumentReference` y `BillingReference` en las notas.
3. Inventario de las variantes de la fase 7 (exportación, contingencia, AIU, mandato, transporte, documento soporte, lotes y RADIAN).
