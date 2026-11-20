# RDS in the private subnets. The engine follows Database:Provider; the password is write-only (never in the state).

locals {
  database_name = "impifact"

  rds_engine = {
    Postgres  = "postgres"
    MySql     = "mysql"
    SqlServer = var.database_sqlserver_edition
  }[var.database_engine]

  database_port = {
    Postgres  = 5432
    MySql     = 3306
    SqlServer = 1433
  }[var.database_engine]

  database_instance_class = coalesce(
    var.database_instance_class,
    var.database_engine == "SqlServer" ? "db.t3.small" : "db.t4g.micro"
  )
}

resource "aws_db_subnet_group" "main" {
  name       = "${var.name_prefix}-database"
  subnet_ids = aws_subnet.private[*].id
}

resource "aws_db_instance" "main" {
  identifier     = "${var.name_prefix}-db"
  engine         = local.rds_engine
  engine_version = var.database_engine_version
  instance_class = local.database_instance_class
  license_model  = var.database_engine == "SqlServer" ? "license-included" : null

  # SQL Server on RDS takes no initial database: the Migrator creates it (EF Core migrations).
  db_name  = var.database_engine == "SqlServer" ? null : local.database_name
  username = var.database_username

  password_wo         = var.database_password
  password_wo_version = var.secrets_version

  allocated_storage     = var.database_allocated_storage
  max_allocated_storage = var.database_allocated_storage * 5
  storage_type          = "gp3"
  storage_encrypted     = true

  db_subnet_group_name   = aws_db_subnet_group.main.name
  vpc_security_group_ids = [aws_security_group.database.id]
  publicly_accessible    = false
  multi_az               = var.database_multi_az

  backup_retention_period    = 7
  auto_minor_version_upgrade = true
  deletion_protection        = true
  skip_final_snapshot        = false
  final_snapshot_identifier  = "${var.name_prefix}-db-final"
  copy_tags_to_snapshot      = true
}

# Same formats as deploy/docker-compose.yml, with TLS to RDS. The RDS certificate authority is not in the image trust
# store, so the connections encrypt without validating the server certificate.
locals {
  database_connection_string = {
    Postgres  = "Host=${aws_db_instance.main.address};Port=5432;Database=${local.database_name};Username=${var.database_username};Password=${var.database_password};SSL Mode=Require;GSS Encryption Mode=Disable"
    MySql     = "Server=${aws_db_instance.main.address};Port=3306;Database=${local.database_name};User ID=${var.database_username};Password=${var.database_password};SslMode=Required"
    SqlServer = "Server=${aws_db_instance.main.address},1433;Database=${local.database_name};User ID=${var.database_username};Password=${var.database_password};Encrypt=True;TrustServerCertificate=True"
  }[var.database_engine]
}
