# Local development — machine setup facts

Environment facts from the scaffold bootstrap (2026-08-12) on the primary dev machine
(macOS, Apple Silicon). The scaffold was verified against exactly this setup. This is a
works-on-my-machine antidote: facts, not preferences.

- **PostgreSQL 17 runs via Homebrew as a user process**, not a managed service.
  Superuser role `postgres` / `postgres`; existing databases: `OpenTms` (host) and
  `OpenTms_Demo` (the local `demo` tenant). Nothing restarts it after a reboot — run
  `brew services start postgresql@17` (or start it ad hoc with
  `pg_ctl -D /opt/homebrew/var/postgresql@17 start`, which needs a valid `LC_ALL`,
  e.g. `en_US.UTF-8`, or the postmaster dies at startup).
- **The .NET 10 SDK is user-local in `~/.dotnet`**; the system-wide install at
  `/usr/local/share/dotnet` only has SDK 8/9. Anything that shells out to `dotnet`
  (including `abp` and `dotnet-ef`) needs `DOTNET_ROOT=$HOME/.dotnet` and
  `$HOME/.dotnet:$HOME/.dotnet/tools` on `PATH`.
- **The API host runs plain HTTP in local dev**: `http://localhost:44301`
  (a self-signed HTTPS cert breaks browser→API fetches from the SPA). Production
  stays HTTPS behind TLS termination.
- **The PostgreSQL-backed tests create their own databases per checkout.**
  `PostgreSqlTestDatabases` (in `backend/test/OpenTms.EntityFrameworkCore.Tests/PostgreSql/`)
  drops and recreates `OpenTms_Tests_<suffix>` and `OpenTms_Tests_<suffix>_TenantA` once per
  test process. The suffix is `OPENTMS_TEST_PG_DB_SUFFIX` when set (letters, digits and
  underscores; other characters become `_`), otherwise the first eight hex characters of the
  SHA-256 of the full path of the directory holding `OpenTms.slnx`. Two test processes on this
  machine (a worktree next to the main checkout, the Stop hook's verify gate next to a running
  `dotnet test`) therefore never share a database; CI gets its own hash the same way. Server
  and credentials come from `OPENTMS_TEST_PG` (a connection string without `Database=`),
  default `localhost:5432`, `postgres`/`postgres`. A deleted worktree leaves its pair behind:
  list them with `SELECT datname FROM pg_database WHERE datname LIKE 'OpenTms\_Tests\_%'`
  and drop with `DROP DATABASE "<name>" WITH (FORCE)`.
- **Tenants app: the host login provisions locally.** `appsettings.Development.json` sets
  `Tenancy:Provisioning:AllowHostLogin`, so the Tenants app creates and drops tenant databases
  with `postgres`. Any other environment refuses that and needs a provisioning login of its own in
  `Tenancy:Provisioning:ConnectionString` (docs/designs/tenants-and-editions.md, "Provisioning login").
- **Data-protection keys live in the host database** (`platform."DataProtectionKeys"`), not in
  `~/.aspnet/DataProtection-Keys`. After the first start with them you sign in once more; the old
  key files can be deleted.
- **Dev tenants from before the whole-id database names (`test`, `test2`).** Their databases still
  carry the old short name `t_<name>_<8 hex>`, which the drop job no longer accepts (an eight-digit
  prefix can be shared by two tenants, so it cannot prove a database is this tenant's). Deleting such
  a tenant in the Tenants app removes the tenant and logs "has no generated database of its own;
  nothing is dropped"; drop the database and its role yourself afterwards. First check that the
  tenant is gone and which database it used:

  ```sql
  -- in OpenTms: the tenant (soft-deleted: IsDeleted = true) and its connection string with the database name
  SELECT t."Name", t."IsDeleted", c."Value" FROM "AbpTenants" t JOIN "AbpTenantConnectionStrings" c ON c."TenantId" = t."Id"
  WHERE t."Name" IN ('test', 'test2');
  -- databases with the old short name
  SELECT datname FROM pg_database WHERE datname ~ '^t_[a-z0-9_]*_[0-9a-f]{8}$';
  ```

  then, for each name that belongs to a deleted tenant only:
  `DROP DATABASE "<name>" WITH (FORCE); DROP ROLE "<name>";`

## Report rendering (pdfgeneratorapi.com)

The report definition commands (edit template, copy template, test document; design 55) call
pdfgeneratorapi.com with FiLogic's account. `appsettings.json` of the host holds only the base URL and the workspace
names (`Integrations:PdfGeneratorApi`); the key and the secret never go into an appsettings file, the database or the
repository. Without them the commands are disabled ("not configured") and the `pdfgeneratorapi` check in Maintenance →
Health reports Degraded; nothing else depends on them.

To render locally, put the key and secret you were given in the host's user secrets (stored outside the repository,
in `~/.microsoft/usersecrets/opentms-next-httpapi-host/`; loaded in the Development environment only):

```bash
cd backend/src/OpenTms.HttpApi.Host
dotnet user-secrets set "Integrations:PdfGeneratorApi:Key" "<api key>"
dotnet user-secrets set "Integrations:PdfGeneratorApi:Secret" "<api secret>"
dotnet user-secrets list   # check; never paste the output anywhere
```

Other environments set them as environment variables `Integrations__PdfGeneratorApi__Key` and
`Integrations__PdfGeneratorApi__Secret`, injected from Secret Manager. **Do not copy the values from the OpenTMS
repository**: they are the production credentials in plain text there and are to be rotated after the cut-over.
Then switch the feature `Platform.DocumentRendering` on in the tenant's edition (Tenants app → Editions → Features).

## Demo master data

`scripts/seed-demo-masterdata.sh <tenant-name>` fills one **empty** local tenant with a large,
realistic set of Master Data, so lists, lookups and performance can be judged at volume. Dev only.

- **What it writes** (schema `masterdata`, one transaction): 20,000 relations (1 own company, 20
  depots, 300 carriers, 150 creditors, the rest customers; ~3 % archived, ~1 % blocked) with
  ~24,600 addresses, ~40,000 contact persons, ~2,800 IBANs, category and region memberships and
  external ids; 800 products (40 ADR), 25 transport units, 19 vehicle types, 26 transport
  characteristics, 6 temperatures, 48 geographical regions (NL, BE, DE); 500 drivers with ~1,800
  qualifications (some expiring within 90 days or expired); 1,000 vehicles and 305 combinations; 50,000
  product relation codes. Afterwards it runs `ANALYZE` on the masterdata tables, checks the data
  against the domain rules (`scripts/seed-demo-masterdata/verify.sql`, every check must be 0) and
  switches all `Apps.MasterData.*` features on for the tenant in the host database (`features.sql`).
- **The data is fictional.** Names, addresses, VAT/CoC numbers, IBANs, plates and VINs are generated
  (check digits valid); every e-mail address and website is on a `.example` domain.
- **Deterministic.** `gen_masterdata.py` (Python 3 standard library only) produces the same SQL for the
  same ids, seed (default 20261016) and reference date; the script passes today's date, so qualification
  validity and creation times are relative to the day you run it. A tenant in the shared host database
  gets a seed derived from its id, because row ids are global primary keys there.

Run it after the DbMigrator has created and seeded the tenant:

```bash
scripts/seed-demo-masterdata.sh demo             # or: --dry-run demo (lookups and generation only)
```

Guards: it refuses any `PGHOST` other than `localhost`, `127.0.0.1` or `::1` (defaults `PGHOST=localhost`,
`PGPORT=5432`, `PGUSER=postgres`, `PGPASSWORD=postgres`); it looks the tenant up by name in `OpenTms`
(not deleted) and finds its database in `AbpTenantConnectionStrings` (no connection string: the tenant
lives in the host database); it stops with a message when the admin user, the NL/BE/DE countries, the
Visiting/Postal address types or EUR are missing (DbMigrator seed not run), and when the tenant already
has any relation: it fills an empty tenant only. On any error during the load nothing stays. The backend
caches feature values: restart the API host to see the Master Data app.
