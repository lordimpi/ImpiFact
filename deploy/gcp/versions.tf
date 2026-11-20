terraform {
  # 1.11 or later: ephemeral variables and write-only arguments keep the secret values out of the state.
  required_version = ">= 1.11"

  required_providers {
    google = {
      source  = "hashicorp/google"
      version = "~> 8.10"
    }
  }

  # Remote state in Cloud Storage, configured at init time: terraform init -backend-config=backend.hcl (README).
  backend "gcs" {}
}

provider "google" {
  project = var.project_id
  region  = var.region

  default_labels = {
    application = "impifact"
  }
}
