# ─── Cloud Storage ───────────────────────────────────────────────────────────────────────────────────────────────────

# Storage:Gcs:Bucket. The application does not create it; Cloud Storage always encrypts at rest.
resource "google_storage_bucket" "documents" {
  name                        = "${var.name_prefix}-documents-${var.project_id}"
  location                    = var.region
  storage_class               = "STANDARD"
  uniform_bucket_level_access = true
  public_access_prevention    = "enforced"

  soft_delete_policy {
    retention_duration_seconds = 604800
  }

  depends_on = [google_project_service.apis]
}

# ─── Cloud KMS ───────────────────────────────────────────────────────────────────────────────────────────────────────

resource "google_kms_key_ring" "main" {
  name     = var.name_prefix
  location = var.region

  depends_on = [google_project_service.apis]
}

# Secrets:GcpSecrets:KmsKeyName: symmetric key that wraps the Data Protection keys. Automatic rotation every 90 days:
# Cloud KMS keeps the older versions enabled and picks the right one when it decrypts.
resource "google_kms_crypto_key" "data_protection" {
  name            = "data-protection"
  key_ring        = google_kms_key_ring.main.id
  purpose         = "ENCRYPT_DECRYPT"
  rotation_period = "7776000s"

  lifecycle {
    prevent_destroy = true
  }
}

# ─── Secret Manager (configuration secrets) ──────────────────────────────────────────────────────────────────────────

# Read by Cloud Run at instance start. The values are write-only: they never reach the state. The certificates the
# application stores are separate secrets named impifact-cert-*, not managed here.
resource "google_secret_manager_secret" "database_connection_string" {
  secret_id = "${var.name_prefix}-app-database-connection-string"

  replication {
    auto {}
  }

  depends_on = [google_project_service.apis]
}

resource "google_secret_manager_secret_version" "database_connection_string" {
  secret                 = google_secret_manager_secret.database_connection_string.id
  secret_data_wo         = local.database_connection_string
  secret_data_wo_version = var.secrets_version
}

resource "google_secret_manager_secret" "jwt_signing_key" {
  secret_id = "${var.name_prefix}-app-jwt-signing-key"

  replication {
    auto {}
  }

  depends_on = [google_project_service.apis]
}

resource "google_secret_manager_secret_version" "jwt_signing_key" {
  secret                 = google_secret_manager_secret.jwt_signing_key.id
  secret_data_wo         = var.jwt_signing_key
  secret_data_wo_version = var.secrets_version
}

resource "google_secret_manager_secret" "email_password" {
  count = local.use_email_credentials ? 1 : 0

  secret_id = "${var.name_prefix}-app-email-password"

  replication {
    auto {}
  }

  depends_on = [google_project_service.apis]
}

resource "google_secret_manager_secret_version" "email_password" {
  count = local.use_email_credentials ? 1 : 0

  secret                 = google_secret_manager_secret.email_password[0].id
  secret_data_wo         = var.email_password
  secret_data_wo_version = var.secrets_version
}
