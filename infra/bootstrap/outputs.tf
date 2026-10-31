output "workload_identity_provider" {
  description = "Value for the GCP_WORKLOAD_IDENTITY_PROVIDER repository variable."
  value       = google_iam_workload_identity_pool_provider.github.name
}

output "service_accounts" {
  value = {
    terraform_plan  = google_service_account.terraform_plan.email
    terraform_apply = google_service_account.terraform_apply.email
    deployer        = google_service_account.deployer.email
  }
}

output "registry" {
  value = "${var.region}-docker.pkg.dev/${var.shared_project}/${google_artifact_registry_repository.opentms.repository_id}"
}

output "environment_projects" {
  value = { for name, project in google_project.environment : name => { id = project.project_id, number = project.number } }
}
