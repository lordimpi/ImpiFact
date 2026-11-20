variable "project_id" {
  description = "Google Cloud project (Secrets:GcpSecrets:ProjectId)."
  type        = string

  validation {
    condition     = can(regex("^[a-z][a-z0-9-]{4,28}[a-z0-9]$", var.project_id))
    error_message = "project_id must be 6 to 30 lowercase letters, digits or hyphens."
  }
}

variable "region" {
  description = "Region for every resource. Cloud Run domain mappings exist only in some regions (README)."
  type        = string
  default     = "us-east1"
}

variable "name_prefix" {
  description = "Short prefix for resource names (lowercase letters, digits and hyphens)."
  type        = string
  default     = "impifact"

  validation {
    condition     = can(regex("^[a-z][a-z0-9-]{2,15}$", var.name_prefix))
    error_message = "name_prefix must be 3 to 16 lowercase letters, digits or hyphens, starting with a letter."
  }
}

# ─── Images ──────────────────────────────────────────────────────────────────────────────────────────────────────────

variable "image_registry" {
  description = "Registry that holds the four images, for example us-east1-docker.pkg.dev/my-project/impifact."
  type        = string
}

variable "image_tag" {
  description = "Tag of the impifact-api, impifact-worker, impifact-migrator and impifact-web images."
  type        = string
}

variable "gotenberg_image" {
  description = "Gotenberg image (HTML to PDF, ADR-04), run as a sidecar of the api and the worker."
  type        = string
  default     = "gotenberg/gotenberg:8.39.0-chromium"
}

variable "otel_collector_image" {
  description = "OpenTelemetry Collector image, run as a sidecar of the api and the worker (OTLP to Cloud Trace, Managed Prometheus and Cloud Logging)."
  type        = string
  default     = "otel/opentelemetry-collector-contrib:0.165.0"
}

# ─── Database ────────────────────────────────────────────────────────────────────────────────────────────────────────

variable "database_engine" {
  description = "Database engine (Database:Provider): Postgres, MySql or SqlServer."
  type        = string
  default     = "Postgres"

  validation {
    condition     = contains(["Postgres", "MySql", "SqlServer"], var.database_engine)
    error_message = "database_engine must be Postgres, MySql or SqlServer."
  }
}

variable "database_version" {
  description = "Cloud SQL database_version; null picks POSTGRES_17, MYSQL_8_4 or SQLSERVER_2022_EXPRESS."
  type        = string
  default     = null
}

variable "database_tier" {
  description = "Cloud SQL machine tier; null picks db-g1-small (PostgreSQL, MySQL) or db-custom-2-7680 (SQL Server)."
  type        = string
  default     = null
}

variable "database_high_availability" {
  description = "Regional (high availability) instance instead of zonal."
  type        = bool
  default     = false
}

variable "database_username" {
  description = "Database user the application connects with."
  type        = string
  default     = "impifact"
}

variable "database_password" {
  description = "Password of the database user (and of sqlserver on SQL Server): 16 characters or more, without ';' nor quotes. Pass it as TF_VAR_database_password; it never reaches the state."
  type        = string
  sensitive   = true
  ephemeral   = true

  validation {
    condition     = length(var.database_password) >= 16 && !can(regex("[;'\"]", var.database_password))
    error_message = "database_password must have 16 characters or more and no ';' nor quotes."
  }
}

# ─── Application secrets (write-only) ────────────────────────────────────────────────────────────────────────────────

variable "jwt_signing_key" {
  description = "JWT signing key (ECDSA P-256 private key in PEM). Pass it as TF_VAR_jwt_signing_key; it never reaches the state."
  type        = string
  sensitive   = true
  ephemeral   = true
}

variable "email_password" {
  description = "SMTP password (Email:Password); required when email_username is set. Pass it as TF_VAR_email_password."
  type        = string
  sensitive   = true
  ephemeral   = true
  default     = null
}

variable "secrets_version" {
  description = "Increase it to write new values of database_password, jwt_signing_key and email_password (write-only arguments are only sent when this changes)."
  type        = number
  default     = 1
}

# ─── Public endpoints ────────────────────────────────────────────────────────────────────────────────────────────────

variable "api_host_name" {
  description = "Public host name of the API, for example api.impifact.co. Same registrable domain as the panel: the session cookie is SameSite=Strict."
  type        = string
}

variable "panel_host_name" {
  description = "Public host name of the panel, for example panel.impifact.co."
  type        = string
}

variable "forwarded_known_networks" {
  description = "ForwardedHeaders:KnownNetworks of the api: the range Cloud Run's front end connects from. Verify it on the first deploy (README)."
  type        = list(string)
  default     = ["169.254.0.0/16"]
}

# ─── Email (worker) ──────────────────────────────────────────────────────────────────────────────────────────────────

variable "email_host" {
  description = "SMTP server (Email:Host)."
  type        = string
}

variable "email_port" {
  description = "SMTP port (Email:Port)."
  type        = number
  default     = 587
}

variable "email_encryption" {
  description = "SMTP encryption (Email:Encryption): none, starttls, ssl or auto."
  type        = string
  default     = "starttls"

  validation {
    condition     = contains(["none", "starttls", "ssl", "auto"], var.email_encryption)
    error_message = "email_encryption must be none, starttls, ssl or auto."
  }
}

variable "email_from_address" {
  description = "Sender address (Email:FromAddress)."
  type        = string
}

variable "email_username" {
  description = "SMTP user name (Email:Username); empty when the server needs no authentication."
  type        = string
  default     = ""
}

# ─── Network and capacity ────────────────────────────────────────────────────────────────────────────────────────────

variable "subnet_cidr" {
  description = "Subnet for Cloud Run direct VPC egress (reaches the Cloud SQL private IP)."
  type        = string
  default     = "10.50.0.0/24"
}

variable "api_min_instances" {
  description = "Minimum instances of the api."
  type        = number
  default     = 1
}

variable "api_max_instances" {
  description = "Maximum instances of the api."
  type        = number
  default     = 3
}

variable "worker_instances" {
  description = "Instances of the worker (fixed, CPU always allocated; the outbox and the daily jobs are safe with several)."
  type        = number
  default     = 1
}
