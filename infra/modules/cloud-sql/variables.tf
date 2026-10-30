variable "name" {
  type = string
}

variable "region" {
  type = string
}

variable "tier" {
  type = string
}

variable "backups" {
  description = "Daily backups plus point-in-time recovery (7 days)."
  type        = bool
}

variable "deletion_protection" {
  type = bool
}

variable "max_connections" {
  description = "Overrides the tier's default connection limit (shared-core tiers default to 25)."
  type        = number
  default     = null
}

variable "labels" {
  type    = map(string)
  default = {}
}
