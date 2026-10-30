# One OpenTMS environment (docs/designs/gcp-infrastructure.md). The same code for every environment;
# the differences live in ../environments/<name>.tfvars, the state in prefix opentms/env/<name>:
#
#   terraform init -backend-config="prefix=opentms/env/staging"
#   terraform apply -var-file=../environments/staging.tfvars
#
# The project itself, its APIs and the CI identities' rights come from ../bootstrap.

terraform {
  required_version = ">= 1.9"

  required_providers {
    google = {
      source  = "hashicorp/google"
      version = "~> 7.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
    tls = {
      source  = "hashicorp/tls"
      version = "~> 4.0"
    }
  }

  backend "gcs" {
    bucket = "ryd-platform-tfstate"
    # prefix comes from -backend-config, one per environment
  }
}

provider "google" {
  project = var.project_id
  region  = var.region

  default_labels = {
    app         = "opentms"
    environment = var.environment
  }
}

data "google_project" "this" {}

locals {
  # Cloud Run's deterministic addresses; computed instead of read from the services so the API and the
  # SPA can each be told the other's address without a dependency cycle (G5: no domain yet).
  api_url = "https://opentms-api-${data.google_project.this.number}.${var.region}.run.app"
  web_url = "https://opentms-web-${data.google_project.this.number}.${var.region}.run.app"
}

# --- Runtime identities ------------------------------------------------------------------------

resource "google_service_account" "api" {
  account_id   = "opentms-api"
  display_name = "OpenTMS API (Cloud Run service)"
}

resource "google_service_account" "migrator" {
  account_id   = "opentms-migrator"
  display_name = "OpenTMS DbMigrator (Cloud Run job)"
}

resource "google_service_account" "web" {
  account_id   = "opentms-web"
  display_name = "OpenTMS SPA (Cloud Run service); no rights"
}

resource "google_project_iam_member" "cloudsql_client" {
  for_each = {
    api      = google_service_account.api.member
    migrator = google_service_account.migrator.member
  }
  project = var.project_id
  role    = "roles/cloudsql.client"
  member  = each.value
}

# The deploy workflow deploys revisions that run as these accounts.
resource "google_service_account_iam_member" "deployer_acts_as" {
  for_each = {
    api      = google_service_account.api.name
    migrator = google_service_account.migrator.name
    web      = google_service_account.web.name
  }
  service_account_id = each.value
  role               = "roles/iam.serviceAccountUser"
  member             = "serviceAccount:${var.deployer_service_account}"
}

# --- Database --------------------------------------------------------------------------------

module "database" {
  source = "../modules/cloud-sql"

  name                = "opentms-${var.environment}"
  region              = var.region
  tier                = var.database.tier
  backups             = var.database.backups
  deletion_protection = var.database.deletion_protection
  max_connections     = var.database.max_connections
}

# --- Secrets ---------------------------------------------------------------------------------

resource "random_password" "string_encryption" {
  # Never rotated by Terraform: a new passphrase makes values encrypted with the old one unreadable.
  length  = 32
  special = false
}

resource "tls_private_key" "openiddict" {
  algorithm = "RSA"
  rsa_bits  = 2048
}

resource "tls_self_signed_cert" "openiddict" {
  private_key_pem       = tls_private_key.openiddict.private_key_pem
  validity_period_hours = 24 * 365 * 2
  allowed_uses          = ["digital_signature", "key_encipherment"]

  subject {
    common_name  = "OpenTMS ${var.environment} OpenIddict"
    organization = "OpenTMS"
  }
}

locals {
  secrets = {
    "connection-string-default" = {
      value     = module.database.app_connection_string
      accessors = ["api", "migrator"]
    }
    "connection-string-provisioning" = {
      value     = module.database.provisioning_connection_string
      accessors = ["api", "migrator"]
    }
    "openiddict-certificate-pem" = {
      value     = tls_self_signed_cert.openiddict.cert_pem
      accessors = ["api"]
    }
    "openiddict-key-pem" = {
      value     = tls_private_key.openiddict.private_key_pem
      accessors = ["api"]
    }
    "string-encryption-passphrase" = {
      value     = random_password.string_encryption.result
      accessors = ["api", "migrator"]
    }
  }

  accessor_members = {
    api      = google_service_account.api.member
    migrator = google_service_account.migrator.member
  }

  secret_accessors = merge([
    for name, secret in local.secrets : {
      for accessor in secret.accessors : "${name}/${accessor}" => { secret = name, member = local.accessor_members[accessor] }
    }
  ]...)
}

resource "google_secret_manager_secret" "this" {
  for_each  = local.secrets
  secret_id = each.key

  replication {
    user_managed {
      replicas {
        location = var.region
      }
    }
  }
}

resource "google_secret_manager_secret_version" "this" {
  for_each    = local.secrets
  secret      = google_secret_manager_secret.this[each.key].id
  secret_data = each.value.value
}

resource "google_secret_manager_secret_iam_member" "this" {
  for_each  = local.secret_accessors
  secret_id = google_secret_manager_secret.this[each.value.secret].id
  role      = "roles/secretmanager.secretAccessor"
  member    = each.value.member
}

# --- Cloud Run -------------------------------------------------------------------------------

locals {
  app_env = {
    App__SelfUrl                     = local.api_url
    App__CorsOrigins                 = local.web_url
    App__RedirectAllowedUrls         = local.web_url
    App__DisablePII                  = "true" # constitution rule 5
    AuthServer__Authority            = local.api_url
    AuthServer__RequireHttpsMetadata = "true"
    InstanceRegistry__Role           = "Api"
  }
}

module "api" {
  source = "../modules/cloud-run-service"

  name                      = "opentms-api"
  region                    = var.region
  service_account           = google_service_account.api.email
  min_instances             = var.api.min_instances
  max_instances             = var.api.max_instances
  cpu                       = "1"
  memory                    = "1Gi"
  cpu_idle                  = false
  session_affinity          = true # SignalR negotiate and connect on the same revision during a canary
  cloud_sql_connection_name = module.database.connection_name
  health_path               = "/health/live"
  deletion_protection       = var.database.deletion_protection

  env = local.app_env

  secret_env = {
    ConnectionStrings__Default              = google_secret_manager_secret.this["connection-string-default"].secret_id
    Tenancy__Provisioning__ConnectionString = google_secret_manager_secret.this["connection-string-provisioning"].secret_id
    AuthServer__SigningCertificatePem       = google_secret_manager_secret.this["openiddict-certificate-pem"].secret_id
    AuthServer__SigningKeyPem               = google_secret_manager_secret.this["openiddict-key-pem"].secret_id
    StringEncryption__DefaultPassPhrase     = google_secret_manager_secret.this["string-encryption-passphrase"].secret_id
  }

  depends_on = [google_secret_manager_secret_iam_member.this, google_secret_manager_secret_version.this]
}

module "web" {
  source = "../modules/cloud-run-service"

  name            = "opentms-web"
  region          = var.region
  service_account = google_service_account.web.email
  min_instances   = 0
  max_instances   = var.web.max_instances
  cpu             = "1"
  memory          = "256Mi"
  concurrency     = 250
}

resource "google_cloud_run_v2_job" "migrator" {
  name                = "opentms-migrator"
  location            = var.region
  deletion_protection = var.database.deletion_protection

  template {
    task_count = 1

    template {
      service_account = google_service_account.migrator.email
      max_retries     = 0
      timeout         = "1800s"

      volumes {
        name = "cloudsql"
        cloud_sql_instance {
          instances = [module.database.connection_name]
        }
      }

      containers {
        image = "us-docker.pkg.dev/cloudrun/container/job"

        resources {
          limits = {
            cpu    = "1"
            memory = "1Gi"
          }
        }

        env {
          name  = "OpenIddict__Applications__OpenTms_App__RootUrl"
          value = local.web_url
        }

        env {
          name  = "OpenIddict__Applications__OpenTms_Swagger__RootUrl"
          value = "${local.api_url}/"
        }

        dynamic "env" {
          for_each = {
            ConnectionStrings__Default              = "connection-string-default"
            Tenancy__Provisioning__ConnectionString = "connection-string-provisioning"
            StringEncryption__DefaultPassPhrase     = "string-encryption-passphrase"
            Seed__HostAdminPassword                 = "host-admin-password"
          }
          content {
            name = env.key
            value_source {
              secret_key_ref {
                secret  = env.value
                version = "latest"
              }
            }
          }
        }

        env {
          name  = "Seed__HostAdminEmail"
          value = var.host_admin_email
        }

        volume_mounts {
          name       = "cloudsql"
          mount_path = "/cloudsql"
        }
      }
    }
  }

  lifecycle {
    ignore_changes = [template[0].template[0].containers[0].image, client, client_version, labels, annotations]
  }

  depends_on = [google_secret_manager_secret_iam_member.this, google_secret_manager_secret_version.this, google_secret_manager_secret_version.host_admin]
}

# --- Host administrator ----------------------------------------------------------------------

# Seeded once by the migrator (seeding never overwrites an existing user). Read it with
#   gcloud secrets versions access latest --secret=host-admin-password --project=<project>
resource "random_password" "host_admin" {
  length      = 24
  min_upper   = 2
  min_lower   = 2
  min_numeric = 2
  min_special = 2
}

resource "google_secret_manager_secret" "host_admin" {
  secret_id = "host-admin-password"

  replication {
    user_managed {
      replicas {
        location = var.region
      }
    }
  }
}

resource "google_secret_manager_secret_version" "host_admin" {
  secret      = google_secret_manager_secret.host_admin.id
  secret_data = random_password.host_admin.result
}

resource "google_secret_manager_secret_iam_member" "host_admin" {
  secret_id = google_secret_manager_secret.host_admin.id
  role      = "roles/secretmanager.secretAccessor"
  member    = google_service_account.migrator.member
}

# --- Monitoring (prod) -------------------------------------------------------------------------

resource "google_monitoring_notification_channel" "email" {
  count        = var.monitoring.uptime_check ? 1 : 0
  display_name = "OpenTMS ${var.environment} alerts"
  type         = "email"
  labels       = { email_address = var.monitoring.alert_email }
}

resource "google_monitoring_uptime_check_config" "api" {
  count        = var.monitoring.uptime_check ? 1 : 0
  display_name = "opentms-api ${var.environment} /health/ready"
  timeout      = "10s"
  period       = "300s"

  http_check {
    path         = "/health/ready"
    port         = 443
    use_ssl      = true
    validate_ssl = true
  }

  monitored_resource {
    type = "uptime_url"
    labels = {
      project_id = var.project_id
      host       = trimprefix(local.api_url, "https://")
    }
  }
}

resource "google_monitoring_alert_policy" "api_down" {
  count        = var.monitoring.uptime_check ? 1 : 0
  display_name = "opentms-api ${var.environment} is not ready"
  combiner     = "OR"

  conditions {
    display_name = "Uptime check failing"
    condition_threshold {
      filter          = "metric.type=\"monitoring.googleapis.com/uptime_check/check_passed\" AND resource.type=\"uptime_url\" AND metric.label.check_id=\"${google_monitoring_uptime_check_config.api[0].uptime_check_id}\""
      comparison      = "COMPARISON_GT"
      threshold_value = 1
      duration        = "600s"

      aggregations {
        alignment_period     = "300s"
        per_series_aligner   = "ALIGN_NEXT_OLDER"
        cross_series_reducer = "REDUCE_COUNT_FALSE"
        group_by_fields      = ["resource.label.*"]
      }

      trigger {
        count = 1
      }
    }
  }

  notification_channels = [google_monitoring_notification_channel.email[0].id]
}
