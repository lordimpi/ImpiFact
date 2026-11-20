output "api_dns_records" {
  description = "DNS records to create for api_host_name."
  value       = google_cloud_run_domain_mapping.api.status[0].resource_records
}

output "panel_dns_records" {
  description = "DNS records to create for panel_host_name."
  value       = google_cloud_run_domain_mapping.panel.status[0].resource_records
}

output "migrator_job" {
  description = "Job to execute before each release (gcloud run jobs execute)."
  value       = google_cloud_run_v2_job.migrator.name
}

output "documents_bucket" {
  description = "Storage:Gcs:Bucket."
  value       = google_storage_bucket.documents.name
}

output "data_protection_key_name" {
  description = "Secrets:GcpSecrets:KmsKeyName."
  value       = google_kms_crypto_key.data_protection.id
}
