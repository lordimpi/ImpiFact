# Pruebas de carga

Pruebas de carga reproducibles de ImpiFact con [k6](https://grafana.com/docs/k6/) contra el stack local en contenedores: emisión de facturas y consultas (listado con filtros y páginas, detalle y tablero). Este documento dice cómo correrlas, qué miden, qué se observó el 20-nov-2026 y qué cuellos de botella aparecieron. Los scripts están en `load-tests/`.

## 1. Garantía: ninguna prueba llega a la DIAN

La emisión deja cada factura en la bandeja de salida (outbox) y el Worker la envía a la DIAN. En estas pruebas el Worker sí corre, pero contra una DIAN falsa, y hay tres barreras independientes:

1. **Dirección falsa.** `load-tests/compose.load.yml` cambia `Dian__HabilitacionUrl` y `Dian__ProduccionUrl` de la API y del Worker a `http://fake-dian:8080/WcfDianCustomerServices.svc`, un servicio del mismo stack (`load-tests/fake-dian/fake_dian.py`, solo biblioteca estándar de Python). Responde cada operación SOAP con la forma de las respuestas de las pruebas de integración (`DianReplies.cs`): `SendBillSync` y `GetStatus` aceptan con código `00`. Las direcciones del código QR también apuntan a ella; nadie las llama.
2. **Nombres bloqueados.** Dentro de los contenedores de la API y del Worker, `vpfe-hab.dian.gov.co`, `vpfe.dian.gov.co` y los dos `catalogo-vpfe…` resuelven a `127.0.0.1` (`extra_hosts`), donde no escucha nada.
3. **Red sin salida.** La red del proyecto es `internal: true`: ningún contenedor del stack puede abrir conexiones fuera de ella, ni a la DIAN ni a internet. Por eso no se publican puertos y k6 corre dentro de la misma red.

Cómo se verificó en cada corrida:

- `load-tests/run.sh egress-check` revisa que la red sea interna y que `https://vpfe-hab.dian.gov.co` y `https://vpfe.dian.gov.co` no respondan desde ella.
- `load-tests/run.sh fake-dian-stats` cuenta las llamadas que recibió la DIAN falsa por operación y por el host del encabezado `wsa:To` del sobre SOAP (la dirección a la que el Worker cree que envía). En todas las corridas el único host fue `fake-dian`.
- Los registros de la API y del Worker no nombran `dian.gov.co` ni una sola vez; las únicas direcciones HTTP que aparecen son `http://fake-dian` y `http://gotenberg`.

## 2. Qué hay en `load-tests/`

| Archivo | Qué hace |
| --- | --- |
| `compose.load.yml` | Override de `deploy/docker-compose.yml` (perfil `app`): proyecto `impifact-t10`, red interna, sin puertos publicados, imágenes propias `impifact-t10-*:local` (no pisa las `impifact-*:local` del stack principal), DIAN falsa, límites de peticiones altos y el servicio `k6` (perfil `load`). |
| `compose.sqlserver.yml` | Segundo override: Migrator, API y Worker sobre SQL Server. PostgreSQL no arranca, así que nunca corren dos motores a la vez. |
| `fake-dian/fake_dian.py` | DIAN falsa; `GET /stats` da los conteos. |
| `lib/config.js` | URL de la API, la API key (solo por variable de entorno), etapas y umbrales comunes. |
| `lib/invoice.js` | Cuerpo de la factura, tomado del caso golden `backend/tests/golden/invoice/single-line-iva19/input.json`. |
| `lib/summary.js` | Resumen final: JSON completo y una tabla por endpoint. |
| `scenarios/issue-invoices.js` | Escenario de emisión. |
| `scenarios/queries.js` | Escenario de consultas. |
| `sql/pipeline-postgres.sql`, `sql/pipeline-sqlserver.sql` | Estado de documentos y outbox, y tiempo de emisión a aceptación. |
| `sql/lock-watch-sqlserver.sql` | Una muestra de los bloqueos del outbox en SQL Server. |
| `run.sh` | Envuelve todos los pasos. |

## 3. Cómo correrlas

Requisitos: Docker con Compose 2.24 o superior (usa `!reset` y `!override`), bash (en Windows, Git Bash) y `openssl`. Todo se corre desde la raíz del repositorio. `LOAD_DIR` es una carpeta fuera del repositorio donde quedan los secretos desechables, la API key y los resultados.

```bash
export LOAD_DIR=/ruta/fuera/del/repo/carga
load-tests/run.sh secrets        # llave maestra, llave JWT y contraseñas nuevas en $LOAD_DIR/load.env
load-tests/run.sh up             # construye y levanta Migrator, API, Worker, PostgreSQL, Gotenberg, MailHog y la DIAN falsa
load-tests/run.sh egress-check   # la red no tiene salida
load-tests/run.sh seed           # empresa demo (seed-demo --force); la API key queda en $LOAD_DIR/api-key
load-tests/run.sh k6 scenarios/issue-invoices.js
load-tests/run.sh k6 scenarios/queries.js
load-tests/run.sh sql load-tests/sql/pipeline-postgres.sql
load-tests/run.sh fake-dian-stats
load-tests/run.sh down           # down -v y borra las imágenes impifact-t10-*
```

Para SQL Server, las mismas órdenes con `DB=sqlserver` delante (por ejemplo `DB=sqlserver load-tests/run.sh up`) y `sql/pipeline-sqlserver.sql`; `DB=sqlserver load-tests/run.sh lock-watch 600` muestrea los bloqueos cada segundo durante 600 s, en CSV.

Sin el script, cada paso es un `docker compose` normal:

```bash
docker compose -p impifact-t10 --env-file "$LOAD_DIR/load.env" \
  -f deploy/docker-compose.yml -f load-tests/compose.load.yml \
  --profile app --profile load up -d --wait migrator api worker
IMPIFACT_API_KEY="$(cat "$LOAD_DIR/api-key")" docker compose -p impifact-t10 --env-file "$LOAD_DIR/load.env" \
  -f deploy/docker-compose.yml -f load-tests/compose.load.yml \
  --profile app --profile load run --rm -e IMPIFACT_API_KEY k6 run scenarios/issue-invoices.js
```

En Git Bash hay que exportar `MSYS_NO_PATHCONV=1` (el script lo hace) para que las rutas del contenedor no se conviertan en rutas de Windows.

Variables de k6 (con `-e NOMBRE=valor` después del script en `run.sh k6`):

| Variable | Por omisión | Uso |
| --- | --- | --- |
| `IMPIFACT_API_KEY` | (obligatoria) | API key de la empresa; `run.sh k6` la pasa como variable de entorno, nunca en un archivo del repositorio. |
| `VUS` | 20 emisión, 40 consultas | Usuarios virtuales en la meseta. |
| `RAMP_UP`, `HOLD`, `RAMP_DOWN` | `1m`, `3m`, `30s` | Duración de la subida, la meseta y la bajada. Una prueba de humo: `-e VUS=2 -e HOLD=20s`. |
| `SEED_DOCS` | 300 | Consultas: documentos mínimos de la empresa; `setup()` emite los que falten. |
| `THINK_MS` | 0 | Emisión: pausa entre facturas de cada usuario virtual. |
| `SUMMARY_NAME` | nombre del escenario | Nombre del resumen JSON en `$LOAD_DIR/results`. |
| `LOG_ERRORS` | (apagado) | Emisión: escribe el cuerpo de cada respuesta 4xx o 5xx. |

Ajustes del stack de carga que difieren de la configuración normal, y por qué:

- `RateLimit__PermitPerMinutePerKey` y `RateLimit__PermitPerMinutePerIp` en 100 000 (el máximo que aceptan las opciones). Con los valores normales (120 por minuto por llave) la prueba mediría el limitador y no la aplicación.
- `APP_ENVIRONMENT=Production`, como en un servidor; por eso `seed-demo` lleva `--force`.

k6 termina con código 99 si algún umbral no se cumple, así que la corrida falla en un script o en CI.

## 4. Escenarios y umbrales

Los dos escenarios usan el ejecutor `ramping-vus`: suben hasta `VUS`, sostienen la meseta y bajan. Cada petición lleva la etiqueta `endpoint`; los umbrales y el resumen van por esa etiqueta. Todas las peticiones se autentican con la API key de la empresa demo (`Authorization: Bearer apk_…`), con los alcances `documents:read` y `documents:write`.

**Emisión** (`scenarios/issue-invoices.js`). Cada iteración es un `POST /api/v1/documents/invoices` real: validación, numeración, XML UBL, firma, archivos y outbox. El cuerpo es el del caso golden `single-line-iva19` con estos cambios: sin `number` (la API toma el siguiente de la resolución SETP), fecha y hora actuales de Colombia, un cliente de un grupo de 50 NIT ficticios (desde 800100001, sin los de dígito de verificación 0; ver la sección 7) y un precio que varía con su IVA del 19 % exacto. Cada petición lleva un `Idempotency-Key` único.

**Consultas** (`scenarios/queries.js`). `setup()` asegura `SEED_DOCS` documentos y toma hasta 2000 ids. Cada iteración elige:

- 40 %: `GET /api/v1/documents?limit=50` con un filtro al azar (`kind`, `status`, `from`/`to` del día, `customer` por NIT o por texto, `number`), 30 % de las veces con `include_total=true`, y luego hasta dos páginas más con `next_cursor`;
- 40 %: `GET /api/v1/documents/{id}`;
- 20 %: `GET /api/v1/dashboard/summary`.

| Endpoint (etiqueta) | p95 | p99 | Errores | Checks |
| --- | --- | --- | --- | --- |
| `issue_invoice` | < 1500 ms | < 3000 ms | < 1 % | > 99 % |
| `list_documents` | < 500 ms | < 1000 ms | < 1 % | > 99 % |
| `list_documents_next_page` | < 500 ms | < 1000 ms | < 1 % | > 99 % |
| `get_document` | < 300 ms | < 600 ms | < 1 % | > 99 % |
| `dashboard_summary` | < 800 ms | < 1500 ms | < 1 % | > 99 % |

Los umbrales son metas para el stack local, no medidas ajustadas para que pasen. Un error es una respuesta HTTP 4xx o 5xx o una falla de red (`http_req_failed`). Los checks revisan además que la emisión devuelva un CUFE de 96 caracteres hexadecimales y que el detalle devuelva el mismo id pedido.

Resumen: `handleSummary` escribe `<SUMMARY_NAME>-summary.json` (el resumen completo de k6) en `$LOAD_DIR/results` y muestra una tabla con solicitudes, RPS, p50, p95, p99, máximo y porcentaje de fallas por endpoint, más el resultado de cada umbral. No usa la biblioteca remota `k6-summary` porque la red de la prueba no tiene salida.

## 5. Equipo y stack usados

| | |
| --- | --- |
| Equipo | Portátil con AMD Ryzen 7 7435HS (8 núcleos, 16 hilos) y 15,8 GB de RAM, Windows 11 Home |
| Docker | Docker Desktop 29.8.2 sobre WSL 2, con 16 CPU y 7,7 GB de memoria para los contenedores |
| Al mismo tiempo | Otros contenedores del equipo (SonarQube con su base, que usaba 1,7 GB, y el PostgreSQL, Gotenberg y MailHog del stack principal, en reposo) y compilaciones de imágenes de otras tareas en algunos momentos |
| Stack | Imágenes de la rama `develop` (71da7ae): Migrator, API y Worker de `Dockerfile.*`, PostgreSQL 18.1, SQL Server 2022 Developer (memoria limitada a 1536 MB), Gotenberg 8.39.0, MailHog 1.0.1, DIAN falsa en `python:3.13.13-alpine3.23` y `grafana/k6:2.3.0` |
| Configuración | La del perfil `app` (`APP_ENVIRONMENT=Production`, almacenamiento `Local`, secretos `LocalAesGcm`, Worker con `Outbox:BatchSize` 20 y `PollSeconds` 2), una instancia de API y una de Worker |
| Datos | Una empresa (`seed-demo`) con la resolución SETP; los documentos se crean en la misma corrida |

Las cifras son de un solo equipo de desarrollo con otras cargas: sirven para comparar corridas y encontrar cuellos de botella, no como capacidad de producción.

## 6. Resultados

RPS es el promedio de toda la corrida, con la subida y la bajada incluidas. Latencias en milisegundos.

### 6.1 PostgreSQL

**Emisión con el Worker corriendo** (20 usuarios virtuales: 1 min de subida, 3 min de meseta, 30 s de bajada).

| Endpoint | Solicitudes | RPS | p50 | p90 | p95 | p99 | Máx. | Errores |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `issue_invoice` | 2870 | 10,6 | 1146 | 3490 | 4681 | 7192 | 16 111 | 0 % |

Umbrales: errores y checks pasan; p95 y p99 no (4681 > 1500 y 7192 > 3000). La corrida termina con código 99.

**Emisión con el Worker detenido** (20 usuarios virtuales: 30 s, 90 s, 10 s), para ver la API sola.

| Endpoint | Solicitudes | RPS | p50 | p90 | p95 | p99 | Máx. | Errores |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `issue_invoice` | 1938 | 14,9 | 884 | 2387 | 2967 | 4708 | 8294 | 0 % |

**Worker.** Cada factura genera tres mensajes en la outbox: `SendDocumentToDian` al emitir, `RenderPdf` al quedar aceptada y `SendEmail` después del PDF. Sin carga en la API, el Worker sacó unos 4,2 mensajes por segundo, unos 2 envíos a la DIAN falsa por segundo. Al terminar la primera corrida de emisión había 2912 facturas: 1410 aceptadas, con una mediana de 88 s y un p95 de 117 s desde la emisión hasta la aceptación, y 1502 todavía en cola. La cola crece mientras la API emita más rápido de lo que el Worker procesa (sección 7). Todas las facturas que llegaron a la DIAN falsa quedaron aceptadas (`00`).

**Consultas** (40 usuarios virtuales: 1 min, 3 min, 30 s; 4850 documentos en la empresa; Worker detenido).

| Endpoint | Solicitudes | RPS | p50 | p90 | p95 | p99 | Máx. | Errores |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `list_documents` | 61 090 | 225,6 | 24 | 78 | 103 | 157 | 457 | 0 % |
| `list_documents_next_page` | 103 662 | 382,9 | 20 | 42 | 53 | 86 | 350 | 0 % |
| `get_document` | 61 309 | 226,4 | 31 | 59 | 74 | 115 | 326 | 0 % |
| `dashboard_summary` | 30 675 | 113,3 | 62 | 107 | 127 | 188 | 547 | 0 % |
| Total | 256 748 | 948,3 | | | | | | 0 % |

Todos los umbrales pasan. PostgreSQL usó entre 7 y 9 núcleos y la API entre 3 y 4,5.

### 6.2 SQL Server

Una corrida de emisión para medir el reclamo de la outbox con más de 5000 filas, la que pedía la verificación de la tarea de SQL Server: el reclamo podía escalar a un bloqueo de toda la tabla. Pasos:

1. Worker detenido, 20 usuarios virtuales durante 6,5 min: 6108 facturas en cola.
2. Se arranca el Worker y, 20 s después, otra corrida de emisión de 20 usuarios virtuales durante 3,3 min. Así el Worker reclama con más de 6000 filas pendientes mientras la API inserta filas nuevas.
3. `run.sh lock-watch` muestrea cada 2 s, en `sql/lock-watch-sqlserver.sql`, los contadores de escalado de `sys.dm_db_index_operational_stats`, los bloqueos de `sys.dm_tran_locks` y las peticiones bloqueadas. Al final se leen `sys.dm_os_wait_stats` y el plan del reclamo en `sys.dm_exec_query_stats`.

| Corrida | Solicitudes | RPS | p50 | p90 | p95 | p99 | Máx. | Errores |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Emisión, Worker detenido (30 s, 6 min, 10 s) | 6108 | 15,3 | 1217 | 1442 | 1591 | 1947 | 3188 | 0 % |
| Emisión, Worker reclamando más de 6000 filas (10 s, 3 min, 10 s) | 2275 | 11,4 | 1721 | 1946 | 2178 | 2520 | 2766 | 0 % |

En las dos, p95 no cumple el umbral (más de 1500 ms); p99, errores y checks sí.

**Escalado de bloqueos del outbox:**

| Medida | Valor |
| --- | --- |
| Plan del reclamo | `Clustered Index Scan` de `outbox_messages`: el índice `(status, next_attempt_at)` no se usa. Promedio de 43 ms y 840 lecturas lógicas por reclamo, máximo de 83 ms, 20 filas devueltas. |
| Con 6100 a 8400 filas pendientes vencidas y la API insertando | 1302 intentos de escalado y 207 escalados a un bloqueo `X` de toda la tabla en unos 4,5 min. En 9 de 166 muestras había un bloqueo de toda la tabla concedido. |
| Inserciones bloqueadas por ese bloqueo | `LCK_M_IX`: 66 esperas en total, 814 ms entre todas, la más larga de 46 ms. |
| Con 2330 filas pendientes vencidas y 14 500 a 16 800 filas en la tabla (prueba de diagnóstico, ver abajo) | 517 intentos más en 2 min y ningún escalado nuevo. |
| Para comparar: el consecutivo | `LCK_M_U` sobre `numbering_sequences`: 32 245 esperas y 13 602 s en total, la más larga de 2988 ms. |

La prueba de diagnóstico de la cuarta fila corrió solo en esta base desechable. Se dejaron los `RenderPdf` pendientes con `next_attempt_at` una hora adelante, así quedaron menos de 5000 filas vencidas con la tabla igual de grande.

Conclusión:

- El escalado ocurre. Como el reclamo recorre toda la tabla, SQL Server pide escalar en cada reclamo cuando la tabla pasa de unas 5000 filas, haya o no trabajo pendiente.
- El escalado se concretó cuando había más de 5000 filas vencidas. Entonces el reclamo tiene toda la tabla bloqueada en exclusivo durante sus 40 a 80 ms, y las inserciones de la API esperan.
- Con este volumen el efecto es pequeño (46 ms como máximo) frente a la espera del consecutivo. Crece con el tamaño de la tabla, porque los mensajes hechos se guardan 30 días (`ScheduledJobs:OutboxRetentionDays`).

En la sección 7 hay opciones para corregirlo. No se tocó el código del producto.

**Worker en SQL Server.** Con solo mensajes `SendDocumentToDian` en la cola (los `RenderPdf` nuevos quedan detrás, en orden de `next_attempt_at`), el Worker envió 5159 documentos en unos 275 s: unos 19 por segundo, diez veces más que en PostgreSQL con PDF de por medio. En SQL Server solo se corrió la emisión con el outbox; las consultas se midieron en PostgreSQL. SQL Server usó 1 GB de memoria y entre 50 % y 60 % de un núcleo durante la emisión.

## 7. Cuellos de botella y hallazgos

Nada de esto se corrigió en estas pruebas: el alcance era medir y documentar. Cada punto trae su evidencia y las opciones, para decidirlas aparte.

### 7.1 La emisión de una resolución va de a una factura

Es el cuello de botella principal. El consecutivo de la resolución (`UPDATE numbering_sequences SET next_number = next_number + 1 …`) toma el bloqueo de su fila y lo mantiene hasta el final de la transacción de la emisión. Dentro de esa transacción están también el XML UBL, la firma, los archivos y la outbox. Así, las facturas de una misma resolución se emiten una detrás de otra.

Evidencia:

- **PostgreSQL**, con 20 usuarios virtuales: en cada muestra de `pg_stat_activity` de la meseta, 17 a 19 sesiones esperaban (`Lock:transactionid`) sobre ese `UPDATE`. La que tenía el bloqueo aparecía «idle in transaction», es decir, la API trabajaba con el bloqueo tomado.
- **SQL Server**: 32 245 esperas `LCK_M_U` en `numbering_sequences`, con 13 602 s en total.
- **La API** usó un núcleo (100 % a 120 %) de los 16 disponibles.
- **El techo** quedó en unas 15 facturas por segundo por resolución con el Worker detenido, y en unas 11 con el Worker corriendo en el mismo equipo. Más clientes solo alargan la espera: con 20 clientes, la mediana pasa de 1 s.

Empresas distintas, o resoluciones distintas, no comparten la fila, así que el techo es por resolución y no de todo el sistema.

Opciones, todas con efectos sobre la numeración:

- Tomar el número en una transacción corta propia, aceptando huecos si la emisión falla después.
- Sacar de la transacción lo que no depende del número.
- Documentar el techo por resolución para los integradores.

### 7.2 Un solo Worker no alcanza a la API, y el PDF es lo más lento

- **La diferencia.** La API emite de 10 a 15 facturas por segundo. Un Worker con `BatchSize` 20 completa unas 1,4 por segundo, con sus tres mensajes: unos 4,2 mensajes por segundo en total. Así, la cola crece durante cualquier ráfaga sostenida.
- **El PDF.** Con solo `SendDocumentToDian` en la cola, el Worker envía unos 19 documentos por segundo (SQL Server). La diferencia la pone `RenderPdf`: Gotenberg usó de 2,3 a 5,3 núcleos (3,3 de mediana) durante la emisión.
- **La tanda.** El Worker espera a que termine toda la tanda (`Task.WhenAll`) antes de reclamar la siguiente, así que un PDF lento detiene los otros 19 lugares.
- **El orden.** La cola va en orden de `next_attempt_at`, así que con atraso los PDF y correos de las facturas nuevas esperan a que se envíen todas las anteriores.

Opciones: más réplicas del Worker (el reclamo ya admite varias instancias), más instancias de Gotenberg, una tanda más grande o un procesamiento continuo en lugar de por tandas.

### 7.3 SQL Server: el reclamo del outbox recorre toda la tabla y escala a bloqueo de tabla

Está medido en la sección 6.2.

- **La causa.** El reclamo de `SqlServerOutboxQueueSql` filtra con `type IN (…)` y `((status = pendiente AND next_attempt_at <= @now) OR (status = en proceso AND locked_until < @now))`. Con esa forma el optimizador elige un recorrido completo del índice agrupado y no usa `ix_outbox_messages_status_next_attempt_at`.
- **Lo que se midió.** Con `UPDLOCK` en el recorrido, SQL Server pide escalar en cuanto la tabla supera unas 5000 filas. Con más de 5000 filas vencidas lo logró: 207 veces, bloqueando toda la tabla durante el reclamo. Las inserciones de la API esperaron como máximo 46 ms.
- **Por qué puede crecer.** El costo crece con la tabla entera, también con las filas ya hechas que se guardan 30 días.

Opciones:

- Que el reclamo pueda buscar por índice: dos búsquedas unidas (`UNION ALL`) por `status`, o un índice filtrado para los estados pendiente y en proceso.
- `ALTER TABLE outbox_messages SET (LOCK_ESCALATION = DISABLE)`: más memoria para los bloqueos.
- Una retención más corta de los mensajes hechos.

PostgreSQL no tiene este problema: `FOR UPDATE SKIP LOCKED` no escala.

### 7.4 Posible error: `customer.dv` igual a `"0"` se toma como ausente

Al emitir a un cliente con NIT (`type_document_identification_id` 6) cuyo dígito de verificación es 0, la API responde 422 con «El campo customer.dv es obligatorio cuando customer.type document identification id es 6.», aunque el cuerpo traiga `"dv": "0"`. Pasó en 4 de 46 facturas de la prueba de humo, las de los NIT del grupo con dígito 0 (aproximadamente uno de cada once NIT).

La causa está en `IssueDocumentValidator`: `customer.Dv is { IsPhpEmpty: false }`, y `LegacyScalar.IsPhpEmpty` trata `"0"` como vacío, igual que `empty()` de PHP. Si la intención era imitar la regla `required_if` de la validación de origen, esa regla sí acepta `"0"`, así que el comportamiento parece un error. Falta confirmarlo contra la referencia. Mientras tanto, el escenario de emisión deja fuera los NIT con dígito 0.

### 7.5 Carrera en el primer contador de nombres de archivo del Worker

Al procesar a la vez varios `SendDocumentToDian` de una empresa que todavía no tiene la fila del contador de nombres de archivo del año, varios mensajes intentan crearla. Todos menos uno fallan con la llave única `ux_numbering_sequences_company_id_scope_key` (`PersistenceConflictException`). Pasó 3 veces en PostgreSQL y 19 en SQL Server, todas en la primera tanda.

El mensaje se reintenta y sale bien en el segundo intento, así que no se pierde ni se duplica nada. Lo que cuesta es la espera del reintento y registros de nivel `Error` en cada arranque en frío. Opciones: crear la fila con `INSERT … ON CONFLICT DO NOTHING` (o su equivalente por motor), o reintentar en el momento ante ese conflicto.

### 7.6 Consultas: holgadas hoy, pero crecen con los documentos de la empresa

Con 4850 documentos, las consultas cumplen todos los umbrales con amplio margen: p95 de 127 ms o menos a unos 950 RPS. PostgreSQL es lo que más trabaja: de 7 a 9 núcleos, contra 3 a 4,5 de la API. En `pg_stat_activity` se repetían dos consultas, y las dos recorren todos los documentos de la empresa, así que crecen con ellos:

- el listado con `customer`, que busca dentro del JSON del cliente (`customer_snapshot ->> …`) con o sin `count(*)`;
- la agregación por mes del tablero (`to_char(issued_at …, 'YYYY-MM')`).

Conviene repetir el escenario de consultas con una empresa de cientos de miles de documentos antes de dar por buenos los índices.

## 8. Límites de estas pruebas

- Una sola empresa con una sola resolución: el bloqueo del consecutivo (sección 7) pesa más que con muchas empresas, que no comparten fila.
- Almacenamiento `Local` y secretos `LocalAesGcm`: las pruebas no miden Blob, S3, GCS ni los almacenes de secretos de la nube.
- La DIAN falsa responde al instante (`FAKE_DIAN_LATENCY_MS=0`); la real tarda alrededor de un segundo, lo que haría más lento al Worker, no a la API.
- No se probó MySQL ni el panel web; el escenario de consultas usa las mismas rutas que el panel.
- Las cifras dependen del equipo y de lo que corría al mismo tiempo (sección 5).
