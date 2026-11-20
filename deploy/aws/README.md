# Despliegue en AWS (Terraform)

Plantilla de referencia para correr ImpiFact en AWS con el mapa de servicios de la sección 14 de `docs/Arquitectura detallada.md`. **No se ha desplegado**: solo se valida sin conexión (decisión de la fase 6: el despliegue real se hace al elegir la nube).

Se eligió Terraform (proveedor `hashicorp/aws`) y no CloudFormation porque es la misma herramienta de la plantilla de GCP, y porque desde Terraform 1.11 las variables efímeras y los argumentos de solo escritura (`password_wo`, `secret_string_wo`) mandan los secretos a RDS y a Secrets Manager sin guardarlos en el estado.

## Qué crea

| Necesidad | Recurso |
| --- | --- |
| Cómputo | ECS en Fargate: servicios `impifact-api`, `impifact-worker` e `impifact-web` en subredes privadas. Gotenberg y el OpenTelemetry Collector van como contenedores auxiliares de la API y del Worker |
| Migrator | Definición de tarea `impifact-migrator`, que se corre con `aws ecs run-task` antes de cada versión |
| Entrada | Application Load Balancer público: HTTPS con el certificado de ACM (`certificate_arn`), HTTP redirigido a HTTPS y reglas por nombre de host hacia la API y el panel |
| Base de datos | RDS según `database_engine`: PostgreSQL (por omisión), MySQL o SQL Server, cifrado, sin acceso público y con protección contra borrado |
| Blobs | Bucket S3 privado, con SSE-S3 por defecto y solo HTTPS |
| Secretos | Llave de KMS con rotación automática (anual) y su alias; secretos de configuración en Secrets Manager |
| Observabilidad | CloudWatch Logs (consola de cada contenedor), X-Ray (trazas) y métricas de CloudWatch en formato EMF (espacio `ImpiFact`), a través del Collector |
| Identidad | Rol de tarea `impifact-workload` para la API y el Worker; rol de ejecución para bajar imágenes y leer los secretos de configuración |
| Red | VPC con dos subredes públicas y dos privadas, una NAT Gateway para la salida (DIAN, SMTP, APIs de AWS) |

Permisos del rol de tarea, los de la sección 11: `s3:PutObject`, `s3:GetObject` y `s3:DeleteObject` sobre los objetos del bucket (más `s3:ListBucket`, para que `/health/ready` no marque el almacenamiento como degradado); `secretsmanager:CreateSecret`, `GetSecretValue`, `DeleteSecret` y `TagResource` sobre `impifact/certificates/*`; `kms:Encrypt`, `kms:Decrypt`, `kms:GenerateDataKey` y `kms:DescribeKey` sobre la llave; y lo que usa el Collector (`xray:PutTraceSegments`, `xray:PutTelemetryRecords` y escritura en sus dos grupos de registros). El Migrator y el panel no tienen rol de tarea: no llaman APIs de AWS.

## Configuración que fija

API y Worker: `Database__Provider`, `Database__ConnectionString` (secreto), `Storage__Provider=S3`, `Storage__S3__Bucket`, `Storage__S3__Region`, `Secrets__Provider=AwsSecrets`, `Secrets__AwsSecrets__Region`, `Secrets__AwsSecrets__KmsKeyId` (el ARN de la llave, nunca el alias), `Pdf__GotenbergUrl=http://127.0.0.1:3000` y `OTEL_EXPORTER_OTLP_ENDPOINT=http://127.0.0.1:4317`. Solo la API: `Jwt__SigningKey` (secreto), `Cors__Origins__0` y `ForwardedHeaders__KnownNetworks__0` y `__1`. Solo el Worker: `Panel__BaseUrl` y `Email__*`. El panel: `IMPIFACT_API_BASE_URL`.

`ForwardedHeaders__KnownNetworks__*` son las dos subredes públicas, donde viven los nodos del balanceador. El balanceador agrega al final de `X-Forwarded-For` la IP desde la que llegó el cliente, y la API toma esa (un salto), así que un cliente no puede falsearla con su propio encabezado.

Salud: el grupo de destino de la API revisa `/health/ready` (saca de servicio una tarea sin base, almacenamiento o secretos) y el del panel `/health/live`; la prueba de cada contenedor usa `/health/live`, como la de su imagen.

Rotación de la llave: se usa la rotación automática de KMS, que conserva el id, el ARN y el material anterior. Nunca mover el alias a otra llave: la aplicación desenvuelve siempre con la llave configurada y todo lo envuelto antes quedaría ilegible.

## Requisitos

- Terraform 1.11 o superior y una identidad con permisos para crear estos recursos.
- Un bucket S3 para el estado (`backend.hcl`, no se versiona) con `bucket`, `key`, `region` y `use_lockfile = true`. El estado no guarda contraseñas, pero sí la topología: acceso restringido.
- Las cuatro imágenes en un registro (`image_registry` e `image_tag`), con los nombres `impifact-api`, `impifact-worker`, `impifact-migrator` e `impifact-web`. Con ECR en la misma cuenta, el rol de ejecución ya puede bajarlas.
- Un certificado de ACM en la misma región para los dos nombres, y los dos nombres del mismo dominio registrable (por ejemplo `api.impifact.co` y `panel.impifact.co`): la cookie de sesión es `SameSite=Strict`.

## Desplegar

```sh
cp terraform.tfvars.example terraform.tfvars   # y completar
export TF_VAR_database_password='<16 caracteres o más, sin ; ni comillas>'
export TF_VAR_jwt_signing_key="$(openssl ecparam -name prime256v1 -genkey -noout | openssl pkcs8 -topk8 -nocrypt)"
export TF_VAR_email_password='<solo si email_username no está vacío>'
terraform init -backend-config=backend.hcl
terraform apply -var api_desired_count=0 -var worker_desired_count=0   # la primera vez
```

Después, crear en el DNS un `CNAME` (o un alias de Route 53) de los dos nombres hacia `load_balancer_dns_name`. Para cambiar una contraseña o la llave de JWT: nuevos valores en las variables y `-var secrets_version=<número mayor>`; sin ese cambio, Terraform no vuelve a mandar los valores de solo escritura.

## Migrator y primera llave

Antes de cada versión (y, la primera vez, antes de subir `api_desired_count` y `worker_desired_count`). El Migrator tiene que ser el de la versión nueva: la definición de su tarea toma la misma `image_tag` que la API y el Worker, así que primero se actualiza solo esa definición y después se corre. Si se corriera la definición actual, se aplicarían las migraciones de la versión anterior y la API y el Worker nuevos no arrancarían (revisan al arrancar que no falten migraciones):

```sh
terraform apply -target=aws_ecs_task_definition.migrator -var image_tag=<tag nuevo>
aws ecs run-task --cluster impifact --launch-type FARGATE \
  --task-definition "$(terraform output -raw migrator_task_definition)" \
  --network-configuration "$(terraform output -json migrator_network_configuration)"
aws ecs wait tasks-stopped --cluster impifact --tasks <arn de la tarea>
aws ecs describe-tasks --cluster impifact --tasks <arn de la tarea> --query 'tasks[0].containers[0].exitCode'
```

Código de salida 0: migraciones y catálogos al día; luego `terraform apply` completo con la misma `image_tag` nueva y los conteos de siempre. La primera llave de plataforma sale con el mismo comando y `--overrides '{"containerOverrides":[{"name":"migrator","command":["create-platform-key","admin"]}]}'`; se lee una sola vez en el grupo `/impifact/migrator`.

## Validar sin conexión

Sin credenciales de AWS y sin `plan` ni `apply` (con la imagen oficial `hashicorp/terraform` si no está instalado):

```sh
terraform init -backend=false
terraform validate
terraform fmt -check -recursive
```

## Pendiente al desplegar de verdad

- Tamaños: tareas de la API y del Worker con 1 vCPU y 2 GiB (512 y 1024 para la aplicación, el resto para Gotenberg y el Collector), RDS `db.t4g.micro` (`db.t3.small` en SQL Server Express). Ajustar con las pruebas de carga.
- Una sola NAT Gateway: si cae su zona, las tareas pierden la salida. Para alta disponibilidad, una por zona.
- SQL Server: el Migrator necesita `db_owner` (activa RCSI); el usuario maestro de RDS lo es de las bases que crea.
