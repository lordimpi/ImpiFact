# Cloud SQL with a private IP only. The engine follows Database:Provider; passwords are write-only (never in the state).

locals {
  database_name = "impifact"

  database_version = coalesce(var.database_version, {
    Postgres  = "POSTGRES_17"
    MySql     = "MYSQL_8_4"
    SqlServer = "SQLSERVER_2022_EXPRESS"
  }[var.database_engine])

  database_tier = coalesce(var.database_tier, var.database_engine == "SqlServer" ? "db-custom-2-7680" : "db-g1-small")
}

resource "google_sql_database_instance" "main" {
  name             = "${var.name_prefix}-db"
  region           = var.region
  database_version = local.database_version

  # SQL Server requires a password for the built-in sqlserver user.
  root_password_wo         = var.database_engine == "SqlServer" ? var.database_password : null
  root_password_wo_version = var.database_engine == "SqlServer" ? var.secrets_version : null

  deletion_protection = true

  settings {
    tier              = local.database_tier
    edition           = "ENTERPRISE"
    availability_type = var.database_high_availability ? "REGIONAL" : "ZONAL"
    disk_type         = "PD_SSD"
    disk_size         = 20
    disk_autoresize   = true

    backup_configuration {
      enabled                        = true
      point_in_time_recovery_enabled = var.database_engine == "Postgres"
      binary_log_enabled             = var.database_engine == "MySql"
    }

    ip_configuration {
      ipv4_enabled    = false
      private_network = google_compute_network.main.id
      ssl_mode        = "ENCRYPTED_ONLY"
    }
  }

  depends_on = [google_service_networking_connection.private_services]
}

resource "google_sql_database" "main" {
  name     = local.database_name
  instance = google_sql_database_instance.main.name
}

resource "google_sql_user" "app" {
  name     = var.database_username
  instance = google_sql_database_instance.main.name

  password_wo         = var.database_password
  password_wo_version = var.secrets_version
}

# Same formats as deploy/docker-compose.yml, with TLS. The Cloud SQL certificate authority is not in the image trust
# store, so the connections encrypt without validating the server certificate.
locals {
  database_host = google_sql_database_instance.main.private_ip_address

  database_connection_string = {
    Postgres  = "Host=${local.database_host};Port=5432;Database=${local.database_name};Username=${var.database_username};Password=${var.database_password};SSL Mode=Require;GSS Encryption Mode=Disable"
    MySql     = "Server=${local.database_host};Port=3306;Database=${local.database_name};User ID=${var.database_username};Password=${var.database_password};SslMode=Required"
    SqlServer = "Server=${local.database_host},1433;Database=${local.database_name};User ID=${var.database_username};Password=${var.database_password};Encrypt=True;TrustServerCertificate=True"
  }[var.database_engine]
}
