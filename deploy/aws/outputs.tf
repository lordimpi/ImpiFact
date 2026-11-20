output "load_balancer_dns_name" {
  description = "CNAME (or Route 53 alias) target of api_host_name and panel_host_name."
  value       = aws_lb.main.dns_name
}

output "cluster_name" {
  description = "ECS cluster."
  value       = aws_ecs_cluster.main.name
}

output "migrator_task_definition" {
  description = "Task definition to run before each release (aws ecs run-task)."
  value       = aws_ecs_task_definition.migrator.arn
}

output "migrator_network_configuration" {
  description = "awsvpcConfiguration for aws ecs run-task --network-configuration."
  value = {
    awsvpcConfiguration = {
      subnets        = aws_subnet.private[*].id
      securityGroups = [aws_security_group.tasks.id]
      assignPublicIp = "DISABLED"
    }
  }
}

output "documents_bucket" {
  description = "Storage:S3:Bucket."
  value       = aws_s3_bucket.documents.bucket
}

output "data_protection_key_arn" {
  description = "Secrets:AwsSecrets:KmsKeyId (the key ARN, not the alias)."
  value       = aws_kms_key.data_protection.arn
}
