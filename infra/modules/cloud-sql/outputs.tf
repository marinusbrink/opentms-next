output "instance_name" {
  value = google_sql_database_instance.this.name
}

output "connection_name" {
  description = "PROJECT:REGION:INSTANCE, for the Cloud Run Cloud SQL volume."
  value       = google_sql_database_instance.this.connection_name
}

output "app_connection_string" {
  value     = "Host=${local.socket};Database=${google_sql_database.host.name};Username=${google_sql_user.app.name};Password=${random_password.app.result}"
  sensitive = true
}

output "provisioning_connection_string" {
  description = "Points at the maintenance database; the provisioner connects there to create tenant databases."
  value       = "Host=${local.socket};Database=postgres;Username=${google_sql_user.provisioner.name};Password=${random_password.provisioner.result}"
  sensitive   = true
}
