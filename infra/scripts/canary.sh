#!/usr/bin/env bash
# Canary rollout of a Cloud Run service (department design §5, PO decision 2026-10-30):
#   1. deploy the image as a new revision without traffic, tagged "canary";
#   2. check /health/ready on the tag URL;
#   3. give it CANARY_PERCENT of traffic and observe for OBSERVE_MINUTES;
#   4. promote to 100% — or put 100% back on the previous revision and fail.
#
#   infra/scripts/canary.sh <project> <region> <service> <image>
set -euo pipefail

PROJECT="${1:?project}"
REGION="${2:?region}"
SERVICE="${3:?service}"
IMAGE="${4:?image}"

CANARY_PERCENT="${CANARY_PERCENT:-10}"
OBSERVE_MINUTES="${OBSERVE_MINUTES:-10}"
MAX_ERROR_PERCENT="${MAX_ERROR_PERCENT:-1}"
MIN_ERRORS="${MIN_ERRORS:-5}"

run() { gcloud run "$@" --project "${PROJECT}" --region "${REGION}" --quiet; }

previous="$(run services describe "${SERVICE}" --format='value(status.latestReadyRevisionName)')"
echo "Serving revision: ${previous}"

run deploy "${SERVICE}" --image "${IMAGE}" --no-traffic --tag canary
canary="$(run services describe "${SERVICE}" --format='value(status.latestCreatedRevisionName)')"
canary_url="$(run services describe "${SERVICE}" --format=json | jq -r '.status.traffic[] | select(.tag == "canary") | .url')"
echo "Canary revision: ${canary} at ${canary_url}"

rollback() {
  echo "::error::Canary ${canary} failed: $1. Restoring 100% on ${previous}."
  run services update-traffic "${SERVICE}" --to-revisions "${previous}=100"
  run services update-traffic "${SERVICE}" --remove-tags canary || true
  exit 1
}

ready() { curl -fsS --max-time 10 "${canary_url}/health/ready" >/dev/null; }

for attempt in $(seq 1 12); do
  ready && break
  [[ "${attempt}" == 12 ]] && rollback "not ready on its tag URL"
  sleep 10
done

run services update-traffic "${SERVICE}" --to-revisions "${canary}=${CANARY_PERCENT}"
since="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
echo "Canary has ${CANARY_PERCENT}% of traffic; observing for ${OBSERVE_MINUTES} minutes."

count() {
  gcloud logging read \
    "resource.type=\"cloud_run_revision\" AND resource.labels.service_name=\"${SERVICE}\" AND resource.labels.revision_name=\"${canary}\" AND httpRequest.requestUrl:* AND timestamp>=\"${since}\" $1" \
    --project "${PROJECT}" --limit 100000 --format='value(timestamp)' | wc -l | tr -d ' '
}

for minute in $(seq 1 "${OBSERVE_MINUTES}"); do
  sleep 60
  ready || rollback "not ready after ${minute} minute(s)"
  total="$(count "")"
  errors="$(count "AND httpRequest.status>=500")"
  echo "  minute ${minute}: ${errors} errors in ${total} requests"
  if (( errors >= MIN_ERRORS && errors * 100 > total * MAX_ERROR_PERCENT )); then
    rollback "${errors} 5xx responses in ${total} requests"
  fi
done

run services update-traffic "${SERVICE}" --to-latest
run services update-traffic "${SERVICE}" --remove-tags canary
echo "Promoted ${canary} to 100%."
