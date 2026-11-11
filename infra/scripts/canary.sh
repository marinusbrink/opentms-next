#!/usr/bin/env bash
# Canary rollout of a Cloud Run service (department design §5, PO decision 2026-10-30):
#   1. deploy the image as a new revision without traffic, tagged "canary";
#   2. check /health/ready on the tag URL;
#   3. give it CANARY_PERCENT of traffic and observe for OBSERVE_MINUTES;
#   4. compare its p95 response time with the previous revision's over the same minutes (design 76);
#   5. promote to 100% — or put 100% back on the previous revision and fail.
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
# Slower means: p95 above LATENCY_FACTOR times the previous revision's and above LATENCY_FLOOR_MS (the read budget), with
# at least MIN_LATENCY_SAMPLES requests on each revision. With fewer the comparison says nothing and is skipped.
LATENCY_FACTOR="${LATENCY_FACTOR:-1.5}"
LATENCY_FLOOR_MS="${LATENCY_FLOOR_MS:-300}"
MIN_LATENCY_SAMPLES="${MIN_LATENCY_SAMPLES:-50}"

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

# Request latencies of one revision since the canary got traffic, in milliseconds. Long-lived connections (SignalR) and the
# health probes say nothing about how fast a request is answered.
latencies() {
  # A failed read must not stop the script halfway with the canary on part of the traffic: it counts as no samples.
  { gcloud logging read \
    "resource.type=\"cloud_run_revision\" AND resource.labels.service_name=\"${SERVICE}\" AND resource.labels.revision_name=\"$1\" AND httpRequest.latency:* AND timestamp>=\"${since}\" AND NOT httpRequest.requestUrl:\"/signalr-hubs/\" AND NOT httpRequest.requestUrl:\"/health/\"" \
    --project "${PROJECT}" --limit 100000 --format='value(httpRequest.latency)' || true; } | sed -n 's/^\([0-9.]*\)s$/\1/p' | awk '{ printf "%d\n", $1 * 1000 }'
}

# p95 of the numbers on stdin (nearest rank), or nothing for no numbers.
p95() { sort -n | awk '{ v[NR] = $1 } END { if (NR > 0) { i = int(NR * 0.95); if (i < NR * 0.95) i++; print v[i] } }'; }

canary_ms="$(latencies "${canary}")"
previous_ms="$(latencies "${previous}")"
canary_n="$(printf '%s' "${canary_ms}" | grep -c . || true)"
previous_n="$(printf '%s' "${previous_ms}" | grep -c . || true)"
if (( canary_n >= MIN_LATENCY_SAMPLES && previous_n >= MIN_LATENCY_SAMPLES )); then
  canary_p95="$(printf '%s\n' "${canary_ms}" | p95)"
  previous_p95="$(printf '%s\n' "${previous_ms}" | p95)"
  echo "p95 response time: canary ${canary_p95} ms over ${canary_n} requests, previous ${previous_p95} ms over ${previous_n}."
  if awk -v c="${canary_p95}" -v p="${previous_p95}" -v f="${LATENCY_FACTOR}" -v floor="${LATENCY_FLOOR_MS}" 'BEGIN { exit !(c > p * f && c > floor) }'; then
    rollback "p95 ${canary_p95} ms against ${previous_p95} ms on the previous revision"
  fi
else
  echo "p95 response time not compared: ${canary_n} canary and ${previous_n} previous requests, fewer than ${MIN_LATENCY_SAMPLES} on a side."
fi

run services update-traffic "${SERVICE}" --to-latest
run services update-traffic "${SERVICE}" --remove-tags canary
echo "Promoted ${canary} to 100%."
