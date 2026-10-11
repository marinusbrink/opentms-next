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
