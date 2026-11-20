terraform {
  # 1.11 or later: ephemeral variables and write-only arguments keep the secret values out of the state.
  required_version = ">= 1.11"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.70"
    }
  }

  # Remote state in S3, configured at init time: terraform init -backend-config=backend.hcl (README).
  backend "s3" {}
}

provider "aws" {
  region = var.region

  default_tags {
    tags = {
      application = "impifact"
    }
  }
}
