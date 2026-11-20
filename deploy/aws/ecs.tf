# ECS on Fargate: api, worker and web services, plus the migrator task definition that runs before each release
# (aws ecs run-task, README). Gotenberg and the OpenTelemetry Collector are sidecars of the api and the worker.

locals {
  use_email_credentials = var.email_username != ""

  images = {
    api      = "${var.image_registry}/impifact-api:${var.image_tag}"
    worker   = "${var.image_registry}/impifact-worker:${var.image_tag}"
    migrator = "${var.image_registry}/impifact-migrator:${var.image_tag}"
    web      = "${var.image_registry}/impifact-web:${var.image_tag}"
  }

  # Configuration shared by the api and the worker (docs/Arquitectura detallada.md, section 11).
  backend_environment = {
    ASPNETCORE_ENVIRONMENT        = "Production"
    DOTNET_ENVIRONMENT            = "Production"
    Database__Provider            = var.database_engine
    Storage__Provider             = "S3"
    Storage__S3__Bucket           = aws_s3_bucket.documents.bucket
    Storage__S3__Region           = var.region
    Secrets__Provider             = "AwsSecrets"
    Secrets__AwsSecrets__Region   = var.region
    Secrets__AwsSecrets__KmsKeyId = aws_kms_key.data_protection.arn
    Pdf__GotenbergUrl             = "http://127.0.0.1:3000"
    OTEL_EXPORTER_OTLP_ENDPOINT   = "http://127.0.0.1:4317"
  }

  # The ALB nodes live in the public subnets: trust their X-Forwarded-For so the login rate limit sees the real
  # client IP.
  forwarded_networks = {
    for index, cidr in aws_subnet.public[*].cidr_block : "ForwardedHeaders__KnownNetworks__${index}" => cidr
  }

  api_environment = merge(local.backend_environment, local.forwarded_networks, {
    Cors__Origins__0 = "https://${var.panel_host_name}"
  })

  worker_environment = merge(
    local.backend_environment,
    {
      Panel__BaseUrl     = "https://${var.panel_host_name}"
      Email__Host        = var.email_host
      Email__Port        = tostring(var.email_port)
      Email__Encryption  = var.email_encryption
      Email__FromAddress = var.email_from_address
    },
    local.use_email_credentials ? { Email__Username = var.email_username } : {},
  )

  api_secrets = {
    Database__ConnectionString = aws_secretsmanager_secret.database_connection_string.arn
    Jwt__SigningKey            = aws_secretsmanager_secret.jwt_signing_key.arn
  }

  worker_secrets = merge(
    { Database__ConnectionString = aws_secretsmanager_secret.database_connection_string.arn },
    local.use_email_credentials ? { Email__Password = aws_secretsmanager_secret.email_password[0].arn } : {},
  )

  # Same check as the HEALTHCHECK of the images: bash opens the connection with /dev/tcp (no curl in the image).
  http_health_command = {
    for name, port in { api = 8080, worker = 8091 } : name => [
      "CMD", "bash", "-c",
      "exec 3<>/dev/tcp/127.0.0.1/${port} && printf 'GET /health/live HTTP/1.1\\r\\nHost: localhost\\r\\nConnection: close\\r\\n\\r\\n' >&3 && head -n 1 <&3 | grep -q ' 200 '",
    ]
  }

  sidecars = [
    {
      name      = "gotenberg"
      image     = var.gotenberg_image
      essential = true
      cpu       = 384
      memory    = 768
      logConfiguration = {
        logDriver = "awslogs"
        options = {
          awslogs-group         = aws_cloudwatch_log_group.containers["gotenberg"].name
          awslogs-region        = var.region
          awslogs-stream-prefix = "gotenberg"
        }
      }
    },
    {
      name      = "otel-collector"
      image     = var.otel_collector_image
      essential = false
      cpu       = 128
      memory    = 256
      command   = ["--config=env:OTELCOL_CONFIG"]
      environment = [
        { name = "OTELCOL_CONFIG", value = file("${path.module}/otel-collector.yaml") },
        { name = "AWS_REGION", value = var.region },
        { name = "OTEL_METRICS_LOG_GROUP", value = aws_cloudwatch_log_group.metrics.name },
        { name = "OTEL_LOGS_LOG_GROUP", value = aws_cloudwatch_log_group.otlp.name },
      ]
      logConfiguration = {
        logDriver = "awslogs"
        options = {
          awslogs-group         = aws_cloudwatch_log_group.containers["otel-collector"].name
          awslogs-region        = var.region
          awslogs-stream-prefix = "otel-collector"
        }
      }
    },
  ]
}

# ─── Logs ────────────────────────────────────────────────────────────────────────────────────────────────────────────

# Console output of every container (one JSON line per event from the api, worker and migrator).
resource "aws_cloudwatch_log_group" "containers" {
  for_each = toset(["api", "worker", "migrator", "web", "gotenberg", "otel-collector"])

  name              = "/${var.name_prefix}/${each.key}"
  retention_in_days = var.log_retention_days
}

# Logs and metrics (embedded metric format) received by the collector over OTLP.
resource "aws_cloudwatch_log_group" "otlp" {
  name              = "/${var.name_prefix}/otlp-logs"
  retention_in_days = var.log_retention_days
}

resource "aws_cloudwatch_log_group" "metrics" {
  name              = "/${var.name_prefix}/metrics"
  retention_in_days = var.log_retention_days
}

# ─── Cluster ─────────────────────────────────────────────────────────────────────────────────────────────────────────

resource "aws_ecs_cluster" "main" {
  name = var.name_prefix

  setting {
    name  = "containerInsights"
    value = "enabled"
  }
}

# ─── Task definitions ────────────────────────────────────────────────────────────────────────────────────────────────

resource "aws_ecs_task_definition" "api" {
  family                   = "${var.name_prefix}-api"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = 1024
  memory                   = 2048
  execution_role_arn       = aws_iam_role.execution.arn
  task_role_arn            = aws_iam_role.workload.arn

  runtime_platform {
    operating_system_family = "LINUX"
    cpu_architecture        = "X86_64"
  }

  container_definitions = jsonencode(concat([
    {
      name         = "api"
      image        = local.images.api
      essential    = true
      cpu          = 512
      memory       = 1024
      portMappings = [{ containerPort = 8080, protocol = "tcp" }]
      environment  = [for name, value in local.api_environment : { name = name, value = value }]
      secrets      = [for name, arn in local.api_secrets : { name = name, valueFrom = arn }]
      healthCheck = {
        command     = local.http_health_command.api
        interval    = 15
        timeout     = 5
        retries     = 3
        startPeriod = 60
      }
      dependsOn = [{ containerName = "gotenberg", condition = "START" }]
      logConfiguration = {
        logDriver = "awslogs"
        options = {
          awslogs-group         = aws_cloudwatch_log_group.containers["api"].name
          awslogs-region        = var.region
          awslogs-stream-prefix = "api"
        }
      }
    },
  ], local.sidecars))
}

resource "aws_ecs_task_definition" "worker" {
  family                   = "${var.name_prefix}-worker"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = 1024
  memory                   = 2048
  execution_role_arn       = aws_iam_role.execution.arn
  task_role_arn            = aws_iam_role.workload.arn

  runtime_platform {
    operating_system_family = "LINUX"
    cpu_architecture        = "X86_64"
  }

  container_definitions = jsonencode(concat([
    {
      name        = "worker"
      image       = local.images.worker
      essential   = true
      cpu         = 512
      memory      = 1024
      environment = [for name, value in local.worker_environment : { name = name, value = value }]
      secrets     = [for name, arn in local.worker_secrets : { name = name, valueFrom = arn }]
      # /health/ready of the worker has no consumer on ECS (no load balancer); the container check uses /health/live.
      healthCheck = {
        command     = local.http_health_command.worker
        interval    = 15
        timeout     = 5
        retries     = 3
        startPeriod = 60
      }
      dependsOn = [{ containerName = "gotenberg", condition = "START" }]
      logConfiguration = {
        logDriver = "awslogs"
        options = {
          awslogs-group         = aws_cloudwatch_log_group.containers["worker"].name
          awslogs-region        = var.region
          awslogs-stream-prefix = "worker"
        }
      }
    },
  ], local.sidecars))
}

resource "aws_ecs_task_definition" "migrator" {
  family                   = "${var.name_prefix}-migrator"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = 512
  memory                   = 1024
  execution_role_arn       = aws_iam_role.execution.arn

  runtime_platform {
    operating_system_family = "LINUX"
    cpu_architecture        = "X86_64"
  }

  container_definitions = jsonencode([
    {
      name      = "migrator"
      image     = local.images.migrator
      essential = true
      environment = [
        { name = "DOTNET_ENVIRONMENT", value = "Production" },
        { name = "Database__Provider", value = var.database_engine },
      ]
      secrets = [
        { name = "Database__ConnectionString", valueFrom = aws_secretsmanager_secret.database_connection_string.arn },
      ]
      logConfiguration = {
        logDriver = "awslogs"
        options = {
          awslogs-group         = aws_cloudwatch_log_group.containers["migrator"].name
          awslogs-region        = var.region
          awslogs-stream-prefix = "migrator"
        }
      }
    },
  ])
}

resource "aws_ecs_task_definition" "web" {
  family                   = "${var.name_prefix}-web"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = 256
  memory                   = 512
  execution_role_arn       = aws_iam_role.execution.arn

  runtime_platform {
    operating_system_family = "LINUX"
    cpu_architecture        = "X86_64"
  }

  container_definitions = jsonencode([
    {
      name         = "web"
      image        = local.images.web
      essential    = true
      portMappings = [{ containerPort = 8080, protocol = "tcp" }]
      environment = [
        # The browser calls the API directly (CORS with credentials; Arquitectura detallada, section 14).
        { name = "IMPIFACT_API_BASE_URL", value = "https://${var.api_host_name}" },
      ]
      healthCheck = {
        command     = ["CMD", "wget", "-q", "-O", "/dev/null", "http://127.0.0.1:8080/health/live"]
        interval    = 15
        timeout     = 5
        retries     = 3
        startPeriod = 10
      }
      logConfiguration = {
        logDriver = "awslogs"
        options = {
          awslogs-group         = aws_cloudwatch_log_group.containers["web"].name
          awslogs-region        = var.region
          awslogs-stream-prefix = "web"
        }
      }
    },
  ])
}

# ─── Services ────────────────────────────────────────────────────────────────────────────────────────────────────────

resource "aws_ecs_service" "api" {
  name                              = "${var.name_prefix}-api"
  cluster                           = aws_ecs_cluster.main.id
  task_definition                   = aws_ecs_task_definition.api.arn
  desired_count                     = var.api_desired_count
  launch_type                       = "FARGATE"
  health_check_grace_period_seconds = 60

  network_configuration {
    subnets          = aws_subnet.private[*].id
    security_groups  = [aws_security_group.tasks.id]
    assign_public_ip = false
  }

  load_balancer {
    target_group_arn = aws_lb_target_group.api.arn
    container_name   = "api"
    container_port   = 8080
  }

  deployment_circuit_breaker {
    enable   = true
    rollback = true
  }

  depends_on = [aws_lb_listener_rule.api]
}

resource "aws_ecs_service" "worker" {
  name            = "${var.name_prefix}-worker"
  cluster         = aws_ecs_cluster.main.id
  task_definition = aws_ecs_task_definition.worker.arn
  desired_count   = var.worker_desired_count
  launch_type     = "FARGATE"

  network_configuration {
    subnets          = aws_subnet.private[*].id
    security_groups  = [aws_security_group.tasks.id]
    assign_public_ip = false
  }

  deployment_circuit_breaker {
    enable   = true
    rollback = true
  }
}

resource "aws_ecs_service" "web" {
  name                              = "${var.name_prefix}-web"
  cluster                           = aws_ecs_cluster.main.id
  task_definition                   = aws_ecs_task_definition.web.arn
  desired_count                     = var.web_desired_count
  launch_type                       = "FARGATE"
  health_check_grace_period_seconds = 30

  network_configuration {
    subnets          = aws_subnet.private[*].id
    security_groups  = [aws_security_group.tasks.id]
    assign_public_ip = false
  }

  load_balancer {
    target_group_arn = aws_lb_target_group.web.arn
    container_name   = "web"
    container_port   = 8080
  }

  deployment_circuit_breaker {
    enable   = true
    rollback = true
  }

  depends_on = [aws_lb_listener_rule.web]
}
