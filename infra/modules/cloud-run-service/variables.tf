variable "name" {
  type = string
}

variable "region" {
  type = string
}

variable "service_account" {
  type = string
}

variable "min_instances" {
  type = number
}

variable "max_instances" {
  type = number
}

variable "cpu" {
  type    = string
  default = "1"
}

variable "memory" {
  type    = string
  default = "512Mi"
}

variable "cpu_idle" {
  description = "true = request-based billing (CPU only during requests); false = CPU always on while an instance lives."
  type        = bool
  default     = true
}

variable "concurrency" {
  type    = number
  default = 80
}

variable "session_affinity" {
  type    = bool
  default = false
}

variable "env" {
  description = "Plain environment variables."
  type        = map(string)
  default     = {}
}

variable "secret_env" {
  description = "Environment variable name => Secret Manager secret id (latest version)."
  type        = map(string)
  default     = {}
}

variable "cloud_sql_connection_name" {
  description = "Mounts the Cloud SQL connector socket under /cloudsql when set."
  type        = string
  default     = null
}

variable "health_path" {
  description = "HTTP path for the startup and liveness probes; null leaves Cloud Run's default TCP probe."
  type        = string
  default     = null
}

variable "deletion_protection" {
  type    = bool
  default = false
}

variable "labels" {
  type    = map(string)
  default = {}
}
