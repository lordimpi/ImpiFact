# Secrets:AwsSecrets:KmsKeyId: symmetric KMS key that encrypts the certificate secrets and wraps the Data Protection
# keys. Automatic rotation keeps the key id and ARN and the previous key material, so everything wrapped before stays
# readable. Never move the alias to another key: the application always unwraps with the configured key.
resource "aws_kms_key" "data_protection" {
  description             = "ImpiFact Data Protection and certificate secrets"
  enable_key_rotation     = true
  rotation_period_in_days = 365
  deletion_window_in_days = 30
}

resource "aws_kms_alias" "data_protection" {
  name          = "alias/${var.name_prefix}-data-protection"
  target_key_id = aws_kms_key.data_protection.key_id
}

# Configuration secrets read by ECS at task start (execution role). The values are write-only: they never reach the
# state. The certificates the application stores live under impifact/certificates/ and are not managed here.
resource "aws_secretsmanager_secret" "database_connection_string" {
  name                    = "${var.name_prefix}/app/database-connection-string"
  recovery_window_in_days = 7
}

resource "aws_secretsmanager_secret_version" "database_connection_string" {
  secret_id                = aws_secretsmanager_secret.database_connection_string.id
  secret_string_wo         = local.database_connection_string
  secret_string_wo_version = var.secrets_version
}

resource "aws_secretsmanager_secret" "jwt_signing_key" {
  name                    = "${var.name_prefix}/app/jwt-signing-key"
  recovery_window_in_days = 7
}

resource "aws_secretsmanager_secret_version" "jwt_signing_key" {
  secret_id                = aws_secretsmanager_secret.jwt_signing_key.id
  secret_string_wo         = var.jwt_signing_key
  secret_string_wo_version = var.secrets_version
}

resource "aws_secretsmanager_secret" "email_password" {
  count = local.use_email_credentials ? 1 : 0

  name                    = "${var.name_prefix}/app/email-password"
  recovery_window_in_days = 7
}

resource "aws_secretsmanager_secret_version" "email_password" {
  count = local.use_email_credentials ? 1 : 0

  secret_id                = aws_secretsmanager_secret.email_password[0].id
  secret_string_wo         = var.email_password
  secret_string_wo_version = var.secrets_version
}
