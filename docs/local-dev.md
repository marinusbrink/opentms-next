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
