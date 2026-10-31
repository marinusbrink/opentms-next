variable "org_id" {
  description = "Numeric id of the brink.nu organisation."
  type        = string
}

variable "folder_id" {
  description = "Optional folder (numeric id) to create the environment projects in instead of the organisation root."
  type        = string
  default     = null
}

variable "billing_account" {
  description = "Billing account the environment projects are linked to (XXXXXX-XXXXXX-XXXXXX)."
  type        = string
}

variable "shared_project" {
  description = "Project holding what all environments share: state, images, GitHub identity."
  type        = string
  default     = "ryd-platform"
}

variable "state_bucket" {
  description = "Terraform state bucket; created by scripts/bootstrap.sh, must match the backend blocks."
  type        = string
  default     = "ryd-platform-tfstate"
}

variable "region" {
  type    = string
  default = "europe-west4"
}

variable "github_repository" {
  description = "The only GitHub repository whose workflows may use the CI service accounts."
  type        = string
  default     = "marinusbrink/opentms-next"
}

variable "environments" {
  description = "Every environment, keyed by name. Adding one here plus ../environments/<name>.tfvars is all it takes."
  type = map(object({
    project_id         = string
    stoppable_database = bool
  }))
}
