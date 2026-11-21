# Inventario de reglas del PHP (primera entrega)

Qué hace realmente ApiDian Community en la factura nacional, las notas crédito y débito, la configuración de la empresa y las consultas. Es la base para que ImpiFact alcance la paridad (mismo XML y mismo CUFE) sin heredar los errores del PHP.

| Dato | Valor |
| --- | --- |
| Código analizado | `apidian/`, commit `da4ed76` (14-ago-2026), repositorio Community |
| Alcance | Primera entrega (secciones 1 a 8): factura nacional, notas crédito y débito, configuración, autenticación y consultas. Fase 7a (secciones 9 a 18, 21-nov): AIU, mandato, transporte, exportación, contingencia tipo 3 y tipo 4, documento soporte y su nota de ajuste, lotes, eventos RADIAN con los documentos recibidos, y sector salud sin RIPS |
| Método | Lectura estática del código, 14-nov-2026 (primera entrega) y 21-nov-2026 (fase 7a). No se ejecutó el PHP |
| Librería de firma | `ubl21dian/torresoftware`, commit `516e28f`, licencia LGPL-3.0. Se descargó el 14-nov y hay una copia fija fuera del repo, en `IMPI_FACT/reference/`. Las fórmulas de la sección 2.1 están confirmadas en su código |

**Cómo leer las etiquetas.** Cada regla termina con una de estas marcas:

- **Conservar**: comportamiento que ImpiFact debe replicar para la paridad o el contrato.
- **Mejora**: ImpiFact lo hace distinto a propósito, y está documentado en la arquitectura o el modelo de datos.
- **Bug del PHP**: error que no se replica. Si afecta al contrato, el contract test lo deja registrado.

Las rutas de archivo son relativas a `apidian/` y van con su número de línea. Abreviaturas: **IC** `app/Http/Controllers/Api/InvoiceController.php`, **IR** `app/Http/Requests/Api/InvoiceRequest.php`, **CN** `CreditNoteController.php`, **DN** `DebitNoteController.php`, **CR** `CreditNoteRequest.php`, **DR** `DebitNoteRequest.php`, **CC** `ConfigurationController.php`, **DT** `app/Traits/DocumentTrait.php`, **TPL** `resources/templates/xml/`.

Abreviaturas de la fase 7a (controladores en `app/Http/Controllers/Api/`): **VA** `InvoiceAIUController.php`, **VM** `InvoiceMandateController.php`, **VT** `InvoiceTransportController.php`, **VE** `InvoiceExportController.php`, **VC** `InvoiceContingencyController.php`, **DS** `SupportDocumentController.php`, **NS** `sdCreditNoteController.php`, **BC** `BatchController.php`, **SE** `SendEventController.php`, **RE** `RadianEventController.php`, **SC** `StateController.php`; **ARC** `app/Http/Controllers/AcceptRejectDocumentController.php`; **REQ** `app/Http/Requests/Api/`; **RT** `routes/api.php`; **MIG** `database/migrations/`; **CSV** `public/csv/`. **LIB** es `src/` de la copia fija de `torresoftware` (`IMPI_FACT/reference/torresoftware-516e28f/`); las clases `XAdES/urn/` que instala `urn_on.php` tienen los mismos números de línea en lo citado, y las plantillas `TPL/urn/` son idénticas a las de `TPL/` (comprobado el 21-nov).

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
| `Templates\SOAP\SendBillAsync` | Envío de lotes (`send-batch`, sección 15); en los controladores de documento se importa pero no se usa | `To`, `fileName`, `contentFile` |
| `XAdES\SignEvent`, `Templates\SOAP\SendEvent` | Firma y envío de eventos RADIAN (sección 16); la operación SOAP es `SendEventUpdateStatus` | `softwareID`, `pin`, `sign()`, `ConsultarCUDEEVENT()`; `To`, `contentFile` |

La respuesta se lee como objeto: `Envelope->Body->SendBillSyncResponse->SendBillSyncResult` con `IsValid`, `XmlFileName`, `XmlDocumentKey`, `XmlBase64Bytes` (ApplicationResponse), `Code` y `Message`. Si existe `->html`, la DIAN está caída.

La plantilla deja vacíos `cbc:UUID`, el marcador `-----CUFECUDE-----` del QR y `SoftwareSecurityCode` (`TPL/01.blade.php:29`, `TPL/_ubl_extensions.blade.php:23,28`), y la librería los rellena al firmar (confirmado).

### 2.1 Fórmulas confirmadas en el código de la librería

Leídas de `src/XAdES/SignInvoice.php` (commit `516e28f`, líneas 575-706); `SignCreditNote` y `SignDebitNote` las heredan. Todas son SHA-384 en hexadecimal sobre la concatenación, sin separadores, de los **textos tal como quedaron en el XML** (ya formateados por la plantilla):

| Código | Concatenación |
| --- | --- |
| CUFE (factura) | `ID` + `IssueDate` + `IssueTime` + `LineExtensionAmount` + `01` + IVA + `04` + INC + `03` + ICA + `PayableAmount` + NIT emisor + NIT adquiriente + **clave técnica** + `ProfileExecutionID` |
| CUDE (notas) | lo mismo, con el **PIN del software** en lugar de la clave técnica |
| CUDS (documento soporte, fase 7) | sin INC ni ICA: … `01` + IVA + `PayableAmount` + NITs + PIN + `ProfileExecutionID` |
| CUDE del evento (ApplicationResponse, fase 7a) | `ID` + `IssueDate` + `IssueTime` + NIT de `SenderParty` + NIT de `ReceiverParty` + `ResponseCode` + `DocumentReference/ID` + `DocumentReference/DocumentTypeCode` + **PIN del software**, sin `ProfileExecutionID` (LIB `XAdES/SignInvoice.php:704-714`; sección 16.5) |
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
6. Totales obligatorios: `line_extension_amount`, `tax_exclusive_amount`, `tax_inclusive_amount`, `payable_amount`; `pre_paid_amount` si viene `prepaid_payment` (IR:337-344). Los anticipos llegan como `prepaid_payment` (un objeto) y `prepaid_payments` (una lista), con `idpayment`, `paidamount`, `receiveddate`, `paiddate` e `instructionid`, todos opcionales en el PHP (IR:315-331). **ImpiFact** (fase 5): exige `idpayment`, `paidamount` y `receiveddate` (`yyyy-MM-dd`), que el anexo marca obligatorios (FBD02, FBD03 y FBD05); `paiddate` e `instructionid` siguen opcionales; un `prepaid_payment` vacío (`{}`) se ignora, como el `if($request->prepaid_payment)` del PHP.
7. Líneas obligatorias: `unit_measure_id`, `invoiced_quantity`, `line_extension_amount`, `free_of_charge_indicator`, `description`, `code`, `type_item_identification_id`, `price_amount`, `base_quantity`; `reference_price_id` si la línea es gratuita (IR:347-374).
8. Impuestos: `percent` obligatorio salvo `tax_id` 2 y 10; con `tax_id` 10 son obligatorios `unit_measure_id`, `per_unit_amount` y `base_unit_measure`; con `tax_id` 15, `tax_name` (IR:294-302, 359-367). Los campos de `tax_totals` son `required_with:allowance_charges`, lo cual no tiene sentido (IR:295). **Bug del PHP.**
9. Forma de pago: con `payment_form_id = 2` (crédito) son obligatorios `payment_due_date` (≥ `date`) y `duration_measure`. Por defecto: forma 1 y medio 10 (IR:281-282; DT:57-60). `payment_form` puede ser un objeto o una lista de formas de pago (IC:388-401), también en las notas; cada una es un `cac:PaymentMeans` con `PaymentID` igual a su índice desde 0 y `cac:PaymentTerms` (solo en la factura) usa la primera (`TPL/_payment_means.blade.php`, `TPL/_payment_terms.blade.php`). **ImpiFact** (fase 5): igual, con los errores de una lista por índice (`payment_form.1.payment_due_date`) y una fila de `document_payments` por forma.
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
9. Anticipos (`TPL/01.blade.php:65-70`, `_prepaid_payment.blade.php`, `_prepaid_payments.blade.php`): un `cac:PrepaidPayment` por anticipo, después de `cac:PaymentTerms` y antes de `cac:AllowanceCharge`, primero el de `prepaid_payment` y luego los de `prepaid_payments`, con `cbc:ID`, `cbc:PaidAmount` (2 decimales, en la moneda del documento, ver el ítem 6), `cbc:ReceivedDate`, `cbc:PaidDate` e `cbc:InstructionID` (solo si llega). `PrepaidAmount` no entra en `PayableAmount` (FAU14) ni en el CUFE (que usa `ValTot` = `PayableAmount`). Solo la factura: las plantillas de las notas (91 y 92) no los escriben. Diferencias a propósito: el PHP escribe `cbc:PaidDate` vacío cuando no llega (la DIAN lo rechaza por formato) y en la lista pone `schemeID` igual al mismo `idpayment` (un error de copia: el anexo solo usa `schemeID` en el sector salud); ImpiFact omite `cbc:PaidDate` sin valor y escribe `cbc:ID` sin atributos. Sin caso golden del PHP con anticipos: lo esperado sale de las plantillas y del anexo.

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
2. `invoice/{testSetId}` usa `SendTestSetAsync`, se salta las validaciones de plan y de empresa activa, no genera AttachedDocument ni correo, no lee el `ZipKey` y deja el documento en estado 0 (IC:802-829, 1024-1031). **Conservar** el contrato; **Mejora:** ImpiFact guarda el `ZipKey` y consulta el estado. Si GetStatusZip responde «Set de prueba con identificador … se encuentra Aceptado» (código 2, sin validar el documento: la DIAN ya aprobó el set y no valida más envíos), la respuesta es final: el documento pasa a `Rejected` con esa descripción y se dejan de hacer consultas (20-nov). «Batch en proceso de validación.» y el código 2 con otra descripción siguen en proceso.
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
3. `next-consecutive` y `current_number`: misma regla que la sección 3.4. **ImpiFact:** las dos leen el consecutivo de la resolución sin reservarlo (`NextNumberRule`); `next-consecutive` (18-nov, fase 5) exige `type_document_id` (1 a 26) y `prefix` como `NextConsecutiveRequest.php` y responde `{success, type_document_id, prefix, number}`; un tipo sin consecutivo o un prefijo sin resolución activa responde 404 «La resolución no está configurada.» (el PHP respondía 200 con `success: false` y el texto de la excepción).
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
| 13 | `POST accept-reject-document` firma y envía eventos RADIAN de cualquier empresa sin autenticación (sección 16.1) | Crítica | Los eventos exigen API key y actúan solo sobre la empresa de la credencial |
| 14 | La contingencia tipo 4 busca duplicados sin filtrar por empresa y revela el tipo y la fecha de documentos ajenos (sección 12.2) | Alta | Índice único por empresa |
| 15 | La contingencia tipo 4 inventa una respuesta «válida» de la DIAN y envía al cliente un AttachedDocument con un ApplicationResponse vacío (sección 12.2) | Alta | Ninguna respuesta de la DIAN se simula; el AttachedDocument se arma con el ApplicationResponse real |
| 16 | `send-event` escribe en disco el archivo con el nombre que manda el cliente, antes de validar al adquiriente (sección 16.2) | Alta | Nombres generados por el servidor en `IBlobStorage` |
| 17 | Consultas de eventos por UUID y recepción de documentos sin filtro por empresa (sección 16.8) | Alta | Toda consulta filtra por la empresa de la credencial |
| 18 | La exportación inventa la TRM (4.000 pesos) si no la puede leer de la web de la DIAN (sección 11) | Media | La TRM nunca se inventa |
| 19 | Documento soporte con clave técnica en la resolución: la librería calcula un CUFE en lugar del CUDS (sección 13.3) | Media | CUDS siempre para los tipos 05 y 95 |
| 20 | La nota de ajuste al documento soporte escribe el `SoftwareID` del software de factura (sección 14) | Media | Software del documento soporte en todo el documento |
| 21 | Los lotes no guardan el `ZipKey` ni actualizan los documentos, y `createPDF` recibe un argumento de menos (sección 15) | Media | El Worker guarda el `ZipKey`, consulta el estado y actualiza cada documento |

## 8. Implicaciones para ImpiFact

**Aplicado a los documentos (14-nov):**

- «Modelo de datos detallado»: resueltos los tres [verificar] de la sección 6 y el mapeo de `state_document_id`.
- «Arquitectura detallada», sección 6.5: el redondeo usa 2 decimales en importes y precios, 6 en cantidades y 3 en retenciones.
- PRD: actualizados el riesgo de certificados antiguos y el estado del inventario (revisión crítica #3).

**Decisión (14-nov): totales del cliente.** El PHP firma los totales que manda el cliente aunque no cuadren. ImpiFact los valida antes de tomar el consecutivo, con las mismas reglas y tolerancias que aplica la DIAN, y rechaza con 422 solo lo que la DIAN rechazaría. Si están dentro de la tolerancia, el XML lleva los valores del cliente, igual que el PHP, así la paridad se mantiene. Rechazar con coincidencia exacta se descartó porque podría bloquear facturas que la DIAN acepta (los ERP redondean distinto). Las reglas salen del Anexo Técnico v1.9 (Resolución 000165 de 2023, sección 8) y las aplica `DianTotalsValidator`:

- Se exigen las reglas de rechazo sin ambigüedad, comparando a pesos enteros con `round()` de XPath (la mitad sube hacia infinito positivo) después de escribir cada importe con 2 decimales, como en el XML. Factura: FAU02 (valor bruto = suma de las líneas), FAU06 (valor bruto + impuestos de la cabecera = total con impuestos), FAU08 y FAU10 (totales de descuentos y cargos = suma de los globales), FAU12 (anticipos), FAU14 (total con impuestos − descuentos + cargos = valor a pagar; un total que falta vale 0.00 y los anticipos no entran; el ejemplo «Factura con anticipo» de la colección Postman sí los resta y por eso responde 422, como lo rechazaría la DIAN), FAS02 y FAX02 (cada `TaxTotal` = suma de sus subtotales), FAX07 (impuesto de la línea = base × tarifa / 100, o valor por unidad × cantidad) y VLR01 (ningún importe negativo). Las notas usan las mismas con CA y DA; la nota crédito no tiene CAU12 y la débito valida `RequestedMonetaryTotal`.
- No se exigen, porque el anexo es ambiguo, y se confirman en la fase 1b contra la DIAN: FAV06 (la fórmula usa `Price/BaseQuantity`), FAS07 en la cabecera (no divide la tarifa entre 100), FAU04 (`TaxTotal[1]` frente a todos los subtotales), los márgenes de ±2.00 y de ±5 pesos en el IVA (solo aparecen como texto, sin fórmula) y `PayableRoundingAmount` (sin regla propia).

**Pendiente:**

1. Hecho el 14-nov: fórmulas del CUFE, CUDE y `SoftwareSecurityCode` confirmadas en el código de `torresoftware` (sección 2.1). El 15-nov, el CUFE del primer caso golden (`backend/tests/golden/invoice/two-lines-discounts-iva19`) se recalculó desde el XML con esta fórmula y coincide.
2. Validar contra el XSD de UBL 2.1 el orden de `AdditionalDocumentReference` y `BillingReference` en las notas.
3. Hecho el 21-nov: inventario de las variantes de la fase 7a (secciones 9 a 18). Sus preguntas abiertas están en la sección 18.

## 9. Variantes de la fase 7a: resumen y base común

Las rutas `/api/ubl2.1/...` se borran en la T9 (S1 de la fase 7a), así que una regla marcada **Conservar** en las secciones 9 a 17 se refiere al XML, al código (CUFE, CUDE o CUDS), a los valores por defecto y a los mensajes que deben llegar a `/api/v1`, no a la ruta del PHP.

### 9.1 Resumen por variante

| Variante | Ruta (`POST /api/ubl2.1/…`) | `type_document_id` | Plantilla | `CustomizationID` por defecto | Código y clave | Software y SOAP | Evidencia |
| --- | --- | --- | --- | --- | --- | --- | --- |
| AIU | `invoice-aiu` | 1 | 01 | `09` (operación 9) | CUFE, clave técnica | factura; SendBillSync | RT:179-182; VA:178-179, 298-301 |
| Mandato | `invoice-mandate` | 1 | 01 | `11` (operación 11) | CUFE, clave técnica | factura; SendBillSync | RT:185-188; VM:179 |
| Transporte | `invoice-transport` | 1 | 01 | `12` (operación 21) | CUFE, clave técnica | factura; SendBillSync | RT:191-194; VT:179 |
| Exportación | `invoice-export` | 2 | 02 | `10` (operación 10) | CUFE, clave técnica de la resolución tipo 2 | factura; SendBillSync | RT:161-164; VE:213, 323-325 |
| Contingencia tipo 3 | `invoice-contingency` | 3 | 03 | `10` | CUDE, PIN del software | factura; SendBillSync | RT:167-170; VC:179, 303-305 |
| Contingencia tipo 4 | `invoice-contingency-4` | 12 | 04 | `10` | CUDE con el PIN, aunque `schemeName` diga `CUFE-SHA384` | factura; nada al emitir, SendBillSync en `send_pendings` | RT:173-176; VC:829-832, 1046-1052 |
| Documento soporte | `support-document` | 11 | 05 | `10` (operación 23) u `11` (operación 24) | CUDS, PIN del software DS | software DS; SendBillSync a `url_support_document` | RT:209-212; DS:182, 298-301, 318 |
| Nota de ajuste DS | `sd-credit-note` | 13 | 95 | `10` (operación 10) | CUDS, PIN del software DS | software DS; SendBillSync a `url_support_document` | RT:215-218; NS:160, 265-267, 284 |
| Lotes | `add-to-batch/{tipo}/{batch}`, `send-batch/{batch}` | el del tipo | la del tipo | la del tipo | igual que el tipo | factura; SendBillAsync | RT:221-232; BC:1584-1591 |
| Eventos RADIAN | `send-event`, `send-event-data` | 8 | 88 | `1` (fijo) | CUDE del evento, PIN del software | factura; SendEventUpdateStatus a `url_event` | RT:258-270; SE:631-648 |

Catálogos que lo explican (CSV y MIG): tipos de documento 1 (`01`, `fv`), 2 (`02`, `fv`), 3 (`03`, `CUDE-SHA384`, `fv`), 8 (`88`, `CUDE-SHA384`, `ar`), 11 (`05`, `dse`), 12 (`04`, `CUFE-SHA384`, `fv`) y 13 (`95`, `CUDS-SHA384`, `nds`) (CSV `type_documents.csv:1-8`; MIG `2021_05_19_155825…:18`, `2021_07_28_009503…:20`, `2022_07_21_037503…:17`, `2022_07_26_007503…:17`). Operaciones vigentes tras las migraciones: 5 (`32`), 6 (`30`), 8 (`22`), 9 AIU (`09`), 10 Estándar (`10`), 11 Mandatos (`11`), 12 (`20`), 21 Transporte (`12`), 22 Cambiario (`13`), 23 Residente (`10`) y 24 No residente (`11`); la migración borra 4, 7 y 13 a 20, incluida la antigua 16 «Exportación» (`04`) (CSV `type_operations.csv:1-18`; MIG `2021_07_29_007503…:17-28`, `2022_08_22_729767…:17-20`).

### 9.2 Flujo común de los controladores de variante

AIU, mandato y transporte son copias idénticas entre sí salvo la operación por defecto y una bandera (comparación de VA, VM y VT). Exportación, contingencia y documento soporte parten de la misma copia. Se listan solo las diferencias con la factura nacional (sección 3).

1. Misma secuencia que la factura: disponibilidad de la DIAN, certificado, plan, `ActualizarTablas`, documento en estado 0, XML, firma, ZIP, PDF, SendBillSync, AttachedDocument si `IsValid` y correo (VA:56-558). **Conservar** lo que la sección 3 conserva; las mejoras de la sección 3 (Worker, consecutivo atómico, idempotencia, `plan_quotas`) valen igual para todas las variantes.
2. La plantilla es la del código del tipo de documento (`view("xml.{code}")`, DT:290), así que AIU, mandato y transporte usan la 01 y solo cambian por banderas del request (sección 10). **Conservar**: un solo builder de factura con opciones por variante.
3. Respuesta de éxito **sin `success`** ni `whatsapp_response`, en este orden: `message`, `send_email_success`, `send_email_date_time`, `ResponseDian`, `invoicexml`, `zipinvoicexml`, `unsignedinvoicexml`, `reqfe`, `rptafe`, `attacheddocument`, `urlinvoicexml`, `urlinvoicepdf`, `urlinvoiceattached`, `cufe`, `QRStr`, `certificate_days_left`, `resolution_days_left` (VA:540-558). La contingencia devuelve `cude` (VC:450, 560) y el documento soporte `cuds` (DS:446, 554). **Mejora**: `/api/v1` responde a todas las variantes con la forma de la factura de ImpiFact (sección 3.8) y el nombre del código que corresponda.
4. Solo la factura nacional acepta `query_uuid` (REQ/InvoiceRequest.php:77-78); ninguna variante lo declara. Por eso la herramienta golden necesita el modo sin `query_uuid` (T1). **Conservar**: las variantes de `/api/v1` no necesitan `query_uuid`.
5. El cliente se valida distinto en cada variante, sin una regla de la DIAN que lo explique: AIU exige `phone`, `address` y `merchant_registration` (REQ/InvoiceAIURequest.php:165-168); mandato, además, `email` (REQ/InvoiceMandateRequest.php:164); transporte exige `email` salvo para el consumidor final y deja el resto opcional (REQ/InvoiceTransportRequest.php:166-172); exportación exige `email` (REQ/InvoiceExportRequest.php:199). En ninguna variante `customer.dv` es obligatorio con NIT (REQ/InvoiceAIURequest.php:152), a diferencia de la factura (sección 3.1, ítem 5). **Mejora**: las variantes usan las reglas del cliente de la factura nacional; una regla extra solo entra si la exige el anexo.
6. `payment_form` se valida solo como objeto (REQ/InvoiceAIURequest.php:219-222), pero el controlador también acepta una lista (VA:229-243), que así se salta la validación. **Bug del PHP**; ImpiFact valida las dos formas como la factura (sección 3.1, ítem 9).
7. `type_operation_id` usa `exists:type_operations` sin columna (REQ/InvoiceAIURequest.php:142; igual en mandato, transporte, exportación y contingencia). Laravel busca entonces la columna `type_operation_id`, que no existe en `type_operations`, así que enviar el campo provoca un error de SQL (HTTP 500) y en la práctica siempre se usa la operación por defecto. **Bug del PHP**; ImpiFact fija la operación de cada variante y responde 422 si el cliente manda otra.
8. `tax_totals.*.percent` es obligatorio salvo con `tax_id` 10 (no exime el 2) y no existe `tax_name` (REQ/InvoiceAIURequest.php:235, 293); `discount_id` es obligatorio solo en los descuentos (227). **Mejora**: reglas de impuestos y descuentos de la factura nacional.
9. En la rama `GuardarEn` de AIU el correo sale aunque `sendmail` sea `false` (`isset` sin mirar el valor, VA:404-421) y se usa la variable inexistente `$GuardarEn` (VA:389, 393). **Bug del PHP**; ImpiFact ignora `GuardarEn` (sección 7, fila 3) y envía el correo solo con `sendmail = true`.
10. Si el envío lanza una excepción, la respuesta es texto plano (VA:525-527), como en las notas (sección 4.4, ítem 3). **Bug del PHP.**
11. `testSetStore` de cada variante (`POST …/{testSetId}`) usa SendTestSetAsync, se salta el plan y la empresa activa, deja el documento en estado 0 y responde `urlinvoiceattached = Attachment-{número}.xml` (VA:570-866). **Conservar** el comportamiento de la sección 3.5.
12. Cuota: el mismo `switch` sin `break` (VA:86-119), y la familia factura cuenta solo los tipos 1 a 5 en estado 1 (DT:1559). AIU, mandato, transporte, exportación y contingencia tipo 3 cuentan; la tipo 4 (tipo 12) nunca cuenta. **Mejora**: en ImpiFact todas estas variantes, incluida la tipo 4, consumen la cuota de la familia factura al aceptarse.
13. Nombre DIAN del XML: el prefijo sale del tipo de documento (`fv` para los tipos 1, 2, 3 y 12), pero el contador de `sends` es por empresa, año y **tipo** (DT:1121-1139). Una factura AIU (tipo 1) y una exportación (tipo 2) del mismo año pueden recibir el mismo nombre `fv…`. **Bug del PHP**; ImpiFact lleva el contador por empresa, año y prefijo de archivo.

## 10. AIU, mandato y transporte

**ImpiFact (fase 7a, T2, 21-nov): implementado.** `POST /api/v1/documents/invoices/aiu`, `/mandate` y `/transport` (y los alias `/api/ubl2.1/invoice-aiu`, `/invoice-mandate` e `/invoice-transport`) reproducen el XML y el CUFE de los golden de las tres variantes. Las reglas **Conservar** de esta sección se replican; las **Mejora** de la sección 9.2 (cliente con las reglas de la factura, operación fija con 422 si llega otra, formas de pago validadas) también. Además, el DV del mandante es obligatorio con `agentparty` y se revisa contra el NIT, y `RNDC_consignment_number` debe ser solo dígitos. El mandante sigue siendo opcional por línea (pregunta 4 de la sección 18, abierta).

### 10.1 AIU (`POST invoice-aiu`)

1. `type_document_id` `in:1` y `noteAIU` obligatorio (texto) (REQ/InvoiceAIURequest.php:106, 139). Los errores de resolución son los de la factura (sección 3.1, ítem 1). **Conservar.**
2. Operación por defecto 9, `CustomizationID` `09` (VA:178-179; CSV `type_operations.csv:6`). **Conservar.**
3. La nota «Contrato de servicios AIU por concepto de: {noteAIU}» se escribe como `cbc:Note` **solo en las líneas cuya `description` es exactamente `Administración`** (con tilde), antes de la nota propia de la línea (TPL/_invoice_lines.blade.php:16-23). No hay otra marca de AIU en el XML: administración, imprevistos y utilidad son líneas normales que arma el cliente. **Conservar** el texto y la condición exacta.
4. CUFE con la clave técnica de la resolución, igual que la factura (VA:297-301). **Conservar.**

### 10.2 Mandato (`POST invoice-mandate`)

1. Operación por defecto 11, `CustomizationID` `11` («Mandatos bienes») (VM:179; CSV `type_operations.csv:8`). **Conservar.**
2. `isMandate = TRUE` (VM:286) hace que **todas** las líneas lleven `cbc:ID schemeID="1"` (TPL/_invoice_lines.blade.php:3-4). **Conservar.**
3. Mandante por línea: `agentparty` (NIT, numérico de 1 a 15 dígitos) y `agentparty_dv` (un dígito), ambos opcionales (REQ/InvoiceMandateRequest.php:296-297). Si llega, `cac:Item` lleva `cac:InformationContentProviderParty/cac:PowerOfAttorney/cac:AgentParty/cac:PartyIdentification/cbc:ID` con `schemeAgencyID="195"`, el nombre de agencia de la DIAN, `schemeID` = DV y `schemeName="31"`, después de `StandardItemIdentification` (TPL/_invoice_lines.blade.php:80-90). El bloque no depende de `isMandate`. **Conservar** el XML; si ImpiFact debe exigir el mandante en cada línea queda abierto (sección 18).
4. `customer.email` obligatorio (REQ/InvoiceMandateRequest.php:164). **Mejora**: reglas del cliente de la factura nacional (sección 9.2, ítem 5).

### 10.3 Transporte (`POST invoice-transport`)

1. Operación por defecto 21, `CustomizationID` `12` (VT:179). La semilla la llama «Mandatos Servicios» y la migración la renombra «Transporte» (CSV `type_operations.csv:18`; MIG `2021_07_29_007503…:17`). **Conservar** el código `12`.
2. `isTransport = TRUE` (VT:286): las líneas con `is_RNDC = true` llevan `cbc:ID schemeID="1"` y las demás `schemeID="0"` (TPL/_invoice_lines.blade.php:6-11). **Conservar.**
3. `is_RNDC` es obligatorio (booleano) en cada línea; con `true` son obligatorios `RNDC_consignment_number` (numérico), `internal_consignment_number` (texto), `value_consignment` (entero), `unit_measure_consignment_id` (unidad del catálogo) y `quantity_consignment` (numérico) (REQ/InvoiceTransportRequest.php:286-291). **Conservar.**
4. Con RNDC, `cac:Item` lleva tres `cac:AdditionalItemProperty` después del mandante: `01` con el número de remesa del RNDC, `02` con el número interno y `03` con el valor y `cbc:ValueQuantity unitCode` = código de la unidad, con la cantidad tal como llega, **sin** `number_format` (TPL/_invoice_lines.blade.php:91-107). **Conservar**, incluida la cantidad sin formatear.

## 11. Exportación (`POST invoice-export`)

**ImpiFact (fase 7a, T3, 21-nov): implementado.** `POST /api/v1/documents/invoices/export` y `/test-set/{testSetId}` (y los alias `/api/ubl2.1/invoice-export` e `/invoice-export/{testSetId}`) reproducen byte a byte el XML y el CUFE de los dos golden (`cop-cif-additional-data` y `usd-fob`) con lo que hace el PHP: plantilla 02, resolución de tipo 2 con su propio consecutivo, operación 10 fija (otra responde 422), `cac:DeliveryTerms`, sin `cac:PaymentTerms`, `BrandName`/`ModelName` en las líneas y la dirección de un comprador fuera de Colombia con `00001`, `municipality_name`, `state_name` y `01`. Cuenta en el plan como una factura. Mejoras sobre el PHP: `k_supplement` es opcional (el anexo 1.9, sección 21, dice que el suplemento K no tiene reglas de validación) pero, si llega, deben venir sus 30 campos (11 de `Interoperabilidad` y 19 de `TotalesCop`, no 20 como decía el ítem 5) con texto de hasta 100 caracteres y `FctConvCop` mayor que 0; si falta uno, 422 por campo y nunca 500. La TRM nunca se consulta en línea ni se inventa. **Moneda extranjera (diseño del anexo, sin referencia en el PHP):** sin `idcurrency` o con la moneda de la empresa, el XML va en pesos como el PHP. Con otra moneda, `calculationrate` (mayor que 0) y `calculationratedate` son obligatorios (422 en español si faltan) y el XML sigue el anexo 1.9: `cbc:DocumentCurrencyCode` en esa moneda, todos los `@currencyID` iguales a ella salvo `TotalesCop` (regla FAD15b) y `cac:PaymentExchangeRate` después de los descuentos y cargos con `SourceCurrencyCode` = la moneda, bases `1.00`, `TargetCurrencyCode` COP, `CalculationRate` y `Date` (reglas FAR01 a FAR07). El CUFE no cambia porque no incluye la moneda. Con la entrada del golden `usd-fob`, `/api/v1` escribe así el XML en dólares y no el XML en pesos del PHP (el golden fija el comportamiento del PHP en el builder). Incoterms: la lista del PHP, que es el catálogo `incoterms` sin DPU; DPU sigue rechazado (pregunta 3 de la sección 18). Lo que queda por confirmar en habilitación está en la sección 18.

1. `type_document_id` `in:2`, con su propia resolución tipo 2; el CUFE usa la clave técnica de esa resolución (REQ/InvoiceExportRequest.php:106; VE:323-325). **Conservar.**
2. `deliveryterms` es obligatorio: `special_terms` (texto), `loss_risk_responsibility_code` (uno de CFR, CIP, CPT, DAP, DAT, DDP, FOB, EXW, CIF, FAS, FCA) y `loss_risk` (texto) (REQ/InvoiceExportRequest.php:229-232). El catálogo semilla de Incoterms tiene además DPU (CSV `incoterms.csv`), que la validación rechaza. **Conservar** la lista de la validación; DPU queda abierto (sección 18).
3. XML (plantilla 02): `cac:DeliveryTerms` con `SpecialTerms`, `LossRiskResponsibilityCode` y `LossRisk`, después de `AccountingCustomerParty` y antes de `Delivery`; sin `cac:PaymentTerms`; `InvoiceTypeCode` 02 (TPL/02.blade.php:44-48, 56). **Conservar.**
4. Moneda: el PHP emite la exportación **en la moneda de la empresa**. `idcurrency`, `calculationrate` y `calculationratedate` ni se validan ni llegan a la plantilla, que tiene comentados `DocumentCurrencyCode` en otra moneda y `PaymentExchangeRate` (REQ/InvoiceExportRequest.php:139-141; VE:217-221, 313; TPL/02.blade.php:27-31, 66-68). **Conservar** para la paridad del golden. La moneda extranjera de la T3 no tiene referencia en el PHP: se toma del anexo y se prueba en habilitación (sección 18).
5. Suplemento K (`k_supplement`): como `deliveryterms` siempre llega, si falta `k_supplement.FctConvCop` el PHP lo rellena con la TRM del dólar, leída de la página `DetalleTRM.aspx` de la DIAN, y con **4000.00** si no la consigue (VE:51-78; `app/Http/Controllers/Api/TrmController.php:47, 119-138`). Si existe `k_supplement`, se agrega un tercer `ext:UBLExtension` con `CustomTagGeneral/Interoperabilidad` (11 `AdditionalInformation` con nombres fijos, como «Responsable/Encargado» o «Lugar de Salida») y `TotalesCop` (20 campos, de `FctConvCop` a `TotAnticiposCop`) (TPL/_ubl_extensions.blade.php:36-113). La plantilla lee cada campo sin comprobar que exista, así que un suplemento incompleto, incluido el que arma el propio PHP con solo `FctConvCop`, hace fallar la plantilla. **Bug del PHP** (la TRM inventada y el fallo). ImpiFact escribe el suplemento solo si llega, exige los campos que la plantilla escribe y nunca inventa la TRM.
6. Operación por defecto 10, no la antigua `04` «Exportación» que la migración borró (VE:212-213). **Conservar.**
7. Líneas: `brandname` y `modelname` opcionales (REQ/InvoiceExportRequest.php:342-343). Cliente extranjero: `municipality_name` y `state_name` en texto (REQ/InvoiceExportRequest.php:189-190), que `_accounting` usa cuando el país no es Colombia (TPL/_accounting.blade.php:55-70). **Conservar.**

## 12. Contingencia

### 12.1 Tipo 3 (`POST invoice-contingency`)

1. `type_document_id` `in:3`, con resolución tipo 3. Son obligatorios `AdditionalDocumentReferenceID`, `AdditionalDocumentReferenceDate` (`Y-m-d`), `AdditionalDocumentReferenceTypeDocument` (un `code` de `type_documents`) y `time` (REQ/InvoiceContingencyRequest.php:105, 131-137). **Conservar.**
2. XML (plantilla 03): igual a la 01 sin la rama de salud ni la `additional_document_reference` general; `InvoiceTypeCode` 03 y un `cac:AdditionalDocumentReference` con `ID`, `IssueDate` y `DocumentTypeCode` (la factura de talonario o de papel que respalda) justo después de `LineCountNumeric` y antes de `OrderReference` (TPL/03.blade.php:34-35; TPL/_additional_document_reference.blade.php:1-5). **Conservar**; el orden está antes de `OrderReference`, contra el orden del XSD de UBL 2.1, así que **a validar** contra el XSD como en la sección 4.3.
3. Código: `SignInvoice` sin clave técnica, así que la librería calcula el **CUDE con el PIN del software** (VC:303-305; LIB `XAdES/SignInvoice.php:598-602`) y `UUID@schemeName` es `CUDE-SHA384`. La respuesta lo devuelve como `cude` (VC:450, 560). **Conservar.**
4. Envío, AttachedDocument, correo y respuesta como la base (sección 9.2) (VC:321-560). Operación por defecto 10 (VC:178-179). **Conservar.**
5. Cliente: `phone`, `address` y `email` obligatorios salvo para el consumidor final; `dv` obligatorio si llega `delivery` (REQ/InvoiceContingencyRequest.php:153, 166-168). **Mejora**: reglas del cliente de la factura nacional (sección 9.2, ítem 5).

### 12.2 Tipo 4 (`POST invoice-contingency-4`)

1. `type_document_id` `in:12` (REQ/InvoiceContingencyType4Request.php:106): «Factura electrónica de Venta - tipo 04», código `04`, `CUFE-SHA384`, prefijo `fv` (MIG `2021_07_28_009503…:20`). Plantilla 04: igual a la 01 sin la rama de salud, con `InvoiceTypeCode` 04 (TPL/04.blade.php). Sin `AdditionalDocumentReference` obligatoria. **Conservar.**
2. Antes de emitir busca un documento con el mismo prefijo y número en estado distinto de 0, **sin filtrar por empresa**, y responde «El documento {prefix}{number} ya fue enviado como tipo de documento: {nombre} en la fecha: {created_at}» con `success: false` (VC:647-654). **Bug del PHP (seguridad)**: revela documentos de otras empresas y bloquea sus números. ImpiFact usa el índice único por empresa (sección 3.4).
3. Firma igual que el tipo 3, sin clave técnica, así que el código es un **CUDE con el PIN** aunque `schemeName` diga `CUFE-SHA384` (VC:829-832, 845; LIB `XAdES/SignInvoice.php:598-602`). La extensión no lleva `InvoiceControl`, que solo se escribe para las resoluciones de tipo 1, 2, 3 y 11 (TPL/_ubl_extensions.blade.php:5-9). **A verificar con la DIAN** las dos cosas (sección 18).
4. **No envía a la DIAN.** Arma una respuesta simulada con `IsValid = 'true'`, `StatusCode` `00`, «Procesado Correctamente.» y un `StatusMessage` fijo con un número de factura de pruebas escrito a mano (VC:873-890). **Bug del PHP**: ImpiFact nunca simula respuestas de la DIAN.
5. Guarda el documento en **estado 2** con el CUDE local (VC:908-911) y el nombre DIAN del XML en `Type4XMLFilename-{número}.xml` (VC:892-894). **Conservar** el estado (`ContingencyPending`, sección 6).
6. Arma, firma y guarda el AttachedDocument con un ApplicationResponse vacío (`<ApplicationResponse></ApplicationResponse>`) y fecha y hora de validación nulas, y lo envía por correo al cliente si `sendmail` es `true`, antes de que la DIAN valide (VC:912-960). **Bug del PHP**; ImpiFact arma el AttachedDocument solo con el ApplicationResponse real.
7. Pasa la disponibilidad de la DIAN, el certificado y el plan (VC:582-640) aunque no envía nada. Respuesta como la del tipo 3, sin `reqfe` ni `rptafe` y con `ResponseDian` como arreglo (VC:984-1003). **Mejora**: ImpiFact emite la tipo 4 sin exigir que la DIAN responda, que es el motivo de la contingencia.

### 12.3 Envío de pendientes (`POST invoice-contingency-4/send_pendings/{prefix?}/{number?}`)

1. Sin parámetros toma todos los documentos tipo 12 en estado 2 de la empresa; con `prefix`, los de ese prefijo; con `prefix` y `number`, uno; con solo `number` responde «Para hacer envios los envios pendientes debe al menos suministrar el prefijo de las facturas de contingencia tipo 4 que desea enviar....» (VC:1032-1043). **Conservar** los filtros como disparo manual; en ImpiFact el Worker envía los pendientes solo (T5).
2. Revisa la disponibilidad de la DIAN, el certificado y la empresa activa, pero no el plan (VC:1011-1030). **Conservar**: la cuota ya se revisó al emitir y se cuenta al aceptarse (sección 9.2, ítem 12).
3. Un SendBillSync por documento con el ZIP ya guardado `FES-{prefijo}{número}.zip`, sin volver a firmar; `fileName` = `{prefijo}{número}.xml` (VC:1046-1052). **Conservar** que se envía el mismo XML firmado al emitir.
4. Si la DIAN responde HTML a mitad de la lista, devuelve «El servicio DIAN no se encuentra disponible en el momento, reintente mas tarde...» y abandona el resto sin decir cuáles salieron (VC:1053-1057). **Bug del PHP.**
5. Con `IsValid` pasa a estado 1 (VC:1059-1063). Si no, el documento sigue en estado 2 sin guardar el rechazo y se vuelve a enviar en cada llamada. No guarda el CUFE de la DIAN, no rehace el AttachedDocument (que conserva el ApplicationResponse vacío) ni envía correo. **Bug del PHP**; ImpiFact pasa el documento a `Rejected` o `Accepted` con la respuesta, rehace el AttachedDocument y encola el correo.
6. Respuesta `{success: true, message: «Envios de documentos de contingencia tipo 4 realizados con exito.», responses}`, con `responses` como texto JSON de las respuestas válidas; sin pendientes, `{success: true, message: «No existen registros de documentos de contingencia tipo 4 para realizar envios....»}` (VC:1065-1074). **Conservar** los mensajes.

## 13. Documento soporte (`POST support-document`)

### 13.1 Validación, software y plan

1. `type_document_id` `in:11`; en lugar de `customer` llega `seller` (el vendedor no obligado a facturar), obligatorio (REQ/SupportDocumentRequest.php:98, 139). **Conservar.**
2. `seller`: `identification_number` alfanumérico de 1 a 15; `dv` opcional, validado con módulo 11 si llega; `type_document_identification_id` en 4, 5, 6, 7, 8, 9 u 11; `name`, `phone`, `address`, `merchant_registration` y `postal_zone_code` (numérico) obligatorios (REQ/SupportDocumentRequest.php:139-168). **Conservar.**
3. `seller.country_code` se compara con `LIKE '%{valor}%'` contra `countries.code`, con el error «seller.country code es inválido.» (REQ/SupportDocumentRequest.php:150-156). Acepta fragmentos de código. **Bug del PHP**; ImpiFact compara el código exacto y conserva el mensaje.
4. `type_operation_id` en 23 (residente, `CustomizationID` `10`) o 24 (no residente, `11`), por defecto 23 (REQ/SupportDocumentRequest.php:131; DS:181-182). **Conservar.**
5. Cada línea exige `type_generation_transmition_id` y `start_date` (`Y-m-d`) (REQ/SupportDocumentRequest.php:294-295), que salen como `cac:InvoicePeriod` de la línea con `StartDate`, `DescriptionCode` (el id) y `Description` (el nombre) (TPL/_invoice_lines.blade.php:33-39). Las líneas del tipo 11 no llevan `FreeOfChargeIndicator` (TPL/_invoice_lines.blade.php:40-42). **Conservar.**
6. Software propio: `identifier_support_document`, `pin_support_document` y `url_support_document`, que el alta del software acepta también como `idsupportdocument`, `pinsupportdocument` y `urlsupportdocument` (CC:475-495), y ambiente propio `support_document_type_environment_id`, por defecto 2 (CC:287), que `config/environment` cambia con `support_document_type_environment_id` o `documentos_soporte` (CC:923-972). **Conservar.**
7. Plan propio: `type_plan4`, `start_plan_date4` y `qty_docs_ds`, con los mismos mensajes y el mismo `switch` sin `break` que la factura (DS:76-119); cuenta los tipos 11 y 13 en estado 1 (DT:1569-1572). **Mejora**: familia «documento soporte» en `plan_quotas`.

### 13.2 XML (plantilla 05)

1. `ProfileID` «DIAN 2.1: documento soporte en adquisiciones efectuadas a no obligados a facturar.» (con punto final); `ProfileExecutionID` y `UUID@schemeID` con el ambiente del documento soporte; `schemeName` `CUDS-SHA384`; `InvoiceTypeCode` 05 (TPL/05.blade.php:16-23). **Conservar** el texto exacto: la librería lo compara para elegir el CUDS (LIB `XAdES/SignInvoice.php:599`).
2. **Partes invertidas**: el vendedor va en `AccountingSupplierParty` y la empresa en `AccountingCustomerParty` (TPL/05.blade.php:33-36). `CorporateRegistrationScheme/ID` con el prefijo sale en las dos partes (TPL/_accounting.blade.php:139-141), y el vendedor lleva `PostalZone` de `postal_zone_code` (DS:158; TPL/_accounting.blade.php:42-46). **Conservar.**
3. Sin `OrderReference` ni `AdditionalDocumentReference`; con `PaymentMeans`, `PaymentTerms`, anticipos, cargos y descuentos como la factura (TPL/05.blade.php:37-64). **Conservar.**
4. Moneda: si no llega `idcurrency`, el controlador pone COP con tasa 1 y la **fecha del día**, así que el XML siempre lleva `PaymentExchangeRate` (DS:192-194; TPL/05.blade.php:54-56). **Conservar** para la paridad; la captura golden debe fijar esa fecha.
5. Extensión DIAN: `SoftwareID` = `identifier_support_document` (TPL/_ubl_extensions.blade.php:17-18), `InvoiceControl` con la resolución tipo 11 (TPL/_ubl_extensions.blade.php:5-9) y QR con el catálogo del ambiente del documento soporte (DT:233-238). **Conservar.**

### 13.3 CUDS, envío y respuesta

1. CUDS: SHA-384 de `ID` + `IssueDate` + `IssueTime` + `LineExtensionAmount` + `01` + IVA + `PayableAmount` + NIT de `AccountingSupplierParty` (el **vendedor**) + NIT de `AccountingCustomerParty` (la **empresa**) + **PIN del software DS** + `ProfileExecutionID` (LIB `XAdES/SignInvoice.php:638-662`; DS:298-299). **Conservar** (sección 2.1).
2. `setUUID` calcula el CUFE si la clave técnica no está vacía (LIB `XAdES/SignInvoice.php:598-606`) y el controlador pasa `technicalKey = resolution.technical_key` (DS:300). La clave solo es obligatoria para el tipo 1 (REQ/ConfigurationResolutionRequest.php:32): sin clave sale el CUDS, pero si alguien la configura en la resolución del documento soporte, el PHP escribe un CUFE. **Bug del PHP**; ImpiFact calcula siempre el CUDS para los tipos 05 y 95.
3. `SoftwareSecurityCode` = SHA-384 de `identifier_support_document` + `pin_support_document` + `cbc:ID` (DS:298-299; LIB `XAdES/SignInvoice.php:585`). **Conservar.**
4. SendBillSync a `url_support_document`, y la comprobación de disponibilidad también va a esa URL (DS:59, 317-318). El set de pruebas usa SendTestSetAsync a la misma URL (DS:801-808). **Conservar.**
5. Archivos `DS-` (sin firmar), `DSS-` (firmado y ZIP), `ReqDS-` y `RptaDS-`; nombre DIAN con el prefijo `dse` y AttachedDocument que cambia `dse` por `ad` (DS:313-323, 364; DT:1124). **Conservar** el nombre DIAN.
6. El documento se guarda con `ambient_id` del ambiente del documento soporte y `customer` = NIT del vendedor (DS:328, 345). El PDF usa la plantilla `SUPPORTDOCUMENT` (DS:325). **Conservar.**
7. Correo al vendedor solo con `sendmail = true` y si no es 222222222222 (DS:404-424, 500-519). **Conservar.**
8. Respuesta como la base, con `cuds` y los nombres `DSS-` (DS:433-446, 541-554). **Mejora**: la forma de `/api/v1` (sección 9.2, ítem 3), con `cuds` como nombre del código.

## 14. Nota de ajuste al documento soporte (`POST sd-credit-note`)

1. `type_document_id` `in:13`: código `95`, `CUDS-SHA384`, prefijo `nds` (REQ/sdCreditNoteRequest.php:94; MIG `2022_07_26_007503…:17`). Software, URL, plan (`type_plan4`) y PIN del documento soporte, como la sección 13 (NS:57, 265-267, 284). **Conservar.**
2. `billing_reference` es obligatorio, con `number`, `uuid` de 96 caracteres e `issue_date` (`Y-m-d`): no hay notas de ajuste sin referencia (REQ/sdCreditNoteRequest.php:134-137). El UUID referenciado sale con `schemeName="CUDS-SHA384"` fijo (TPL/_billing_reference.blade.php:4-5). El documento soporte original no se busca en la base (NS:253). **Mejora**, como en la sección 4.2: si el CUDS es de un documento soporte de la empresa, debe estar aceptado.
3. `discrepancyresponsecode` es opcional, entre 1 y 6 (REQ/sdCreditNoteRequest.php:130), contra el catálogo `credit_note_discrepancy_responses_sd` de 5 códigos (CSV `credit_note_discrepancy_responses_sd.csv:1-5`): el 6 revienta en `findOrFail`. Sin código no hay `DiscrepancyResponse`; con código, `Description` sale del catálogo, no del request (TPL/95.blade.php:35-37; TPL/_discrepancy_response.blade.php:6-16). **Bug del PHP** el 6; ImpiFact valida de 1 a 5. Si debe ser obligatorio queda abierto (sección 18).
4. `type_operation_id` se valida en 23 o 24 (REQ/sdCreditNoteRequest.php:107), pero el valor por defecto es 10 (NS:159-160), que también da `CustomizationID` `10`. **Conservar** el código `10` por defecto.
5. XML (plantilla 95): `CreditNoteTypeCode` 95; `ProfileID` «DIAN 2.1: Nota de ajuste al documento soporte en adquisiciones efectuadas a sujetos no obligados a expedir factura o documento equivalente»; ambiente del documento soporte; partes invertidas como en la sección 13.2; orden `DiscrepancyResponse` → `BillingReference` → partes → `PaymentMeans` → `PaymentExchangeRate` (solo si llega `idcurrency`) → cargos → impuestos → retenciones → `LegalMonetaryTotal` → líneas; sin `InvoicePeriod`, `OrderReference` ni `AdditionalDocumentReference` (TPL/95.blade.php:17-59; NS:162-175). **Conservar.**
6. CUDS: `SignCreditNote` sin clave técnica y con el `ProfileID` de la nota de ajuste, así que la librería elige el CUDS con el PIN del documento soporte y `LegalMonetaryTotal` (NS:265-267; LIB `XAdES/SignInvoice.php:598-602`). La respuesta lo devuelve como `cuds` (NS:412, 518). **Conservar.**
7. `sts:SoftwareID` sale con el `identifier` del software de **factura**, porque la plantilla usa el del documento soporte solo cuando el código es 05 (TPL/_ubl_extensions.blade.php:15-21), mientras que el `SoftwareSecurityCode` se calcula con el del documento soporte (NS:266-267). **Bug del PHP**; ImpiFact usa el software del documento soporte en los dos.
8. Archivos `NDSNS-` (NS:287-296, 409); el documento se guarda con el `ambient_id` del ambiente de **factura** (NS:311). **Bug del PHP** el ambiente; ImpiFact guarda el del documento soporte.

## 15. Lotes (`add-to-batch` y `send-batch`)

No hay tabla ni migración de lotes: un lote es solo el archivo `zip/{company_id}/{batch}.zip`, al que cada «agregar» suma un XML (DT:930-971). Postman no tiene lotes, así que la referencia es el PHP (S2.3).

### 15.1 Agregar al lote (`POST add-to-batch/{tipo}/{batch}`)

1. Siete acciones, cada una con la misma validación que su ruta individual: `invoice` (tipo 1), `invoice-aiu` (1), `invoice-mandate` (1), `invoice-export` (2), `invoice-contingency` (3), `credit-note` (4 o 26) y `debit-note` (5 o 25) (RT:222-228; BC:51, 277, 505, 730, 966, 1183, 1380). No hay lote para transporte, documento soporte, nota de ajuste ni set de pruebas. **Conservar** los siete tipos.
2. `{batch}` es un texto libre del cliente, que se usa tal cual como nombre del ZIP (DT:930). **Mejora**: ImpiFact genera el identificador del lote y valida sus caracteres.
3. Igual que la ruta individual: `ActualizarTablas`, documento en estado 0, tipo de operación por defecto, XML, firma con clave técnica (facturas) o sin ella (contingencia y notas), consumo del contador de `sends` y PDF (BC:61-236). **Conservar**: el XML de cada documento del lote es el mismo que el de su ruta individual.
4. Distinto de la ruta individual: no revisa la disponibilidad de la DIAN, el certificado, el plan, la empresa activa ni los duplicados, y no envía nada (BC:51-265 frente a IC:208-369). **Mejora**: ImpiFact aplica al agregar las mismas validaciones que a un documento individual.
5. El CUFE o CUDE se calcula pero no se guarda en el documento (BC:214, 236). **Mejora**: ImpiFact lo guarda al agregar.
6. `createPDF` recibe 13 argumentos cuando la firma del método exige 14 (`$healthfields` no tiene valor por defecto) (BC:214; DT:324). En PHP 8 eso lanza `ArgumentCountError` después de agregar el XML al ZIP y gastar el consecutivo, y antes de guardar el documento. La contingencia no genera PDF y no falla, pero devuelve `urlinvoicepdf` igual (BC:1154, 1169). **Bug del PHP.**
7. Respuesta: `message` «{tipo} #{número} generada y agregada con éxito al batch {batch}.zip», `invoicexml`, `zipinvoicexml` (el ZIP completo del lote hasta ese momento, en base64), `unsignedinvoicexml`, `urlinvoicexml`, `urlinvoicepdf` y `cufe` (facturas) o `cude` (contingencia y notas), más `QRStr` salvo en la contingencia; sin `success` (BC:240-265). **Mejora**: ImpiFact no devuelve el ZIP completo en cada agregado.

### 15.2 Enviar el lote (`POST send-batch/{batch}`)

1. Sin cuerpo ni validación: SendBillAsync a `software.url` con `fileName` = `{batch}.zip` y el ZIP en base64 (BC:1576-1591; LIB `Templates/SOAP/SendBillAsync.php:18, 25-28`). No comprueba que el ZIP exista (falla con HTTP 500) ni revisa plan o certificado. **Conservar** la operación SOAP; **Mejora** en las validaciones.
2. El ZIP lleva todos los XML agregados con ese nombre desde siempre, sin límite de cantidad ni de tamaño, y nunca se borra; reutilizar el nombre reenvía documentos viejos (DT:960-971). Si `ZipArchive::open` falla, se reescribe con solo el XML actual (DT:961-965). **Bug del PHP.**
3. Respuesta: `message` «Batch {batch}.zip enviado con éxito», `ResponseDian` (el sobre SOAP crudo), `zipinvoicexml`, `reqfe` y `rptafe` (`ReqBATCH-`/`RptaBATCH-{batch}.xml`) (BC:1598-1604). No lee el `ZipKey` ni cambia ningún documento. **Bug del PHP**; ImpiFact guarda el `ZipKey` y el Worker consulta el estado.
4. El seguimiento es manual con `status/zip/{trackId}`, que lee **una** `DianResponse` y pasa a estado 1 un solo documento por llamada (SC:71, 101-215). La búsqueda del `ZipKey` en los archivos solo mira `RptaFE-*`, no `RptaBATCH-*` (DT:1238, 1248). **Bug del PHP**; ImpiFact (T8) actualiza cada documento del lote con su respuesta.

## 16. Eventos RADIAN y documentos recibidos

### 16.1 Rutas

1. Con `auth:api` (RT:59): `send-event` y `send-event-data` (SE@sendevent, SE@sendeventdata; RT:258-260, 268-270), `query-events-prefix-number/{prefix}/{number}` y `query-events-uuid/{uuid}` (RT:263-265, 273-275), `query-events-cufe-dian/{cufe}/{ambiente}`, que lee la página pública `ShowDocumentToPublic` de la DIAN (RT:278-280; SE:39-43), `status/events-document/{trackId}`, que usa GetStatusEvent (RT:238; SC:853), y, sin el prefijo `ubl2.1`, `process-seller-document-reception` y `load-document-by-cufe` (RT:66, 69). **Conservar** las operaciones; en ImpiFact viven en `/api/v1` (las rutas heredadas se borran en la T9).
2. Sin autenticación: `POST accept-reject-document` y `POST download-file` (RT:357-359). La primera firma y envía eventos (incluidos 031 y 033) de **cualquier** empresa que nombre el request, con el certificado de esa empresa (ARC:97-166). **Bug del PHP (seguridad, crítico)**; ImpiFact no la replica: aceptar o rechazar se hace desde el panel o la API con credencial (RF-16).

### 16.2 Contrato de `send-event` y `send-event-data`

1. `send-event`: `event_id` obligatorio (del catálogo `events`); `base64_attacheddocument` y `base64_attacheddocument_name` obligatorios; `issuer_party` opcional con `identification_number`, `first_name`, `last_name`, `organization_department` y `job_title` (cada uno `required_with:issuer_party`); `type_rejection_id` obligatorio con `event_id` 2; opcionales `resend_consecutive`, `sendmail`, `sendmailtome`, `email_cc_list` y los datos del establecimiento para el PDF (REQ/SendEventRequest.php:26-69). **Conservar.**
2. `send-event-data`: los mismos campos con `document_reference.cufe` obligatorio en lugar del AttachedDocument, más `send_email_cc_list_as_email_cc` y `validate_email`; el documento se baja de la DIAN con GetXmlByDocumentKey a `url_event` y el código debe ser `100` (REQ/SendEventDataRequest.php:47-101; SE:963, 1021-1034). **Conservar.**
3. Validaciones previas, con los mensajes de la factura (DIAN no disponible, certificado vencido, plan inactivo, empresa inactiva, límites del plan) (SE:236-298). **Conservar** los mensajes.
4. Errores del archivo en `send-event`, textuales (SE:310-335): «El archivo no se pudo cargar, revise los problemas asociados», «El archivo no es un AttachedDocument XML», «En el archivo no se encontro el ApplicationResponse dentro del AttachedDocument XML», «El archivo no corresponde al AttachedDocument XML de un documento Invoice ó el AttachedDocument no es valido ya que no tiene el cotenedor CDATA del Invoice» y «El archivo no corresponde al AttachedDocument XML de un documento valido ya que no tiene el contenedor CDATA del ApplicationResponse DIAN». **Conservar** los mensajes.
5. Adquiriente equivocado: «El archivo no corresponde un AttachedDocument XML del adquiriente {nit}» (SE:445) y, en `send-event-data`, «El adquiriente no corresponde a la empresa con nit: {nit}» (SE:1221). **Conservar.**
6. Evento repetido: «Ya se registro este evento para este documento.» (SE:496-532); `send-event-data` añade `cude`, `date_issue`, `seller_id`, `seller_name`, `invoice_number` («prefijo - número») e `invoice_cufe` (SE:1276-1285). **Conservar.**
7. El archivo se escribe en `received/{nit}/{base64_attacheddocument_name}` sin sanear el nombre y **antes** de comprobar el adquiriente (SE:396-398 frente a 442). `GuardarEn` también decide rutas de disco (SE:634-663). **Bug del PHP (seguridad)**; ImpiFact genera los nombres en el servidor.
8. Respuesta: `success` (**siempre `true`** cuando la DIAN contesta, aunque `IsValid` sea `false`), `message` «ApplicationResponse #{prefijo}{número} generada con éxito», `ResponseDian`, `cude` y `certificate_days_left`; con `IsValid`, además `transmitter_id`, `transmitter_name`, `receiver_id`, `receiver_name`, `invoice_number`, `invoice_date`, `invoice_cufe`, `invoice_total` e `invoice_tax` (SE:767-794). Si la DIAN responde HTML: «El servicio DIAN no se encuentra disponible en el momento, reintente mas tarde...» (SE:671-675; falta en `send-event-data`). **Mejora**: `success` refleja la aceptación de la DIAN.

### 16.3 Catálogo de eventos y orden entre ellos

| id | Código | Nombre | Quién lo emite en el PHP | Columna |
| --- | --- | --- | --- | --- |
| 1 | 030 | Acuse de recibo de Factura Electrónica de Venta | la empresa adquiriente | `acu_recibo` |
| 2 | 031 | Reclamo de la Factura Electrónica de Venta | la empresa adquiriente | `rechazo` |
| 3 | 032 | Recibo del bien y/o prestación del servicio | la empresa adquiriente | `rec_bienes` |
| 4 | 033 | Aceptación expresa | la empresa adquiriente | `aceptacion` |
| 5 | 034 | Aceptación Tácita | la empresa emisora, con la DIAN como receptor | `aceptacion` |

Evidencia: CSV `events.csv:1-5`; columnas en SE:813-840. Los ids 6 (`02`) y 7 (`04`) del catálogo no son eventos.

1. `ResponseCode` = código del evento y `Description` = nombre (TPL/88.blade.php:44-49). Con 031, `ResponseCode` lleva `listID` y `name` del motivo de `type_rejections`: `01` Documento con inconsistencias, `02` Mercancía no entregada totalmente, `03` Mercancía no entregada parcialmente, `04` Servicio no prestado (CSV `type_rejections.csv:1-4`). **Conservar.**
2. 034: el receptor es la DIAN con datos fijos (nombre, NIT 800197268, DV 4, organización 1, régimen 1, responsabilidad 117) y una nota de declaración que menciona los 3 días hábiles (SE:561-570, 612-621). **Conservar.**
3. **El PHP no aplica ningún orden entre eventos.** Nada exige el 030 antes del 032, el 032 antes del 033 o del 034, ni impide un 033 después de un 031. La única regla es un evento por columna, y 033 y 034 comparten `aceptacion` (SE:496-532). El plazo de 3 días hábiles de la aceptación tácita no se calcula: solo aparece en el texto de la nota. **Mejora** (T6): ImpiFact aplica el orden y los plazos del Decreto 1154 de 2020 y del anexo RADIAN (030 → 032 → 031 o 033; 034 solo por el emisor después del plazo sin reclamo) y los confirma en habilitación; la DIAN también los valida (sección 18).
4. `resend_consecutive` borra la marca del evento y cambia el ID para reenviarlo (SE:453-491; TPL/88.blade.php:18-22). Tiene un `case 4` repetido y no borra el 5 (SE:483-489). **Bug del PHP** el `case`; **Mejora**: ImpiFact reintenta con el Worker, no con un ID aleatorio.

### 16.4 XML del ApplicationResponse (plantilla 88)

1. Cabecera: `CustomizationID` `1`; `ProfileID` «DIAN 2.1: ApplicationResponse de la Factura Electrónica de Venta»; `ProfileExecutionID` y `UUID@schemeID` con el ambiente de **factura** (`type_environment`), no el de eventos; `schemeName` `CUDE-SHA384` (TPL/88.blade.php:14-23). **Conservar**; la elección del ambiente queda abierta (sección 18).
2. `cbc:ID` = NIT del receptor + número de la factura referenciada + código del evento, con un sufijo `-{0 a 9}` aleatorio si `resend_consecutive` (TPL/88.blade.php:18-22). No hay resolución ni consecutivo. **Conservar**; **Mejora** en el sufijo (ítem 16.3.4).
3. `IssueDate` e `IssueTime` = ahora, con `-05:00` (TPL/88.blade.php:24-25). `Note` solo en el 034. **Conservar.**
4. `SenderParty` = la empresa de la API (adquiriente en 030 a 033, emisora en 034): `CompanyID` con `schemeID` = DV, `schemeVersionID` = tipo de organización y `schemeName="31"` fijo; `TaxLevelCode` y `TaxScheme` de la empresa (SE:537-547; TPL/88.blade.php:29-40). **Conservar.**
5. `ReceiverParty` = el vendedor, leído de la factura: nombre del primer `RegistrationName`, DV recalculado, organización por `AdditionalAccountID`, régimen por `TaxLevelCode@listName` y responsabilidad 117 fija (SE:550-584; TPL/_customer_attached.blade.php:1-17). Su `CompanyID@schemeName` sale del tipo de identificación, que nunca se asigna, así que siempre vale `13` (`app/Company.php:208-215`), también para NIT y para la DIAN. **A verificar con la DIAN** (sección 18).
6. `DocumentResponse/DocumentReference`: `ID` = número de la factura, `UUID` con `schemeName="CUFE-SHA384"` fijo y el CUFE de la factura, y `DocumentTypeCode` = `TypeDocument::findOrFail(InvoiceTypeCode)->code` (SE:451; TPL/88.blade.php:51-55; `app/DocumentReference.php:102-105`). Usa el código como **id**, así que solo los códigos 01, 02 y 03 dan el `DocumentTypeCode` correcto. **Bug del PHP**; ImpiFact usa el código del documento.
7. `IssuerParty/Person`: del request, o la empresa por defecto (nombre como nombre y apellido, cargo `ADMINISTRADOR`, área `ADMINISTRATIVA`), con `schemeName="13"` (TPL/_issuer_party.blade.php:1-17). **Conservar** los valores por defecto.
8. Extensión: sin `InvoiceControl`; `SoftwareID` del software de factura; `SoftwareSecurityCode` como la factura; `QRCode` siempre con el catálogo de producción y el **CUFE de la factura**, no el CUDE del evento (TPL/_ubl_extensions_appresponse.blade.php:1-34; DT:276-277; LIB `XAdES/SignInvoice.php:707, 716-719`). **Conservar** la extensión; el QR queda abierto (sección 18).

### 16.5 CUDE del evento

1. La librería lo calcula cuando el XML contiene `</ApplicationResponse>` y no `</AttachedDocument>` (LIB `XAdES/SignInvoice.php:186-187`): SHA-384 de `ID` + `IssueDate` + `IssueTime` (con `-05:00`) + NIT de `SenderParty` + NIT de `ReceiverParty` + `ResponseCode` + `DocumentReference/ID` + `DocumentReference/DocumentTypeCode` + PIN del software de factura (`software.pin`), **sin `ProfileExecutionID`** (LIB `XAdES/SignInvoice.php:704-714`; SE:631-632). **Conservar.**
2. La respuesta devuelve `ConsultarCUDEEVENT()`, que recalcula el mismo valor (SE:668). **Conservar**: el CUDE de la respuesta es el del XML firmado.

### 16.6 Envío, respuesta y persistencia

1. SendEventUpdateStatus (SOAP 1.2, ZIP en base64) a `software.url_event`, que cambia entre habilitación y producción según `event_type_environment_id` (LIB `Templates/SOAP/SendEvent.php:18, 47-55`; SE:648; CC:978-988). **Conservar.**
2. Nombre del XML y del ZIP: `ar` + código + NIT a 10 dígitos + `000` + año a 2 dígitos + `prefijo.número` a 8 dígitos (DT:1181-1188; SE:655). En los documentos recibidos el número ya trae el prefijo, así que el prefijo se repite. **Conservar** el formato; el prefijo repetido queda abierto (sección 18).
3. Se lee `SendEventUpdateStatusResult->IsValid == 'true'` (SE:677). Con `IsValid` se marca la columna del evento y se guardan `cude_{columna}` y `payload_{columna}` (el JSON de la respuesta) en `received_documents`, o en `documents` para el 034 (SE:813-840, 1541). **Conservar.**
4. Regla 90: si `ErrorMessage->string` es exactamente «Regla: 90, Rechazo: Documento procesado anteriormente.», marca el evento como hecho y guarda un CUDE **recalculado** solo si estaba vacío (SE:861-913), que no es el que la DIAN registró. **Conservar** que la regla 90 cuenta como hecho; **Bug del PHP** el CUDE recalculado, ImpiFact consulta el real con GetStatusEvents.
5. Un solo intento SOAP por solicitud, sin reintentos (SE:648-677; sección 3.6, ítem 5). **Mejora**: los reintentos los hace el Worker.
6. `status/events-document/{trackId}` usa GetStatusEvent con `software.url` (no `url_event`) y devuelve `events[]` con `event_number`, `dian_code`, `description`, `date`, `time` y `cude`, leídos de cada `DocumentResponse` (SC:868-930). **Conservar** los campos.

### 16.7 Documentos recibidos

1. Entran por tres caminos: `send-event` con el AttachedDocument (crea el registro, SE:396-447), `send-event-data` y `load-document-by-cufe` con GetXmlByDocumentKey (guardan `xml` y `pdf` como `no-attached-document`, SE:1021-1225; RE:157-283), y la carga del portal web del vendedor (`SellerLoginController.php:80-190`). **Conservar** los dos primeros; el portal queda fuera de la 7a.
2. Campos leídos del XML: NIT y DV del vendedor (DV recalculado), nombre (primer `RegistrationName`), `type_document_id` = `InvoiceTypeCode`, adquiriente, prefijo y número (el primer `ID` que contiene el `sts:Prefix`), CUFE (primer `UUID`), fecha y hora sin `-05:00`, `sale` = `TaxInclusiveAmount`, descuentos, subtotal, impuestos = `sale - subtotal`, total = `PayableAmount` y ambiente = `ProfileExecutionID` (SE:407-447). **Conservar** los campos.
3. Tabla `received_documents`: `acu_recibo`, `rec_bienes`, `aceptacion` y `rechazo`, cada uno con `cude_*` y `payload_*`, borrado lógico y siempre `state_document_id = 1` (MIG `2022_06_15_161907…:16-42`, `2022_07_13_156700…:24-31`). En `documents` solo quedan `aceptacion`, `cude_aceptacion` y `payload_aceptacion` (MIG `2022_07_13_156700…:17-21`). **Conservar** el modelo (ya existen `ReceivedDocument` y `DocumentEvent` en ImpiFact).
4. `load-document-by-cufe`: `cufe` obligatorio («El campo cufe es requerido.») y `save` opcional; mensajes «La empresa no tiene configurado el software de facturación.», «El CUFE ingresado no corresponde al XML de un documento Invoice.», «El adquiriente del documento no corresponde a la empresa con nit: {nit}», «El documento ya existe en la base de datos.» (`already_exists: true`), «El documento fue cargado y guardado satisfactoriamente.» (`saved: true`) y «Datos del documento obtenidos satisfactoriamente.» (`saved: false`) (RE:142-301). **Conservar** los mensajes.
5. `process-seller-document-reception` (`company_idnumber` y `xml_document` en base64) solo analiza el XML, no guarda nada y acepta cualquier `company_idnumber` (RE:24-124). **Bug del PHP**; ImpiFact toma la empresa de la credencial.
6. Cuota: la familia RADIAN (`start_plan_date3`) cuenta los documentos recibidos más las aceptaciones de documentos propios (DT:1563-1565). **Mejora**: familia «eventos» en `plan_quotas`.

### 16.8 Consultas de eventos

1. `query-events-prefix-number` y `query-events-uuid` responden con las marcas del documento: si todas valen 0, `{success, 'Aceptación Tácita': true, …}`; si no, una clave por evento con su nombre completo; sin documento, «El documento no fue encontrado en la base de datos...» (SE:149-214). Leen columnas que la migración borró de `documents`, y la consulta por UUID no filtra por empresa (SE:189). **Bug del PHP**; ImpiFact responde desde `DocumentEvent`, filtrado por empresa.
2. `query-events-cufe-dian` lee la página pública de la DIAN y devuelve `Evento_030` a `Evento_034`, o `{success: "false", message: "Sin eventos!"}`; pierde el evento de la posición 0 salvo que sea el 030 (SE:45-146). **Mejora**: ImpiFact usa GetStatusEvents, no la página web.

### 16.9 PDF y correo

1. PDF siempre, con la plantilla `pdfs.event`, como `EVS-{NIT}{número}{código}.pdf`; rotula «CUDE» al CUFE de la factura y elige el host del QR por el ambiente de **nómina** (DT:713-758; `resources/views/pdfs/event/template.blade.php:58`). **Bug del PHP** el rótulo y el ambiente.
2. No se arma AttachedDocument para los eventos: SE no usa `SignAttachedDocument` en ningún punto (SE:1-1735). **Conservar.**
3. Correo solo con `IsValid` y `sendmail`, al correo del vendedor que trae el XML, con copia a la empresa si `sendmailtome` y a `email_cc_list` (SE:737-753); asunto «Evento: {nit};{nombre};{nit}{número}{código};{tipo};{nombre}» (`app/Mail/EventMail.php:53`). El ZIP adjunto se arma con un nombre que no coincide con el XML guardado, así que el adjunto probablemente falla (DT:871-885). **Conservar** el asunto; **Bug del PHP** el adjunto.

## 17. Sector salud (sin RIPS)

1. Community lo desactiva: `$healthfields = NULL` con el comentario «Health Fields (Enterprise) deshabilitado en Community» (IC:384-385, 936-937), y el parcial `_ubl_extensions_health` al que remite la plantilla no existe (TPL/01.blade.php:14-15). **No hay referencia de paridad en el PHP.**
2. Los 7 casos de la carpeta 07 de Postman («Sector salud») van a `POST invoice` con `health_fields`: `invoice_period_start_date`, `invoice_period_end_date`, `health_type_operation_id`, `print_users_info_to_pdf` y `users_info[]` (tipo de documento de salud, tipo de usuario, modalidad de pago, cobertura, autorizaciones, MIPRES, contrato, póliza, copago, cuota moderadora, cuota de recuperación y pagos compartidos). La validación no declara `health_fields` y el controlador no lo lee (REQ/InvoiceRequest.php; IC:384-447), así que el PHP emite una factura nacional normal: sin la extensión de salud, sin `InvoicePeriod` y con `CustomizationID` `10`. **Mejora**: ImpiFact lee `health_fields` (ítem 4); esos casos del PHP no sirven como golden del sector salud.
3. Lo que sí está: el catálogo de tipos de documento de salud (MIG `2021_04_23_175732_create_health_type_document_identifications_table.php`; CSV `health_type_document_identifications.csv`), la rama de la plantilla (`CustomizationID` = código de la operación de salud e `InvoicePeriod`, TPL/01.blade.php:20-25, 42-45) y la librería, que firma en el **tercer** `ExtensionContent` si el XML contiene `www.minsalud.gov.co` (LIB `XAdES/SignInvoice.php:197-198`). **Conservar** la posición de la firma.
4. **Mejora** (T7, S2.4): el XML del sector salud se toma del anexo técnico del Ministerio de Salud para la factura electrónica (versión vigente a confirmar) y no del PHP, porque el PHP no lo genera (ítems 1 y 2); las pruebas salen de los ejemplos del anexo y de habilitación, no de un golden del PHP.

## 18. Implicaciones para la fase 7a y preguntas abiertas

**Lo que más afecta a la implementación:**

1. El código depende de la clave que recibe la librería, no del tipo: CUFE con clave técnica (AIU, mandato, transporte, exportación), CUDE con PIN (contingencia 3 y 4), CUDS con PIN del software DS (05 y 95) y CUDE del evento sin `ProfileExecutionID` (secciones 9.1 y 16.5). Cada variante necesita su golden.
2. AIU, mandato y transporte son la factura 01 con banderas por línea: un solo builder de factura con opciones (T2).
3. Documento soporte y nota de ajuste invierten las partes y usan otro software, PIN, URL, ambiente y plan (secciones 13 y 14).
4. La contingencia tipo 4 no envía al emitir y sus pendientes se envían después con el mismo XML firmado (sección 12).
5. Los lotes no guardan el `ZipKey`, y los eventos no aplican orden ni plazos: en los dos el PHP no sirve como especificación completa (secciones 15 y 16.3).

**Preguntas que el código no responde** (a confirmar con el anexo o en habilitación):

1. Tipo 04: ¿CUDE con PIN o CUFE con clave técnica, y con `InvoiceControl` o sin él? El PHP escribe un CUDE con `schemeName` `CUFE-SHA384` y sin `InvoiceControl` (sección 12.2, ítem 3). No se puede forzar en habilitación (S2.6).
2. Contingencia tipo 3: ¿`AdditionalDocumentReference` antes de `OrderReference` pasa el XSD? (sección 12.1, ítem 2).
3. Exportación: ¿qué exige la DIAN para emitir en moneda extranjera (T3) y se acepta DPU? (sección 11, ítems 2 y 4). **T3 (21-nov):** ImpiFact implementa la moneda extranjera con las reglas FAD15a, FAD15b y FAR01 a FAR07 del anexo 1.9 (sección 11) y rechaza DPU como el PHP, porque la lista 13.3.7 del anexo está en la caja de herramientas de la DIAN y no en el repositorio. Quedan por probar en habilitación con la DIAN real, antes de darlas por buenas: (a) una exportación en USD con todos los valores en USD y `cac:PaymentExchangeRate` después de `cac:AllowanceCharge`; (b) si la DIAN acepta DPU; (c) si `name`/`value` en minúscula dentro de `AdditionalInformation` (como el PHP; el anexo los escribe `Name`/`Value`) sirven para la representación gráfica; (d) si la DIAN acepta una factura de exportación y una nacional con el mismo prefijo y número cuando las resoluciones de tipo 1 y 2 comparten prefijo y rango (los dos consecutivos son independientes, como en el PHP, y la colección Postman configura las dos con `SETP` 990000000–995000000); (e) si `FctConvCop` y `MonedaCop` del suplemento K deben coincidir con `calculationrate` e `idcurrency` (hoy no se comparan).
4. Mandato: ¿el mandante (`AgentParty`) es obligatorio en cada línea? (sección 10.2, ítem 3).
5. Nota de ajuste DS: ¿`DiscrepancyResponse` es obligatorio? (sección 14, ítem 3).
6. RADIAN: ¿la DIAN acepta `schemeName="13"` para el NIT del receptor y del emisor del evento? ¿Qué ambiente va en `ProfileExecutionID` cuando el de eventos y el de factura difieren? ¿El QR del evento lleva el CUFE de la factura o el CUDE del evento? ¿El prefijo repetido en el nombre del archivo es aceptado? (sección 16.4 y 16.6).
7. RADIAN: orden y plazos exactos de los eventos 030 a 034 en el anexo vigente (sección 16.3, ítem 3).
8. Lotes: límite de documentos y tamaño del ZIP en SendBillAsync, formato del nombre del ZIP, si se pueden mezclar tipos y cómo responde GetStatusZip con varios documentos (sección 15).
9. Sector salud: versión vigente del anexo del Ministerio de Salud y ejemplos aceptados (sección 17).
