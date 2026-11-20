# Despliegue en Azure (Bicep)

Plantilla de referencia para correr ImpiFact en Azure con el mapa de servicios de la sección 14 de `docs/Arquitectura detallada.md`. **No se ha desplegado**: solo se valida sin conexión (decisión de la fase 6: el despliegue real se hace al elegir la nube).

Se eligió Bicep porque es la herramienta nativa de Azure: no guarda estado propio, los parámetros `@secure()` no quedan en el historial de despliegues y `bicep build` revisa los tipos de cada recurso sin una cuenta.

## Qué crea

| Necesidad | Recurso |
| --- | --- |
| Cómputo | Container Apps en un ambiente con red virtual propia: `impifact-api` (ingress público), `impifact-worker` (sin ingress) e `impifact-web` (ingress público). Gotenberg y el OpenTelemetry Collector van como contenedores auxiliares de la API y del Worker |
| Migrator | Job manual de Container Apps (`impifact-migrator`), que se corre antes de cada versión |
| Base de datos | Según `databaseEngine`: Azure Database for PostgreSQL (por omisión), Azure Database for MySQL o Azure SQL, sin acceso público (subred delegada o *private endpoint*) |
| Blobs | Cuenta de Storage sin llaves compartidas ni acceso público, con el contenedor `impifact-documents` |
| Secretos | Key Vault con RBAC y protección contra purga: la llave RSA `impifact-data-protection` (rotación automática cada 90 días) y los secretos de configuración |
| Observabilidad | Log Analytics (consola de los contenedores) y Application Insights (trazas, métricas y registros por OTLP, a través del Collector) |
| Identidad | Tres identidades administradas asignadas por el usuario: carga de trabajo (API y Worker), Migrator y panel |

Roles de la identidad de la carga de trabajo, los de la sección 11: «Storage Blob Data Contributor» sobre el contenedor, «Storage Blob Delegator» sobre la cuenta, «Key Vault Secrets Officer» y «Key Vault Crypto User» sobre el vault. El Migrator solo tiene «Key Vault Secrets User» sobre el secreto de la cadena de conexión. Con `containerRegistryName`, las tres reciben `AcrPull` sobre ese registro.

## Configuración que fija

API y Worker: `Database__Provider`, `Database__ConnectionString` (referencia a Key Vault), `Storage__Provider=AzureBlob`, `Storage__AzureBlob__ServiceUri`, `Storage__AzureBlob__Container`, `Secrets__Provider=KeyVault`, `Secrets__KeyVault__VaultUri`, `Secrets__KeyVault__KeyName`, `AZURE_CLIENT_ID`, `Pdf__GotenbergUrl=http://127.0.0.1:3000` y `OTEL_EXPORTER_OTLP_ENDPOINT=http://127.0.0.1:4317`. Solo la API: `Jwt__SigningKey` (referencia a Key Vault), `Cors__Origins__0` y `ForwardedHeaders__KnownNetworks__0`. Solo el Worker: `Panel__BaseUrl` y `Email__*`. El panel: `IMPIFACT_API_BASE_URL`. Las sondas usan `/health/live` y `/health/ready` (puerto 8080 en la API, 8091 en el Worker).

`ForwardedHeaders__KnownNetworks__0` es la subred de infraestructura del ambiente (`appsSubnetPrefix`): el proxy de la plataforma (Envoy) llega a la API desde ella y agrega la IP real del cliente en `X-Forwarded-For`, que usa el límite de intentos de inicio de sesión. Conviene comprobarlo en el primer despliegue: con un inicio de sesión fallido, el registro de la API debe mostrar la IP pública del cliente y no una de la subred.

## Requisitos

- Un grupo de recursos y una identidad con permiso para crear recursos y asignar roles en él (por ejemplo «Owner» o «Contributor» más «Role Based Access Control Administrator»).
- Las cuatro imágenes publicadas en un registro (`imageRegistry` e `imageTag`), con los nombres `impifact-api`, `impifact-worker`, `impifact-migrator` e `impifact-web`.
- Dos nombres de dominio del mismo dominio registrable, por ejemplo `api.impifact.co` y `panel.impifact.co`: la cookie de sesión es `SameSite=Strict`, así que la API y el panel deben estar en el mismo sitio.

## Desplegar

Los valores secretos nunca van en `main.bicepparam`: se leen de variables de entorno de la consola que despliega.

```sh
export IMPIFACT_DATABASE_PASSWORD='<contraseña de 16 caracteres o más, sin ; ni comillas>'
export IMPIFACT_JWT_SIGNING_KEY="$(openssl ecparam -name prime256v1 -genkey -noout | openssl pkcs8 -topk8 -nocrypt)"
export IMPIFACT_EMAIL_PASSWORD='<solo si emailUsername no está vacío>'
az deployment group create -g <grupo> -f main.bicep -p main.bicepparam
```

Los dominios propios se activan en tres pasos con `customDomainStage`:

1. `none` (primer despliegue). Con las salidas, crear en el DNS un `CNAME` de cada nombre hacia `apiDefaultFqdn` y `panelDefaultFqdn`, y un `TXT` `asuid.<nombre>` con `customDomainVerificationId`.
2. `validate`: agrega los nombres sin TLS y pide los certificados administrados. Esperar a que estén emitidos (`az containerapp env certificate list -g <grupo> -n cae-impifact`).
3. `bound`: enlaza los certificados. Se deja así en todos los despliegues siguientes.

## Migrator y primera llave

Antes de cada versión (y antes de que la API y el Worker puedan arrancar por primera vez):

```sh
az containerapp job start -g <grupo> -n impifact-migrator
az containerapp job execution list -g <grupo> -n impifact-migrator -o table
```

La API y el Worker no arrancan si faltan migraciones: se reinician hasta que el Migrator termina. Para la primera llave de plataforma se corre el mismo job con otro comando, por ejemplo `az containerapp job start -g <grupo> -n impifact-migrator --image <registro>/impifact-migrator:<tag> --command dotnet --args "ImpiFact.Migrator.dll create-platform-key admin"`; la llave sale una sola vez en el registro de la ejecución.

## Validar sin conexión

Desde esta carpeta, con la CLI de Bicep (o `az bicep`), sin iniciar sesión en Azure:

```sh
bicep build main.bicep --stdout > /dev/null
bicep lint main.bicep
IMPIFACT_DATABASE_PASSWORD=offline-placeholder-16 IMPIFACT_JWT_SIGNING_KEY=x bicep build-params main.bicepparam --stdout > /dev/null
```

## Pendiente al desplegar de verdad

- La rotación de la llave de Key Vault crea versiones nuevas; la aplicación guarda la versión exacta que envolvió cada llave de Data Protection, así que las anteriores siguen sirviendo. No borrar versiones viejas.
- Key Vault y Storage quedan con acceso público de red y RBAC. Para cerrarlos a la red virtual hacen falta *private endpoints* y sus zonas de DNS.
- Tamaños: PostgreSQL y MySQL en `Standard_B1ms`, Azure SQL en serverless `GP_S_Gen5_1`; la API y el Worker con 1,25 vCPU y 2,5 GiB (0,5 y 1 GiB de la aplicación, el resto para Gotenberg y el Collector). Ajustar con las pruebas de carga.
