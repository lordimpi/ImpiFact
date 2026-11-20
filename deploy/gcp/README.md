# Despliegue en GCP (Terraform)

Plantilla de referencia para correr ImpiFact en Google Cloud con el mapa de servicios de la sección 14 de `docs/Arquitectura detallada.md`. **No se ha desplegado**: solo se valida sin conexión (decisión de la fase 6: el despliegue real se hace al elegir la nube).

Se eligió Terraform (proveedor `hashicorp/google`) porque es la herramienta más usada para GCP (Deployment Manager dejó de tener soporte) y la misma de la plantilla de AWS; con Terraform 1.11 o superior, las variables efímeras y los argumentos de solo escritura (`password_wo`, `secret_data_wo`) mandan los secretos a Cloud SQL y a Secret Manager sin guardarlos en el estado.

## Qué crea

| Necesidad | Recurso |
| --- | --- |
| Cómputo | Cloud Run: servicios `impifact-api` e `impifact-web` (públicos, en los dominios propios) e `impifact-worker` (ingress interno, CPU siempre asignada). Gotenberg y el OpenTelemetry Collector van como contenedores auxiliares de la API y del Worker |
| Migrator | Job de Cloud Run `impifact-migrator`, que se ejecuta antes de cada versión |
| Entrada | Asignaciones de dominio de Cloud Run: TLS lo termina Cloud Run con certificados administrados |
| Base de datos | Cloud SQL según `database_engine`: PostgreSQL (por omisión), MySQL o SQL Server, solo con IP privada, TLS obligatorio y protección contra borrado |
| Blobs | Bucket con acceso uniforme y sin acceso público |
| Secretos | Cloud KMS (anillo `impifact`, llave `data-protection` con rotación automática cada 90 días) y los secretos de configuración en Secret Manager |
| Observabilidad | Cloud Logging (consola), Cloud Trace (trazas), Managed Service for Prometheus (métricas) y Cloud Logging (registros OTLP), a través del Collector |
| Identidad | Cuentas de servicio sin llaves: `impifact-runtime` (API y Worker), `impifact-migrator` y `impifact-web` |
| Red | VPC con una subred para la salida directa de Cloud Run y acceso privado a servicios para Cloud SQL |

Permisos de `impifact-runtime`, los de la sección 11: «Storage Object User» sobre el bucket más `storage.buckets.get` (rol propio sobre el bucket; sin él, `/health/ready` queda en 503), «Service Account Token Creator» sobre sí misma (la firma de las URL la hace la API de IAM), «Secret Manager Admin» sobre el proyecto (crea y borra los secretos `impifact-cert-*`), y «Cloud KMS CryptoKey Encrypter/Decrypter» y «Cloud KMS Viewer» sobre la llave; además, los del Collector (`cloudtrace.agent`, `monitoring.metricWriter` y `logging.logWriter`). El Migrator solo puede leer el secreto de la cadena de conexión; la cuenta del panel no tiene roles.

## Configuración que fija

API y Worker: `Database__Provider`, `Database__ConnectionString` (secreto), `Storage__Provider=Gcs`, `Storage__Gcs__Bucket`, `Secrets__Provider=GcpSecrets`, `Secrets__GcpSecrets__ProjectId`, `Secrets__GcpSecrets__KmsKeyName`, `Pdf__GotenbergUrl=http://127.0.0.1:3000` y `OTEL_EXPORTER_OTLP_ENDPOINT=http://127.0.0.1:4317`. Solo la API: `Jwt__SigningKey` (secreto), `Cors__Origins__0` y `ForwardedHeaders__KnownNetworks__*`. Solo el Worker: `Panel__BaseUrl` y `Email__*`. El panel: `IMPIFACT_API_BASE_URL`. `Storage__Gcs__Endpoint` y `Secrets__GcpSecrets__Endpoint` quedan vacías: son solo para los emuladores.

Salud: la sonda de arranque usa `/health/ready` (una revisión nueva recibe tráfico solo cuando la base, el almacenamiento y los secretos responden) y la de vida `/health/live`, en el puerto 8080 de la API y el 8091 del Worker.

`ForwardedHeaders__KnownNetworks__*` sale de `forwarded_known_networks`, por omisión `169.254.0.0/16`. El *front end* de Cloud Run agrega al final de `X-Forwarded-For` la IP desde la que llegó el cliente, y la conexión al contenedor llega desde una dirección interna que Google no documenta. **Hay que comprobarlo en el primer despliegue**: con un inicio de sesión fallido, el registro de la API debe mostrar la IP pública del cliente; si muestra una interna, poner su rango en la variable. Por la misma razón no se usa un balanceador externo delante de Cloud Run: agrega su propia IP al final del encabezado y el límite de intentos de inicio de sesión vería una sola IP para todos.

## Requisitos

- Terraform 1.11 o superior y una identidad con permisos de dueño del proyecto (o los roles de cada servicio).
- Un bucket de Cloud Storage para el estado (`backend.hcl`, no se versiona) con `bucket` y `prefix`. El estado no guarda contraseñas, pero sí la topología: acceso restringido.
- Las cuatro imágenes en Artifact Registry u otro registro (`image_registry` e `image_tag`), con los nombres `impifact-api`, `impifact-worker`, `impifact-migrator` e `impifact-web`. Con Artifact Registry en el mismo proyecto, el agente de servicio de Cloud Run ya puede bajarlas.
- Dos nombres del mismo dominio registrable (por ejemplo `api.impifact.co` y `panel.impifact.co`): la cookie de sesión es `SameSite=Strict` y las direcciones `*.run.app` son sitios distintos entre sí. El dominio tiene que estar verificado en Search Console por la identidad que aplica.
- Una región con asignaciones de dominio de Cloud Run (por ejemplo `us-east1`, `us-central1` o `europe-west1`; no todas las tienen).

## Desplegar

```sh
cp terraform.tfvars.example terraform.tfvars   # y completar
export TF_VAR_database_password='<16 caracteres o más, sin ; ni comillas>'
export TF_VAR_jwt_signing_key="$(openssl ecparam -name prime256v1 -genkey -noout | openssl pkcs8 -topk8 -nocrypt)"
export TF_VAR_email_password='<solo si email_username no está vacío>'
terraform init -backend-config=backend.hcl
terraform apply
```

Después, crear en el DNS los registros de `api_dns_records` y `panel_dns_records`; Cloud Run emite los certificados cuando los ve. Para cambiar una contraseña o la llave de JWT: nuevos valores en las variables y `-var secrets_version=<número mayor>`.

## Migrator y primera llave

Antes de cada versión (la primera vez, la API y el Worker no pasan la sonda de arranque hasta que el Migrator termina):

```sh
gcloud run jobs execute impifact-migrator --region us-east1 --wait
```

La primera llave de plataforma: `gcloud run jobs execute impifact-migrator --region us-east1 --wait --args=create-platform-key,admin`; se lee una sola vez en el registro de la ejecución.

## Validar sin conexión

Sin credenciales de Google y sin `plan` ni `apply` (con la imagen oficial `hashicorp/terraform` si no está instalado):

```sh
terraform init -backend=false
terraform validate
terraform fmt -check -recursive
```

## Pendiente al desplegar de verdad

- Tamaños: la API y el Worker con 3 vCPU (1 por contenedor) y 2,5 GiB por instancia; Cloud SQL `db-g1-small` (`db-custom-2-7680` en SQL Server). Ajustar con las pruebas de carga.
- SQL Server: el Migrator necesita `db_owner` (activa RCSI). Los usuarios que crea Cloud SQL tienen el rol `CustomerDbRootRole`; comprobar en el primer despliegue que el Migrator pueda cambiar la base, o darle `db_owner` con el usuario `sqlserver`.
- La rotación de Cloud KMS crea versiones nuevas y deja las anteriores habilitadas; no deshabilitar ni destruir versiones viejas, porque envolvieron llaves de Data Protection que siguen en uso.
