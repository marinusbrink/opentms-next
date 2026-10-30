# A Cloud Run service whose revisions are deployed by the deploy workflow, not by Terraform.
# Terraform owns everything around the container (identity, scaling, secrets, probes); the image and
# the traffic split belong to .github/workflows/deploy.yml, so Terraform ignores them after creation.

terraform {
  required_providers {
    google = { source = "hashicorp/google" }
  }
}

resource "google_cloud_run_v2_service" "this" {
  name                = var.name
  location            = var.region
  ingress             = "INGRESS_TRAFFIC_ALL"
  deletion_protection = var.deletion_protection

  # Public without an allUsers IAM binding, which the organisation's domain-restricted-sharing policy
  # would refuse. The SPA and the API are meant to be reached by any browser.
  invoker_iam_disabled = true

  labels = var.labels

  template {
    service_account                  = var.service_account
    session_affinity                 = var.session_affinity
    max_instance_request_concurrency = var.concurrency
    timeout                          = "300s"

    scaling {
      min_instance_count = var.min_instances
      max_instance_count = var.max_instances
    }

    dynamic "volumes" {
      for_each = var.cloud_sql_connection_name == null ? [] : [var.cloud_sql_connection_name]
      content {
        name = "cloudsql"
        cloud_sql_instance {
          instances = [volumes.value]
        }
      }
    }

    containers {
      # Placeholder until the first deploy; ignored afterwards.
      image = "us-docker.pkg.dev/cloudrun/container/hello"

      ports {
        container_port = 8080
      }

      resources {
        limits = {
          cpu    = var.cpu
          memory = var.memory
        }
        # false = instance-based billing: CPU stays on while an instance lives, so Hangfire and the
        # outbox dispatcher keep working between requests.
        cpu_idle          = var.cpu_idle
        startup_cpu_boost = true
      }

      dynamic "env" {
        for_each = var.env
        content {
          name  = env.key
          value = env.value
        }
      }

      dynamic "env" {
        for_each = var.secret_env
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

      dynamic "volume_mounts" {
        for_each = var.cloud_sql_connection_name == null ? [] : [1]
        content {
          name       = "cloudsql"
          mount_path = "/cloudsql"
        }
      }

      dynamic "startup_probe" {
        for_each = var.health_path == null ? [] : [var.health_path]
        content {
          http_get {
            path = startup_probe.value
          }
          period_seconds    = 5
          timeout_seconds   = 3
          failure_threshold = 24 # two minutes for a cold start
        }
      }

      dynamic "liveness_probe" {
        for_each = var.health_path == null ? [] : [var.health_path]
        content {
          http_get {
            path = liveness_probe.value
          }
          period_seconds    = 30
          timeout_seconds   = 5
          failure_threshold = 3
        }
      }
    }
  }

  lifecycle {
    ignore_changes = [
      template[0].containers[0].image,
      template[0].revision,
      template[0].labels,
      template[0].annotations,
      traffic,
      client,
      client_version,
      scaling,
    ]
  }
}
