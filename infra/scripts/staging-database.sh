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
  echo "${INSTANCE} is already ${ACTION}ed (${POLICY})."
else
  gcloud sql instances patch "${INSTANCE}" --project "${PROJECT}" --activation-policy="${POLICY}" --quiet
fi

if [[ "${ACTION}" == "start" ]]; then
  for _ in $(seq 1 60); do
    state="$(gcloud sql instances describe "${INSTANCE}" --project "${PROJECT}" --format='value(state)')"
    [[ "${state}" == "RUNNABLE" ]] && { echo "${INSTANCE} is running."; exit 0; }
    sleep 10
  done
  echo "${INSTANCE} did not become RUNNABLE within ten minutes." >&2
  exit 1
fi
