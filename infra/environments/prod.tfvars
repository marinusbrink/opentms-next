environment      = "prod"
project_id       = "ryd-opentms-prod"
host_admin_email = "marinus@brink.nu"

database = {
  tier                = "db-custom-1-3840"
  backups             = true
  deletion_protection = true
}

# One instance per revision until Redis (AP-13) exists; see the design's Assumptions.
api = {
  min_instances = 1
  max_instances = 1
}

web = {
  max_instances = 2
}

monitoring = {
  uptime_check = true
  alert_email  = "marinus@brink.nu"
}
