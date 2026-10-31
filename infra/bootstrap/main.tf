# Bootstrap: everything that needs organisation rights or exists once for all environments.
# Applied by hand by the PO (docs/designs/gcp-infrastructure.md) — never by CI, so no CI identity
# holds project-creator or billing rights. Run through ../scripts/bootstrap.sh, which creates the
# state bucket first (the one resource Terraform cannot own: its own state lives in it).

terraform {
  required_version = ">= 1.9"

  required_providers {
    google = {
      source  = "hashicorp/google"
      version = "~> 7.0"
    }
    google-beta = {
      source  = "hashicorp/google-beta"
      version = "~> 7.0"
    }
  }

  backend "gcs" {
    bucket = "ryd-platform-tfstate"
    prefix = "opentms/bootstrap"
  }
}

provider "google" {
  project = var.shared_project
  region  = var.region
}

provider "google-beta" {
  project = var.shared_project
  region  = var.region
}

locals {
  shared_apis = [
    "artifactregistry.googleapis.com",
    "cloudbilling.googleapis.com",
    "cloudresourcemanager.googleapis.com",
    "iam.googleapis.com",
    "iamcredentials.googleapis.com",
    "serviceusage.googleapis.com",
    "sts.googleapis.com",
  ]

  environment_apis = [
    "cloudresourcemanager.googleapis.com",
    "iam.googleapis.com",
    "iamcredentials.googleapis.com",
    "logging.googleapis.com",
    "monitoring.googleapis.com",
    "run.googleapis.com",
    "secretmanager.googleapis.com",
    "serviceusage.googleapis.com",
    "sqladmin.googleapis.com",
  ]

  environment_api_pairs = {
    for pair in setproduct(keys(var.environments), local.environment_apis) :
    "${pair[0]}/${pair[1]}" => { environment = pair[0], api = pair[1] }
  }

  # What CI may do inside an environment project (constitution rule 7: roles on the project they act in,
  # nothing on the organisation). The applier manages Cloud Run, Cloud SQL, secrets, service accounts and
  # their IAM there; the planner only reads (secret access because Terraform refreshes secret versions).
  apply_roles = [
    "roles/cloudsql.admin",
    "roles/iam.serviceAccountAdmin",
    "roles/iam.serviceAccountUser",
    "roles/monitoring.editor",
    "roles/resourcemanager.projectIamAdmin",
    "roles/run.admin",
    "roles/secretmanager.admin",
    "roles/serviceusage.serviceUsageConsumer",
  ]

  plan_roles = [
    "roles/iam.securityReviewer",
    "roles/secretmanager.secretAccessor",
    "roles/viewer",
  ]

  deploy_roles = [
    "roles/logging.viewer",
    "roles/run.developer",
  ]

  role_pairs = merge(
    { for pair in setproduct(keys(var.environments), local.apply_roles) : "apply/${pair[0]}/${pair[1]}" => { environment = pair[0], role = pair[1], member = google_service_account.terraform_apply.member } },
    { for pair in setproduct(keys(var.environments), local.plan_roles) : "plan/${pair[0]}/${pair[1]}" => { environment = pair[0], role = pair[1], member = google_service_account.terraform_plan.member } },
    { for pair in setproduct(keys(var.environments), local.deploy_roles) : "deploy/${pair[0]}/${pair[1]}" => { environment = pair[0], role = pair[1], member = google_service_account.deployer.member } },
    # A stoppable database is started by the deploy workflow before it migrates.
    { for name, env in var.environments : "deploy/${name}/roles/cloudsql.editor" => { environment = name, role = "roles/cloudsql.editor", member = google_service_account.deployer.member } if env.stoppable_database },
  )
}

# --- Shared project -------------------------------------------------------------------------------

resource "google_project_service" "shared" {
  for_each           = toset(local.shared_apis)
  service            = each.value
  disable_on_destroy = false
}

resource "google_artifact_registry_repository" "opentms" {
  repository_id = "opentms"
  location      = var.region
  format        = "DOCKER"
  description   = "OpenTMS container images (api, migrator, web), tagged with the commit SHA."

  cleanup_policy_dry_run = false

  cleanup_policies {
    id     = "keep-recent"
    action = "KEEP"
    most_recent_versions {
      keep_count = 30
    }
  }

  cleanup_policies {
    id     = "delete-old"
    action = "DELETE"
    condition {
      tag_state  = "ANY"
      older_than = "2592000s" # 30 days; the KEEP policy above wins for the 30 most recent
    }
  }

  depends_on = [google_project_service.shared]
}

# --- GitHub Actions identity (Workload Identity Federation, no keys) ---------------------------

resource "google_iam_workload_identity_pool" "github" {
  workload_identity_pool_id = "github"
  display_name              = "GitHub Actions"
  depends_on                = [google_project_service.shared]
}

resource "google_iam_workload_identity_pool_provider" "github" {
  workload_identity_pool_id          = google_iam_workload_identity_pool.github.workload_identity_pool_id
  workload_identity_pool_provider_id = "opentms-next"
  display_name                       = "marinusbrink/opentms-next"

  attribute_mapping = {
    "google.subject"       = "assertion.sub"
    "attribute.repository" = "assertion.repository"
    "attribute.ref"        = "assertion.ref"
  }

  # Only this repository's tokens are accepted at all.
  attribute_condition = "assertion.repository == \"${var.github_repository}\""

  oidc {
    issuer_uri = "https://token.actions.githubusercontent.com"
  }
}

resource "google_service_account" "terraform_plan" {
  account_id   = "gh-terraform-plan"
  display_name = "GitHub Actions: terraform plan (read-only)"
}

resource "google_service_account" "terraform_apply" {
  account_id   = "gh-terraform-apply"
  display_name = "GitHub Actions: terraform apply (main only)"
}

resource "google_service_account" "deployer" {
  account_id   = "gh-deployer"
  display_name = "GitHub Actions: build and deploy (main only)"
}

locals {
  pool                = google_iam_workload_identity_pool.github.name
  any_ref_of_the_repo = "principalSet://iam.googleapis.com/${local.pool}/attribute.repository/${var.github_repository}"
  main_branch_only    = "principalSet://iam.googleapis.com/${local.pool}/attribute.ref/refs/heads/main"
}

resource "google_service_account_iam_member" "terraform_plan_wif" {
  service_account_id = google_service_account.terraform_plan.name
  role               = "roles/iam.workloadIdentityUser"
  member             = local.any_ref_of_the_repo
}

resource "google_service_account_iam_member" "terraform_apply_wif" {
  service_account_id = google_service_account.terraform_apply.name
  role               = "roles/iam.workloadIdentityUser"
  member             = local.main_branch_only
}

resource "google_service_account_iam_member" "deployer_wif" {
  service_account_id = google_service_account.deployer.name
  role               = "roles/iam.workloadIdentityUser"
  member             = local.main_branch_only
}

resource "google_storage_bucket_iam_member" "state_read" {
  bucket = var.state_bucket
  role   = "roles/storage.objectViewer"
  member = google_service_account.terraform_plan.member
}

resource "google_storage_bucket_iam_member" "state_write" {
  bucket = var.state_bucket
  role   = "roles/storage.objectAdmin"
  member = google_service_account.terraform_apply.member
}

resource "google_artifact_registry_repository_iam_member" "deployer_push" {
  repository = google_artifact_registry_repository.opentms.name
  location   = var.region
  role       = "roles/artifactregistry.writer"
  member     = google_service_account.deployer.member
}

# --- One project per environment ------------------------------------------------------------

resource "google_project" "environment" {
  for_each = var.environments

  project_id      = each.value.project_id
  name            = "OpenTMS ${each.key}"
  org_id          = var.folder_id == null ? var.org_id : null
  folder_id       = var.folder_id
  billing_account = var.billing_account
  labels          = { app = "opentms", environment = each.key }
}

resource "google_project_service" "environment" {
  for_each           = local.environment_api_pairs
  project            = google_project.environment[each.value.environment].project_id
  service            = each.value.api
  disable_on_destroy = false
}

resource "google_project_iam_member" "ci" {
  for_each = local.role_pairs
  project  = google_project.environment[each.value.environment].project_id
  role     = each.value.role
  member   = each.value.member
}

# Cloud Run pulls images with the environment project's serverless service agent; that agent exists
# only once asked for, hence the explicit identity.
resource "google_project_service_identity" "run_agent" {
  provider   = google-beta
  for_each   = var.environments
  project    = google_project.environment[each.key].project_id
  service    = "run.googleapis.com"
  depends_on = [google_project_service.environment]
}

resource "google_artifact_registry_repository_iam_member" "run_agent_pull" {
  for_each   = var.environments
  repository = google_artifact_registry_repository.opentms.name
  location   = var.region
  role       = "roles/artifactregistry.reader"
  member     = google_project_service_identity.run_agent[each.key].member
}
