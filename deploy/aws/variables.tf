variable "region" {
  description = "AWS region for every resource."
  type        = string
  default     = "us-east-1"
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
  description = "Registry that holds the four images, for example 123456789012.dkr.ecr.us-east-1.amazonaws.com/impifact."
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
  description = "OpenTelemetry Collector image, run as a sidecar of the api and the worker (OTLP to X-Ray and CloudWatch)."
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

variable "database_engine_version" {
  description = "RDS engine version; null uses the RDS default for the engine."
  type        = string
  default     = null
}

variable "database_sqlserver_edition" {
  description = "RDS SQL Server edition when database_engine is SqlServer: sqlserver-ex, sqlserver-web, sqlserver-se or sqlserver-ee."
  type        = string
  default     = "sqlserver-ex"
}

variable "database_instance_class" {
  description = "RDS instance class; null picks db.t4g.micro (PostgreSQL, MySQL) or db.t3.small (SQL Server)."
  type        = string
  default     = null
}

variable "database_allocated_storage" {
  description = "Allocated storage in GiB."
  type        = number
  default     = 20
}

variable "database_multi_az" {
  description = "Standby replica in a second availability zone."
  type        = bool
  default     = false
}

variable "database_username" {
  description = "Master user name; the application connects with it."
  type        = string
  default     = "impifact"
}

variable "database_password" {
  description = "Master password: 16 characters or more, without ';' nor quotes (it goes inside the connection string). Pass it as TF_VAR_database_password; it never reaches the state."
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

variable "certificate_arn" {
  description = "ACM certificate in this region covering api_host_name and panel_host_name; TLS terminates at the load balancer."
  type        = string
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

variable "vpc_cidr" {
  description = "Address space of the VPC."
  type        = string
  default     = "10.40.0.0/16"
}

variable "api_desired_count" {
  description = "Tasks of the api. Keep 0 on the very first apply, run the migrator, then raise it (README)."
  type        = number
  default     = 2
}

variable "worker_desired_count" {
  description = "Tasks of the worker (the outbox and the daily jobs are safe with several)."
  type        = number
  default     = 1
}

variable "web_desired_count" {
  description = "Tasks of the panel."
  type        = number
  default     = 2
}

variable "log_retention_days" {
  description = "Retention of the CloudWatch log groups."
  type        = number
  default     = 30
}
