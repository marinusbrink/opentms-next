# opentms-next

Multi-tenant SaaS Transport Management System for Dutch logistics service providers —
the OpenTMS rebuild. Developed by the agent department defined in
`marinusbrink/dev-department`.

**Department conventions bind all work in this repository.** Before doing anything, the
conventions in `marinusbrink/dev-department/conventions/` apply in full: the
[SaaS constitution](https://github.com/marinusbrink/dev-department/blob/main/conventions/saas-constitution.md)
(seven non-negotiable rules; deviations only via the marked escape hatch),
[coding conventions](https://github.com/marinusbrink/dev-department/blob/main/conventions/coding-conventions.md),
and [performance budgets](https://github.com/marinusbrink/dev-department/blob/main/conventions/performance-budgets.md).
Where this file and a department convention seem to conflict, stop and report on the
issue — do not pick a side silently.

## Domain map (owned by the PO)

Copied from design §3.1 (`dev-department/docs/design/software-development-department.md`).
**This map belongs to the PO.** Agents may *propose* splits or shifts via the
retrospective route, never apply them.

| Domain | Responsibility |
|---|---|
| **Orders** | Intake via all channels, order lifecycle |
| **Planning & Execution** | Planboard, trips, assignment, realtime status, POD, track & trace |
| **Financial** | Internal submodules: Tariffs & Calculation, Invoicing, Purchasing/Carrier settlement, Transport-unit balances |
| **Master Data** | Relations, addresses, vehicles, drivers |
| **Integrations** | EDI, OTM5, accounting links, partner links; anti-corruption layer |
| **Reporting** | Dashboards (incl. LOS), data warehouse, sustainability/CO₂; read-only |
| *Platform (horizontal)* | ABP core (identity, permissions, tenancy, localization, audit), mail, background jobs, document generation, notifications |

Heavyweights (Financial, Planning & Execution) keep a strict internal submodule
structure so a later split is a deployment decision, not a rebuild. Financial's
submodules (Tariffs, Invoicing, Purchasing, TransportUnits) are folders/namespaces
inside the Financial module — **not** separate ABP modules (see
`backend/modules/financial/README.md`).

## Module boundary rules (enforced by the build)

One ABP module per domain under `backend/modules/`, five projects each
(`Domain.Shared`, `Domain`, `Application.Contracts`, `Application`,
`EntityFrameworkCore`). `test/OpenTms.ArchitectureTests` fails the build on violations.

1. **Schema-per-module.** Each module has its own `DbContext`, its own PostgreSQL schema
   (`orders`, `planning`, `financial`, `masterdata`, `integrations`, `reporting`,
   `platform`) and its own migrations history table. A module's DbContext maps only its
   own schema; no cross-schema joins; never map another module's entity.
2. **Events or published interfaces only.** Cross-module communication runs via the
   local event bus (`ILocalEventBus`, ETOs live in the publisher's `Domain.Shared/Events`)
   or explicitly published interfaces. A module may reference another module's
   `Domain.Shared` and `Application.Contracts` — never its `Domain`, `Application`, or
   `EntityFrameworkCore` projects, and never another module's `DbContext`.
3. **Platform is the floor.** Every module may reference Platform; Platform references
   no domain module. The dependency arrow always points downward. Platform changes are
   risk class critical by default and always pass the design gate.
4. New permissions are ABP permission definitions in the owning module's
   `Application.Contracts/Permissions/` — never hardcoded checks.

## Multi-tenancy model

- **Database-per-tenant is the provisioning default.** One **host database** (`OpenTms`)
  holds the tenant store, settings, and the module schemas for shared-tier tenants. Each
  provisioned tenant gets its **own PostgreSQL database containing all module schemas**,
  resolved via ABP's per-tenant connection strings (`AbpTenantConnectionStrings`).
- **The mechanism stays per-tenant configurable** (ABP default): a tenant *without* a
  connection string lives in the host database — a shared-database tier needs no
  rearchitecting. Do not build anything that assumes all tenants have their own database.
- Tenant provisioning (create database, run migrations, seed) is a Platform application
  service (`ITenantProvisioningAppService`) with a Hangfire job — currently a **stub**;
  full wiring arrives at department onboarding. Until then, provision manually:
  insert the tenant + `Default` connection string, then run the DbMigrator (it creates
  the database).
- `OpenTms.DbMigrator` migrates the host database plus **every** tenant database,
  continues past a failing tenant, reports per-tenant results, and exits non-zero on any
  failure. Migrations and seeding live **only** there.
- Tenant isolation is constitution rule 1. Every query/job/export/log line is
  tenant-scoped through the ABP data filter; `IgnoreMultiTenancy` /
  `IDataFilter.Disable<IMultiTenant>()` only with the documented escape hatch.

## Apps are views on domains (PO-owned mapping)

The frontend shell is Office-365-style: waffle app-launcher, app tiles, one SPA. Apps
are **views**, not modules: an app never gets its own backend module, and it reads only
from its mapped domains through the generated client. The single source for this list is
`frontend/src/app/apps.config.ts`; this table mirrors it and **changes to the app list
or the mapping are PO decisions** — agents never create a module per app.

| App | Route | Reads from domain(s) |
|---|---|---|
| Dashboard | `/dashboard` | Reporting |
| Planboard | `/planboard` | Planning & Execution |
| Transport | `/transport` | Orders, Planning & Execution |
| Finance | `/finance` | Financial |
| Master data | `/master-data` | Master Data |
| Integrations | `/integrations` | Integrations |
| Reports | `/reports` | Reporting |

Frontend structure: `src/domains/<domain>/` mirrors the backend modules (query hooks,
domain components); `src/apps/<app>/` holds the views composing them. All server access
goes through the generated typed client (`src/lib/api/client.ts`) wrapped in TanStack
Query — no hand-written fetches. Every user-facing string, shell and app names included,
comes from ABP's localization endpoint (resources in each module's `Domain.Shared`;
languages: nl, en — adding one is a PO decision).

## API contract

`openapi/opentms-next.json` is the committed API contract, generated deterministically
from the running host. After any app-service/DTO/controller change:

```bash
./scripts/generate-openapi.sh          # regenerates the committed spec
cd frontend && npm run generate:client # regenerates the typed TS client
```

CI fails when either is out of date with the code. Module APIs are exposed as
conventional controllers with one route root per module (`/api/orders/...`,
`/api/planning/...`, ...).

## Running locally

Prereqs: .NET 10 SDK, Node ≥ 20.19, PostgreSQL 17 on `localhost:5432` with superuser
`postgres`/`postgres` (macOS: `brew install postgresql@17 && brew services start postgresql@17`),
ABP CLI (`dotnet tool install -g Volo.Abp.Studio.Cli`) for `abp install-libs`.

```bash
# 1. Migrate + seed the host database (and any tenant databases)
dotnet run --project backend/src/OpenTms.DbMigrator

# 2. Auth-server login page assets (once per clone; wwwroot/libs is untracked)
cd backend && abp install-libs

# 3. API host + OpenIddict auth server → http://localhost:44301 (dev is plain HTTP)
dotnet run --project backend/src/OpenTms.HttpApi.Host

# 4. Frontend → http://localhost:5173 (must be exactly this origin: OIDC + CORS)
cd frontend && npm install && npm run dev
```

Login: `admin` / `1q2w3E*` (host); tenant `demo` exists locally with its own database
and the same seeded credentials. Health: `/health/live`, `/health/ready`. Swagger UI:
`/swagger`.

```bash
# Backend tests (includes the architecture boundary tests)
dotnet test backend/OpenTms.slnx

# Frontend checks
cd frontend && npm run lint && npm run build
```

Adding a migration (per module context, applied only by the DbMigrator):

```bash
dotnet ef migrations add <Name> -p backend/modules/<module>/OpenTms.<Module>.EntityFrameworkCore
```

## Startup-performance house rules

- `PublishReadyToRun=true` stays **on** in the host publish profile
  (`backend/src/OpenTms.HttpApi.Host/Properties/PublishProfiles/DefaultPublish.pubxml`).
- Migrations never move into app startup — they live in the DbMigrator, period.
- Compiled EF Core models get enabled the moment cold start exceeds **4 seconds**.
- Structured JSON logging with correlation ids stays on from day one; no PII in logs
  (constitution rule 5).

## Deliberately absent (do not add without a PO decision)

- Deploy workflows / Cloud Run config — arrive at department onboarding.
- Real domain entities — every entity arrives via an approved design (gate 1).
- The full tenant-provisioning implementation (the Hangfire job is a stub).
- ABP UI packages beyond the auth server's login pages (LeptonX-lite serves only
  `/Account/*`; the product UI is exclusively the React SPA).
