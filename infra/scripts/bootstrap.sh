#!/usr/bin/env bash
# Applies infra/bootstrap as the PO (organisation rights: project creation, billing).
# Run once, and again whenever infra/bootstrap/environments.auto.tfvars changes.
#
#   TF_VAR_org_id=123456789012 TF_VAR_billing_account=XXXXXX-XXXXXX-XXXXXX infra/scripts/bootstrap.sh
#
# Creates the Terraform state bucket first: Terraform keeps its own state there, so the bucket is the
# one resource outside Terraform (docs/designs/gcp-infrastructure.md).
set -euo pipefail

SHARED_PROJECT="${SHARED_PROJECT:-ryd-platform}"
STATE_BUCKET="${STATE_BUCKET:-ryd-platform-tfstate}"
REGION="${REGION:-europe-west4}"

: "${TF_VAR_org_id:?set TF_VAR_org_id to the brink.nu organisation id (gcloud organizations list)}"
: "${TF_VAR_billing_account:?set TF_VAR_billing_account (gcloud billing accounts list)}"

if ! gcloud storage buckets describe "gs://${STATE_BUCKET}" --project "${SHARED_PROJECT}" >/dev/null 2>&1; then
  echo "Creating state bucket gs://${STATE_BUCKET}"
  gcloud storage buckets create "gs://${STATE_BUCKET}" \
    --project "${SHARED_PROJECT}" \
    --location "${REGION}" \
    --uniform-bucket-level-access \
    --public-access-prevention
fi
gcloud storage buckets update "gs://${STATE_BUCKET}" --versioning >/dev/null

cd "$(dirname "$0")/../bootstrap"
terraform init -input=false
terraform apply -input=false "$@"
