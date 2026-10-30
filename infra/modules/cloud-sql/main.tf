# PostgreSQL 17 on Cloud SQL: the host database "OpenTms" plus room for the tenant databases that
# TenantProvisioningJob creates at runtime (those are application data, not Terraform's).
#
# Reached only through the Cloud SQL connector (Unix socket under /cloudsql/): a public IP with no
# authorised networks and connector enforcement, so no VPC, connector or NAT is needed.

terraform {
  required_providers {
    google = { source = "hashicorp/google" }
    random = { source = "hashicorp/random" }
  }
}

resource "google_sql_database_instance" "this" {
  name                = var.name
  database_version    = "POSTGRES_17"
  region              = var.region
  deletion_protection = var.deletion_protection

  settings {
    edition               = "ENTERPRISE"
    tier                  = var.tier
    availability_type     = "ZONAL"
    disk_type             = "PD_SSD"
    disk_size             = 10
    disk_autoresize       = true
    connector_enforcement = "REQUIRED"
    user_labels           = var.labels

    ip_configuration {
      ipv4_enabled = true
      ssl_mode     = "ENCRYPTED_ONLY"
    }

    backup_configuration {
      enabled                        = var.backups
      point_in_time_recovery_enabled = var.backups
      start_time                     = "02:00"
      transaction_log_retention_days = var.backups ? 7 : null

      backup_retention_settings {
        retained_backups = 7
      }
    }

    maintenance_window {
      day          = 7 # Sunday
      hour         = 3
      update_track = "stable"
    }

    dynamic "database_flags" {
      for_each = var.max_connections == null ? [] : [var.max_connections]
      content {
        name  = "max_connections"
        value = tostring(database_flags.value)
      }
    }

    insights_config {
      query_insights_enabled = true
    }
  }

  lifecycle {
    # A stoppable database is started and stopped by workflows (scripts/staging-database.sh);
    # Terraform must not undo that on every apply.
    ignore_changes = [settings[0].activation_policy]
  }
}

resource "google_sql_database" "host" {
  name     = "OpenTms"
  instance = google_sql_database_instance.this.name
}

resource "random_password" "app" {
  length  = 32
  special = false
}

resource "random_password" "provisioner" {
  length  = 32
  special = false
}

# Every request runs as this login.
resource "google_sql_user" "app" {
  name     = "opentms_app"
  instance = google_sql_database_instance.this.name
  password = random_password.app.result
}

# Tenancy:Provisioning:ConnectionString — creates tenant databases and their roles.
resource "google_sql_user" "provisioner" {
  name     = "opentms_provisioner"
  instance = google_sql_database_instance.this.name
  password = random_password.provisioner.result
}

locals {
  socket = "/cloudsql/${google_sql_database_instance.this.connection_name}"
}
