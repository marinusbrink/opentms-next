#!/usr/bin/env bash
# Fills one EMPTY local development tenant with a large set of fictional demo master data
# (20,000 relations, 500 drivers, 1,000 vehicles, 50,000 product relation codes, ...), checks it
# against the domain rules and switches the Master Data app features on for that tenant.
#
#   scripts/seed-demo-masterdata.sh [--dry-run] <tenant-name>
#
# Dev only: refuses any PostgreSQL server other than localhost. Connection: PGHOST (default localhost),
# PGPORT (5432), PGUSER (postgres), PGPASSWORD (default postgres). Host database: OPENTMS_HOST_DB
# (default OpenTms). --dry-run does every lookup and generates the script, but writes nothing.
# See docs/local-dev.md, "Demo master data".
set -euo pipefail

usage() {
  echo "usage: $0 [--dry-run] <tenant-name>" >&2
  exit 2
}

die() {
  echo "error: $*" >&2
  exit 1
}

dry_run=false
if [[ "${1:-}" == "--dry-run" ]]; then
  dry_run=true
  shift
fi
[[ $# -eq 1 && -n "$1" ]] || usage
tenant_name="$1"

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
seed_dir="$script_dir/seed-demo-masterdata"

export PGHOST="${PGHOST:-localhost}"
export PGPORT="${PGPORT:-5432}"
export PGUSER="${PGUSER:-postgres}"
host_db="${OPENTMS_HOST_DB:-OpenTms}"

is_local_host() {
  case "$1" in
    localhost | 127.0.0.1 | ::1) return 0 ;;
    *) return 1 ;;
  esac
}

is_local_host "$PGHOST" ||
  die "PGHOST is '$PGHOST'; this script writes thousands of rows and only runs against localhost, 127.0.0.1 or ::1."
export PGPASSWORD="${PGPASSWORD:-postgres}"
# psql must never wait in a pager: the verify output is long and the script runs unattended.
export PSQL_PAGER=cat PAGER=cat

command -v psql >/dev/null || die "psql not found on PATH."
command -v python3 >/dev/null || die "python3 not found on PATH."
python3 -c 'import sys; sys.exit(0 if sys.version_info >= (3, 7) else 1)' || die "Python 3.7 or newer is required."

# Runs the SQL on stdin against database $1 and prints unaligned, tuples-only output.
# Extra arguments (psql -v name=value) are passed through, so values are bound as :'name', never spliced into SQL.
query() {
  local db="$1"
  shift
  psql -X -q -At -v ON_ERROR_STOP=1 -d "$db" "$@"
}

# ── 1. Tenant, its id and its database (host database) ──────────────────────────────────────────
query "$host_db" -c 'SELECT 1' >/dev/null ||
  die "cannot connect to the host database '$host_db' on $PGHOST:$PGPORT as $PGUSER (run the DbMigrator first?)."

tenant_ids="$(query "$host_db" -v name="$tenant_name" <<'SQL'
SELECT "Id" FROM "AbpTenants" WHERE "Name" = :'name' AND NOT "IsDeleted";
SQL
)"
[[ -n "$tenant_ids" ]] || die "no (non-deleted) tenant named '$tenant_name' in '$host_db'."
[[ "$tenant_ids" != *$'\n'* ]] || die "more than one tenant named '$tenant_name' in '$host_db'."
tenant_id="$tenant_ids"

conn_string="$(query "$host_db" -v tid="$tenant_id" <<'SQL'
SELECT "Value" FROM "AbpTenantConnectionStrings" WHERE "TenantId" = :'tid' AND "Name" = 'Default';
SQL
)"

# Extracts a key's value from an ADO.NET-style connection string (case-insensitive key, ';'-separated).
conn_value() {
  local wanted="$1" part key
  local -a parts
  IFS=';' read -r -a parts <<<"$conn_string"
  for part in "${parts[@]}"; do
    key="$(echo "${part%%=*}" | tr -d '[:space:]' | tr '[:upper:]' '[:lower:]')"
    if [[ "$part" == *=* && "$key" == "$wanted" ]]; then
      echo "${part#*=}" | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//'
      return
    fi
  done
}

if [[ -n "$conn_string" ]]; then
  tenant_db="$(conn_value database)"
  [[ -n "$tenant_db" ]] || die "the connection string of tenant '$tenant_name' has no Database= part."
  conn_host="$(conn_value host)"
  if [[ -n "$conn_host" ]] && ! is_local_host "$conn_host"; then
    die "tenant '$tenant_name' lives on host '$conn_host', not on localhost; refusing."
  fi
  own_db=true
else
  # No connection string: the tenant lives in the host database (shared tier); its rows carry its TenantId there.
  tenant_db="$host_db"
  own_db=false
fi
echo "tenant '$tenant_name' ($tenant_id), database '$tenant_db' ($([[ $own_db == true ]] && echo "own database" || echo "shared host database"))"

# ── 2. Ids from the DbMigrator seed (tenant database) ───────────────────────────────────────────
query "$tenant_db" -c 'SELECT 1' >/dev/null || die "cannot connect to database '$tenant_db'."
[[ "$(query "$tenant_db" -c "SELECT to_regclass('masterdata.\"Relations\"') IS NOT NULL")" == t ]] ||
  die "database '$tenant_db' has no masterdata schema; run the DbMigrator first."

missing_seed="run 'dotnet run --project backend/src/OpenTms.DbMigrator' to seed the tenant's reference data first."

creator_id="$(query "$tenant_db" -v tid="$tenant_id" <<'SQL'
SELECT "Id" FROM "AbpUsers" WHERE "UserName" = 'admin' AND "TenantId" = :'tid' AND NOT "IsDeleted";
SQL
)"
[[ -n "$creator_id" ]] || die "tenant '$tenant_name' has no user 'admin' in '$tenant_db'; $missing_seed"

country_args=()
for cc in NL BE DE; do
  row="$(query "$tenant_db" -F '|' -v tid="$tenant_id" -v cc="$cc" <<'SQL'
SELECT "Id", "PrimaryTimeZoneId" FROM masterdata."Countries"
WHERE "TenantId" = :'tid' AND "IsoAlpha2" = :'cc' AND NOT "IsDeleted";
SQL
)"
  [[ -n "$row" ]] || die "country $cc missing for tenant '$tenant_name'; $missing_seed"
  case "$cc" in NL) zone=Europe/Amsterdam ;; BE) zone=Europe/Brussels ;; DE) zone=Europe/Berlin ;; esac
  [[ "${row#*|}" == "$zone" ]] ||
    die "country $cc has primary time zone '${row#*|}', the generator writes '$zone' on its addresses."
  country_args+=(--country "$cc=${row%%|*}")
done

address_type_id() {
  query "$tenant_db" -v tid="$tenant_id" -v kind="$1" <<'SQL'
SELECT "Id" FROM masterdata."AddressTypes" WHERE "TenantId" = :'tid' AND "Kind" = :'kind' AND NOT "IsDeleted";
SQL
}
# AddressTypeKind (OpenTms.MasterData.Domain.Shared/Reference): Visiting = 1, Postal = 2.
visiting_id="$(address_type_id 1)"
postal_id="$(address_type_id 2)"
[[ -n "$visiting_id" && -n "$postal_id" ]] || die "address type Visiting or Postal missing for tenant '$tenant_name'; $missing_seed"

eur_id="$(query "$tenant_db" -v tid="$tenant_id" <<'SQL'
SELECT "Id" FROM masterdata."Currencies" WHERE "TenantId" = :'tid' AND "IsoCode" = 'EUR' AND NOT "IsDeleted";
SQL
)"
[[ -n "$eur_id" ]] || die "currency EUR missing for tenant '$tenant_name'; $missing_seed"

# ── 3. Guard: an empty tenant only ──────────────────────────────────────────────────────────────
relation_count="$(query "$tenant_db" -v tid="$tenant_id" <<'SQL'
SELECT count(*) FROM masterdata."Relations" WHERE "TenantId" = :'tid';
SQL
)"
[[ "$relation_count" == 0 ]] ||
  die "tenant '$tenant_name' already has $relation_count relation(s); this script fills an empty tenant only."

# ── 4. Generate ─────────────────────────────────────────────────────────────────────────────────
tmp_root="${TMPDIR:-/tmp}"
work_dir="$(mktemp -d "${tmp_root%/}/seed-demo-masterdata.XXXXXX")"
trap 'rm -rf "$work_dir"' EXIT
seed_file="$work_dir/seed_masterdata.sql"

gen_args=(
  --tenant-id "$tenant_id" --tenant-name "$tenant_name" --creator-id "$creator_id" "${country_args[@]}"
  --address-type-visiting "$visiting_id" --address-type-postal "$postal_id" --eur "$eur_id"
  --database "$tenant_db" --reference-time "$(date +%Y-%m-%d)T09:00:00" --out "$seed_file"
)
if [[ $own_db == false ]]; then
  # Row ids follow from the seed and the primary keys are global: in the shared host database a second tenant
  # with the default seed would collide with the first, so derive the seed from the tenant id there.
  gen_args+=(--shared-database --seed "$((16#${tenant_id:0:8}))")
fi
echo "generating ..."
python3 "$seed_dir/gen_masterdata.py" "${gen_args[@]}"

if [[ $dry_run == true ]]; then
  echo "dry run: generated $(wc -l <"$seed_file" | tr -d ' ') lines; nothing written to '$tenant_db' or '$host_db'."
  exit 0
fi

# ── 5. Load (one transaction), analyze, verify ──────────────────────────────────────────────────
echo "loading into '$tenant_db' (single transaction) ..."
psql -X -q -1 -v ON_ERROR_STOP=1 -d "$tenant_db" -f "$seed_file" >/dev/null

echo "analyzing the masterdata tables ..."
query "$tenant_db" <<'SQL'
DO $$
DECLARE t text;
BEGIN
  FOR t IN SELECT table_name FROM information_schema.tables WHERE table_schema = 'masterdata' AND table_type = 'BASE TABLE' LOOP
    EXECUTE format('ANALYZE masterdata.%I', t);
  END LOOP;
END $$;
SQL

echo "verifying ..."
psql -X -q -v ON_ERROR_STOP=1 -v tenant_id="$tenant_id" -v own_db="$own_db" -d "$tenant_db" -f "$seed_dir/verify.sql" ||
  die "rule checks failed (the data is committed; see the output above)."

# ── 6. Features (host database) ─────────────────────────────────────────────────────────────────
echo "switching the Master Data app features on ..."
psql -X -q -v ON_ERROR_STOP=1 -v tenant_id="$tenant_id" -d "$host_db" -f "$seed_dir/features.sql"

echo
echo "done: demo master data loaded for tenant '$tenant_name'."
echo "The backend caches feature values: restart the API host (OpenTms.HttpApi.Host) to see the Master Data app."
