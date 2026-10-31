output "api_url" {
  value = local.api_url
}

output "web_url" {
  value = local.web_url
}

output "database_instance" {
  value = module.database.instance_name
}
