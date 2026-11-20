# Workload identity: ECS task roles, no static keys. The application uses the standard AWS credential chain, which
# picks the task role up from the ECS container credentials endpoint.

data "aws_partition" "current" {}

data "aws_iam_policy_document" "ecs_tasks_assume" {
  statement {
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["ecs-tasks.amazonaws.com"]
    }

    condition {
      test     = "ArnLike"
      variable = "aws:SourceArn"
      values   = ["arn:${data.aws_partition.current.partition}:ecs:${var.region}:${data.aws_caller_identity.current.account_id}:*"]
    }
  }
}

# ─── Execution role: pulls the images, writes the container logs and reads the configuration secrets ─────────────────

resource "aws_iam_role" "execution" {
  name               = "${var.name_prefix}-ecs-execution"
  assume_role_policy = data.aws_iam_policy_document.ecs_tasks_assume.json
}

resource "aws_iam_role_policy_attachment" "execution_managed" {
  role       = aws_iam_role.execution.name
  policy_arn = "arn:${data.aws_partition.current.partition}:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
}

data "aws_iam_policy_document" "execution_secrets" {
  statement {
    sid     = "ReadConfigurationSecrets"
    actions = ["secretsmanager:GetSecretValue"]
    resources = concat(
      [
        aws_secretsmanager_secret.database_connection_string.arn,
        aws_secretsmanager_secret.jwt_signing_key.arn,
      ],
      aws_secretsmanager_secret.email_password[*].arn,
    )
  }
}

resource "aws_iam_role_policy" "execution_secrets" {
  name   = "configuration-secrets"
  role   = aws_iam_role.execution.id
  policy = data.aws_iam_policy_document.execution_secrets.json
}

# ─── Task role of the api and the worker ─────────────────────────────────────────────────────────────────────────────

resource "aws_iam_role" "workload" {
  name               = "${var.name_prefix}-workload"
  assume_role_policy = data.aws_iam_policy_document.ecs_tasks_assume.json
}

# The minimum of docs/Arquitectura detallada.md, section 11, plus s3:ListBucket so that /health/ready reports the
# bucket as healthy (without it the storage check is «degraded»), and what the collector sidecar needs.
data "aws_iam_policy_document" "workload" {
  statement {
    sid       = "DocumentsObjects"
    actions   = ["s3:PutObject", "s3:GetObject", "s3:DeleteObject"]
    resources = ["${aws_s3_bucket.documents.arn}/*"]
  }

  statement {
    sid       = "DocumentsBucket"
    actions   = ["s3:ListBucket"]
    resources = [aws_s3_bucket.documents.arn]
  }

  statement {
    sid = "CertificateSecrets"
    actions = [
      "secretsmanager:CreateSecret",
      "secretsmanager:GetSecretValue",
      "secretsmanager:DeleteSecret",
      "secretsmanager:TagResource",
    ]
    resources = ["arn:${data.aws_partition.current.partition}:secretsmanager:${var.region}:${data.aws_caller_identity.current.account_id}:secret:impifact/certificates/*"]
  }

  statement {
    sid       = "DataProtectionKey"
    actions   = ["kms:Encrypt", "kms:Decrypt", "kms:GenerateDataKey", "kms:DescribeKey"]
    resources = [aws_kms_key.data_protection.arn]
  }

  statement {
    sid       = "Traces"
    actions   = ["xray:PutTraceSegments", "xray:PutTelemetryRecords"]
    resources = ["*"]
  }

  statement {
    sid       = "TelemetryLogs"
    actions   = ["logs:CreateLogStream", "logs:PutLogEvents", "logs:DescribeLogStreams"]
    resources = ["${aws_cloudwatch_log_group.otlp.arn}:*"]
  }
}

resource "aws_iam_role_policy" "workload" {
  name   = "impifact-workload"
  role   = aws_iam_role.workload.id
  policy = data.aws_iam_policy_document.workload.json
}

# The migrator only needs the database; the web serves static files. Neither calls AWS APIs, so they get no task role.
