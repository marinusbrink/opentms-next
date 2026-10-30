variable "environment" {
  description = "Environment name; must match an entry in ../bootstrap/environments.auto.tfvars."
  type        = string
}

variable "project_id" {
  type = string
}

variable "region" {
  type    = string
  default = "europe-west4"
}

variable "deployer_service_account" {
  description = "The CI identity that deploys revisions (created in bootstrap)."
  type        = string
  default     = "gh-deployer@ryd-platform.iam.gserviceaccount.com"
}

variable "host_admin_email" {
  description = "E-mail address of the host administrator the migrator seeds on first run."
  type        = string
}

variable "database" {
  type = object({
    tier                = string
    backups             = bool
    deletion_protection = bool
    max_connections     = optional(number)
  })
}

variable "api" {
  type = object({
    min_instances = number
    max_instances = number
  })
}

variable "web" {
  type = object({
    max_instances = number
  })
}

variable "monitoring" {
  type = object({
    uptime_check = bool
    alert_email  = optional(string)
  })
}
