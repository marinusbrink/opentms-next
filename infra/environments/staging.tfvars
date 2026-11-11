# Staging: no running costs while idle (PO decision 2026-10-30). Both services scale to zero and the
# database is stoppable — .github/workflows/staging-database.yml starts and stops it.
environment      = "staging"
project_id       = "ryd-opentms-staging"
host_admin_email = "marinus@brink.nu"

# The same machine as prod's, so the performance gate measures what prod will do (design 76). Stopped
# while idle like before, so it costs only while a deploy or a test runs.
database = {
  tier                = "db-custom-1-3840"
  backups             = false
  deletion_protection = false
}

api = {
  min_instances = 0
  max_instances = 1
}

web = {
  max_instances = 1
}

performance_gate = true

monitoring = {
  uptime_check = false # it would keep staging awake
}
