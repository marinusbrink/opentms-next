# The list of environments (PO decision 2026-10-30). To add one: an entry here, then
# ../environments/<name>.tfvars; apply bootstrap (PO), then the infra workflow picks it up.
environments = {
  staging = {
    project_id         = "ryd-opentms-staging"
    stoppable_database = true
  }
  prod = {
    project_id         = "ryd-opentms-prod"
    stoppable_database = false
  }
}
