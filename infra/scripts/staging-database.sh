#!/usr/bin/env bash
# Starts or stops a stoppable environment database (PO decision 2026-10-30: staging keeps its data and
# costs only disk while stopped). Terraform ignores the activation policy, so this never fights an apply.
#
#   infra/scripts/staging-database.sh start|stop <project> [instance]
set -euo pipefail

ACTION="${1:?start or stop}"
PROJECT="${2:?project id}"
INSTANCE="${3:-opentms-staging}"

case "${ACTION}" in
  start) POLICY=ALWAYS ;;
  stop) POLICY=NEVER ;;
  *) echo "usage: $0 start|stop <project> [instance]" >&2; exit 2 ;;
esac

current="$(gcloud sql instances describe "${INSTANCE}" --project "${PROJECT}" --format='value(settings.activationPolicy)')"
if [[ "${current}" == "${POLICY}" ]]; then
  # The policy may already be set by a start or stop that is still running (another workflow): wait for it.
  pending="$(gcloud sql operations list --instance "${INSTANCE}" --project "${PROJECT}" \
    --filter='status!=DONE' --format='value(name)')"
  if [[ -n "${pending}" ]]; then
    # shellcheck disable=SC2086 # one operation name per word
    gcloud sql operations wait ${pending} --project "${PROJECT}" --timeout=1800
  fi
  echo "${INSTANCE} is already ${ACTION}ed (${POLICY})."
  exit 0
fi

# A start can take well over the ten minutes gcloud waits for a patch by itself, so start the operation
# asynchronously and wait for it with our own (longer) timeout.
operation="$(gcloud sql instances patch "${INSTANCE}" --project "${PROJECT}" --activation-policy="${POLICY}" \
  --async --quiet --format='value(name)')"
[[ -n "${operation}" ]] || { echo "No operation returned for ${INSTANCE}." >&2; exit 1; }
gcloud sql operations wait "${operation}" --project "${PROJECT}" --timeout=1800
echo "${INSTANCE} is ${ACTION}ed (${POLICY})."
