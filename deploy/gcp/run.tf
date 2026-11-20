# Cloud Run: api and web services (public, TLS terminated by Cloud Run on the mapped domains), the worker service
# (internal ingress, CPU always allocated) and the migrator job that runs before each release (README). Gotenberg and
# the OpenTelemetry Collector are sidecars of the api and the worker.

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
    ASPNETCORE_ENVIRONMENT          = "Production"
    DOTNET_ENVIRONMENT              = "Production"
    Database__Provider              = var.database_engine
    Storage__Provider               = "Gcs"
    Storage__Gcs__Bucket            = google_storage_bucket.documents.name
    Secrets__Provider               = "GcpSecrets"
    Secrets__GcpSecrets__ProjectId  = var.project_id
    Secrets__GcpSecrets__KmsKeyName = google_kms_crypto_key.data_protection.id
    Pdf__GotenbergUrl               = "http://127.0.0.1:3000"
    OTEL_EXPORTER_OTLP_ENDPOINT     = "http://127.0.0.1:4317"
  }

  # Cloud Run's front end appends the client IP to X-Forwarded-For; trust it so the login rate limit sees the real
  # client IP.
  forwarded_networks = {
    for index, cidr in var.forwarded_known_networks : "ForwardedHeaders__KnownNetworks__${index}" => cidr
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
    Database__ConnectionString = google_secret_manager_secret.database_connection_string.secret_id
    Jwt__SigningKey            = google_secret_manager_secret.jwt_signing_key.secret_id
  }

  worker_secrets = merge(
    { Database__ConnectionString = google_secret_manager_secret.database_connection_string.secret_id },
    local.use_email_credentials ? { Email__Password = google_secret_manager_secret.email_password[0].secret_id } : {},
  )

  collector_config = file("${path.module}/otel-collector.yaml")
}

# ─── api ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

resource "google_cloud_run_v2_service" "api" {
  name     = "${var.name_prefix}-api"
  location = var.region
  ingress  = "INGRESS_TRAFFIC_ALL"

  template {
    service_account       = google_service_account.runtime.email
    execution_environment = "EXECUTION_ENVIRONMENT_GEN2"

    scaling {
      min_instance_count = var.api_min_instances
      max_instance_count = var.api_max_instances
    }

    vpc_access {
      egress = "PRIVATE_RANGES_ONLY"

      network_interfaces {
        network    = google_compute_network.main.id
        subnetwork = google_compute_subnetwork.run.id
      }
    }

    containers {
      name       = "api"
      image      = local.images.api
      depends_on = ["gotenberg", "otel-collector"]

      ports {
        container_port = 8080
      }

      resources {
        # CPU always allocated: the collector exports and background work runs between requests.
        cpu_idle = false
        limits = {
          cpu    = "1"
          memory = "1Gi"
        }
      }

      dynamic "env" {
        for_each = local.api_environment
        content {
          name  = env.key
          value = env.value
        }
      }

      dynamic "env" {
        for_each = local.api_secrets
        content {
          name = env.key
          value_source {
            secret_key_ref {
              secret  = env.value
              version = "latest"
            }
          }
        }
      }

      # A new revision takes traffic only once the database, storage and secrets answer.
      startup_probe {
        initial_delay_seconds = 5
        period_seconds        = 10
        timeout_seconds       = 10
        failure_threshold     = 18

        http_get {
          path = "/health/ready"
          port = 8080
        }
      }

      liveness_probe {
        period_seconds    = 15
        timeout_seconds   = 5
        failure_threshold = 3

        http_get {
          path = "/health/live"
          port = 8080
        }
      }
    }

    containers {
      name  = "gotenberg"
      image = var.gotenberg_image

      resources {
        cpu_idle = false
        limits = {
          cpu    = "1"
          memory = "1Gi"
        }
      }

      startup_probe {
        period_seconds    = 5
        failure_threshold = 24

        http_get {
          path = "/health"
          port = 3000
        }
      }
    }

    containers {
      name  = "otel-collector"
      image = var.otel_collector_image
      args  = ["--config=env:OTELCOL_CONFIG"]

      env {
        name  = "OTELCOL_CONFIG"
        value = local.collector_config
      }

      resources {
        cpu_idle = false
        limits = {
          cpu    = "1"
          memory = "512Mi"
        }
      }
    }
  }

  depends_on = [
    google_secret_manager_secret_version.database_connection_string,
    google_secret_manager_secret_version.jwt_signing_key,
    google_project_iam_member.runtime_secret_manager,
  ]
}

resource "google_cloud_run_v2_service_iam_member" "api_public" {
  name     = google_cloud_run_v2_service.api.name
  location = var.region
  role     = "roles/run.invoker"
  member   = "allUsers"
}

# ─── worker ──────────────────────────────────────────────────────────────────────────────────────────────────────────

resource "google_cloud_run_v2_service" "worker" {
  name     = "${var.name_prefix}-worker"
  location = var.region
  # Nobody calls the worker: internal ingress and no invoker. Cloud Run only needs its health port.
  ingress = "INGRESS_TRAFFIC_INTERNAL_ONLY"

  template {
    service_account       = google_service_account.runtime.email
    execution_environment = "EXECUTION_ENVIRONMENT_GEN2"

    scaling {
      min_instance_count = var.worker_instances
      max_instance_count = var.worker_instances
    }

    vpc_access {
      egress = "PRIVATE_RANGES_ONLY"

      network_interfaces {
        network    = google_compute_network.main.id
        subnetwork = google_compute_subnetwork.run.id
      }
    }

    containers {
      name       = "worker"
      image      = local.images.worker
      depends_on = ["gotenberg", "otel-collector"]

      ports {
        container_port = 8091
      }

      resources {
        # The outbox and the daily jobs run without requests: CPU always allocated.
        cpu_idle = false
        limits = {
          cpu    = "1"
          memory = "1Gi"
        }
      }

      dynamic "env" {
        for_each = local.worker_environment
        content {
          name  = env.key
          value = env.value
        }
      }

      dynamic "env" {
        for_each = local.worker_secrets
        content {
          name = env.key
          value_source {
            secret_key_ref {
              secret  = env.value
              version = "latest"
            }
          }
        }
      }

      startup_probe {
        initial_delay_seconds = 5
        period_seconds        = 10
        timeout_seconds       = 10
        failure_threshold     = 18

        http_get {
          path = "/health/ready"
          port = 8091
        }
      }

      liveness_probe {
        period_seconds    = 15
        timeout_seconds   = 5
        failure_threshold = 3

        http_get {
          path = "/health/live"
          port = 8091
        }
      }
    }

    containers {
      name  = "gotenberg"
      image = var.gotenberg_image

      resources {
        cpu_idle = false
        limits = {
          cpu    = "1"
          memory = "1Gi"
        }
      }

      startup_probe {
        period_seconds    = 5
        failure_threshold = 24

        http_get {
          path = "/health"
          port = 3000
        }
      }
    }

    containers {
      name  = "otel-collector"
      image = var.otel_collector_image
      args  = ["--config=env:OTELCOL_CONFIG"]

      env {
        name  = "OTELCOL_CONFIG"
        value = local.collector_config
      }

      resources {
        cpu_idle = false
        limits = {
          cpu    = "1"
          memory = "512Mi"
        }
      }
    }
  }

  depends_on = [
    google_secret_manager_secret_version.database_connection_string,
    google_secret_manager_secret_version.email_password,
    google_project_iam_member.runtime_secret_manager,
  ]
}

# ─── web (panel) ─────────────────────────────────────────────────────────────────────────────────────────────────────

resource "google_cloud_run_v2_service" "web" {
  name     = "${var.name_prefix}-web"
  location = var.region
  ingress  = "INGRESS_TRAFFIC_ALL"

  template {
    service_account = google_service_account.web.email

    scaling {
      min_instance_count = 1
      max_instance_count = 2
    }

    containers {
      name  = "web"
      image = local.images.web

      ports {
        container_port = 8080
      }

      resources {
        limits = {
          cpu    = "1"
          memory = "512Mi"
        }
      }

      # The browser calls the API directly (CORS with credentials; Arquitectura detallada, section 14).
      env {
        name  = "IMPIFACT_API_BASE_URL"
        value = "https://${var.api_host_name}"
      }

      startup_probe {
        period_seconds    = 5
        failure_threshold = 12

        http_get {
          path = "/health/live"
          port = 8080
        }
      }

      liveness_probe {
        period_seconds    = 15
        timeout_seconds   = 5
        failure_threshold = 3

        http_get {
          path = "/health/live"
          port = 8080
        }
      }
    }
  }
}

resource "google_cloud_run_v2_service_iam_member" "web_public" {
  name     = google_cloud_run_v2_service.web.name
  location = var.region
  role     = "roles/run.invoker"
  member   = "allUsers"
}

# ─── Custom domains (Cloud Run issues and renews the certificates) ───────────────────────────────────────────────────

resource "google_cloud_run_domain_mapping" "api" {
  name     = var.api_host_name
  location = var.region

  metadata {
    namespace = var.project_id
  }

  spec {
    route_name = google_cloud_run_v2_service.api.name
  }
}

resource "google_cloud_run_domain_mapping" "panel" {
  name     = var.panel_host_name
  location = var.region

  metadata {
    namespace = var.project_id
  }

  spec {
    route_name = google_cloud_run_v2_service.web.name
  }
}

# ─── migrator (pre-deploy job) ───────────────────────────────────────────────────────────────────────────────────────

resource "google_cloud_run_v2_job" "migrator" {
  name     = "${var.name_prefix}-migrator"
  location = var.region

  template {
    task_count  = 1
    parallelism = 1

    template {
      service_account = google_service_account.migrator.email
      max_retries     = 0
      timeout         = "1800s"

      vpc_access {
        egress = "PRIVATE_RANGES_ONLY"

        network_interfaces {
          network    = google_compute_network.main.id
          subnetwork = google_compute_subnetwork.run.id
        }
      }

      containers {
        image = local.images.migrator

        resources {
          limits = {
            cpu    = "1"
            memory = "1Gi"
          }
        }

        env {
          name  = "DOTNET_ENVIRONMENT"
          value = "Production"
        }

        env {
          name  = "Database__Provider"
          value = var.database_engine
        }

        env {
          name = "Database__ConnectionString"
          value_source {
            secret_key_ref {
              secret  = google_secret_manager_secret.database_connection_string.secret_id
              version = "latest"
            }
          }
        }
      }
    }
  }

  depends_on = [
    google_secret_manager_secret_version.database_connection_string,
    google_secret_manager_secret_iam_member.migrator_database,
  ]
}
