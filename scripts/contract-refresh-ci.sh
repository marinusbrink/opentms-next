#!/usr/bin/env bash
# Pipeline helper: regenerate the API contract artifacts (OpenAPI spec + typed
# TypeScript client) on a bare CI runner, booting the dependencies they need.
#
# Used by the department build pipeline (dept-build.yml: contract-refresh-command)
# mechanically between the backend and frontend implementers, so the frontend
# always builds against the contract the backend actually implemented — never a
# stale spec (incident 2026-08-14, run 31752956967).
#
# Side effect kept on purpose: the disposable Postgres container stays up with a
# migrated host database for the rest of the CI job — the test engineer's
# integration tests use it.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if ! pg_isready -h localhost -p 5432 >/dev/null 2>&1; then
  echo "Starting disposable Postgres 17 container..."
  docker run -d --name contract-refresh-pg -p 5432:5432 -e POSTGRES_PASSWORD=postgres postgres:17
  for _ in $(seq 1 30); do
    pg_isready -h localhost -p 5432 -U postgres >/dev/null 2>&1 && break
    sleep 2
  done
  pg_isready -h localhost -p 5432 -U postgres >/dev/null 2>&1 || {
    echo "Postgres never became ready" >&2
    exit 1
  }
fi

echo "Building backend solution..."
dotnet build "$REPO_ROOT/backend/OpenTms.slnx" -v q --nologo

echo "Migrating host database..."
dotnet run --project "$REPO_ROOT/backend/src/OpenTms.DbMigrator" --no-build

echo "Regenerating OpenAPI spec..."
"$REPO_ROOT/scripts/generate-openapi.sh"

echo "Regenerating typed client..."
(cd "$REPO_ROOT/frontend" && npm ci && npm run generate:client)

echo "Contract refresh complete."
