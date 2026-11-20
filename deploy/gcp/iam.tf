# Workload identity: Cloud Run service accounts, no keys. The application uses Application Default Credentials, which
# Cloud Run serves from the metadata server.

resource "google_service_account" "runtime" {
  account_id   = "${var.name_prefix}-runtime"
  display_name = "ImpiFact api and worker"

  depends_on = [google_project_service.apis]
}

resource "google_service_account" "migrator" {
  account_id   = "${var.name_prefix}-migrator"
  display_name = "ImpiFact migrator"

  depends_on = [google_project_service.apis]
}

resource "google_service_account" "web" {
  account_id   = "${var.name_prefix}-web"
  display_name = "ImpiFact panel"

  depends_on = [google_project_service.apis]
}

locals {
  runtime_member = "serviceAccount:${google_service_account.runtime.email}"
}

# ─── Cloud Storage (docs/Arquitectura detallada.md, section 11) ──────────────────────────────────────────────────────

resource "google_storage_bucket_iam_member" "runtime_objects" {
  bucket = google_storage_bucket.documents.name
  role   = "roles/storage.objectUser"
  member = local.runtime_member
}

# storage.buckets.get: on a missing object the store checks whether the bucket exists; without it /health/ready gets
# a 403 and stays at 503.
resource "google_project_iam_custom_role" "bucket_metadata_reader" {
  role_id     = "${replace(var.name_prefix, "-", "_")}_bucket_metadata_reader"
  title       = "ImpiFact bucket metadata reader"
  permissions = ["storage.buckets.get"]
}

resource "google_storage_bucket_iam_member" "runtime_bucket_get" {
  bucket = google_storage_bucket.documents.name
  role   = google_project_iam_custom_role.bucket_metadata_reader.id
  member = local.runtime_member
}

# Signed URLs: on Cloud Run the signature is made by the IAM API (iam.serviceAccounts.signBlob on its own account).
resource "google_service_account_iam_member" "runtime_sign_blob" {
  service_account_id = google_service_account.runtime.name
  role               = "roles/iam.serviceAccountTokenCreator"
  member             = local.runtime_member
}

# ─── Secret Manager and Cloud KMS ────────────────────────────────────────────────────────────────────────────────────

# Certificates are created and deleted at run time as impifact-cert-* secrets of the project.
resource "google_project_iam_member" "runtime_secret_manager" {
  project = var.project_id
  role    = "roles/secretmanager.admin"
  member  = local.runtime_member
}

resource "google_kms_crypto_key_iam_member" "runtime_encrypter_decrypter" {
  crypto_key_id = google_kms_crypto_key.data_protection.id
  role          = "roles/cloudkms.cryptoKeyEncrypterDecrypter"
  member        = local.runtime_member
}

resource "google_kms_crypto_key_iam_member" "runtime_kms_viewer" {
  crypto_key_id = google_kms_crypto_key.data_protection.id
  role          = "roles/cloudkms.viewer"
  member        = local.runtime_member
}

# ─── Telemetry (collector sidecar) ───────────────────────────────────────────────────────────────────────────────────

resource "google_project_iam_member" "runtime_telemetry" {
  for_each = toset(["roles/cloudtrace.agent", "roles/monitoring.metricWriter", "roles/logging.logWriter"])

  project = var.project_id
  role    = each.key
  member  = local.runtime_member
}

# ─── Migrator: reads the connection string only ──────────────────────────────────────────────────────────────────────

resource "google_secret_manager_secret_iam_member" "migrator_database" {
  secret_id = google_secret_manager_secret.database_connection_string.id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.migrator.email}"
}

# The web service account has no roles: the panel calls no Google API.
