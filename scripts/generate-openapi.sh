#!/usr/bin/env bash
# Regenerates openapi/opentms-next.json from the running API surface.
#
# The committed spec is the API contract: the typed TypeScript client is generated
# from it (frontend: npm run generate:client), and CI fails when the spec or the
# client is out of date with the code. Run this after changing any app service,
# DTO, or controller:
#
#   ./scripts/generate-openapi.sh
#
# Requires: a reachable PostgreSQL with an up-to-date host database (run the
# DbMigrator first) — the host boots for real to produce the same document it
# serves at runtime.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
HOST_PROJECT="$REPO_ROOT/backend/src/OpenTms.HttpApi.Host"
OUTPUT="$REPO_ROOT/openapi/opentms-next.json"
URL="http://localhost:44399"

echo "Building host..."
dotnet build "$HOST_PROJECT" -v q --nologo

echo "Starting host on $URL (generation instance)..."
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS="$URL" \
App__SelfUrl="$URL" \
AuthServer__Authority="$URL" \
AuthServer__RequireHttpsMetadata=false \
dotnet run --project "$HOST_PROJECT" --no-build --no-launch-profile > /tmp/opentms-openapi-host.log 2>&1 &
HOST_PID=$!
trap 'kill $HOST_PID 2>/dev/null || true' EXIT

echo "Waiting for /health/ready..."
for i in $(seq 1 60); do
  if curl -sf "$URL/health/ready" > /dev/null 2>&1; then
    break
  fi
  if ! kill -0 $HOST_PID 2>/dev/null; then
    echo "Host process died — see /tmp/opentms-openapi-host.log" >&2
    exit 1
  fi
  sleep 2
done
curl -sf "$URL/health/ready" > /dev/null || { echo "Host never became ready" >&2; exit 1; }

echo "Fetching swagger document..."
mkdir -p "$(dirname "$OUTPUT")"
curl -sf "$URL/swagger/v1/swagger.json" | python3 -c '
import json, sys
# Deterministic serialization: sorted object keys, fixed indentation, trailing newline.
# (JSON object key order carries no meaning; array order is preserved.)
doc = json.load(sys.stdin)
print(json.dumps(doc, indent=2, sort_keys=True, ensure_ascii=False))
' > "$OUTPUT"

echo "Wrote $OUTPUT ($(wc -l < "$OUTPUT" | tr -d " ") lines)"
