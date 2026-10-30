# Staging: no running costs while idle (PO decision 2026-10-30). Both services scale to zero and the
# database is stoppable — .github/workflows/staging-database.yml starts and stops it.
environment      = "staging"
project_id       = "ryd-opentms-staging"
host_admin_email = "marinus@brink.nu"

database = {
  tier                = "db-f1-micro"
  backups             = false
  deletion_protection = false
  max_connections     = 50
}

api = {
  min_instances = 0
  max_instances = 1
}

web = {
  max_instances = 1
}

monitoring = {
  uptime_check = false # it would keep staging awake
}
