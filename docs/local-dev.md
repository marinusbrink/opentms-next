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
