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
| **Reporting** | Dashboards, data warehouse, sustainability/CO₂; read-only |
| *Platform (horizontal)* | ABP core (identity, permissions, tenancy, localization, audit), mail, background jobs, document generation, notifications |

Heavyweights (Financial, Planning & Execution) keep a strict internal submodule
structure so a later split is a deployment decision, not a rebuild. Financial's
submodules (Tariffs, Invoicing, Purchasing, TransportUnits) are folders/namespaces
inside the Financial module — **not** separate ABP modules (see
`backend/modules/financial/README.md`).

**Waiting for the transport domain (PO decision 2026-10-16):** fixed CMR texts
(`ConsignmentNoteFixedDescription`), the CMR print profile (`ConsignmentNoteProfile`) and transport profiles
(`TransportProfile`) belong to the transport domain (Orders or Planning & Execution, chosen in that run's design),
not to Platform. The first design that touches CMR printing or transport set-up takes them in or names them under
*Out of scope* with the PO's agreement. Facts and rules: `docs/input/masterdata-rebuild-assessment.md` §2.3.

## Target architecture (PO-owned)

[docs/architecture/](docs/architecture/README.md) describes where the system is heading: the
TMS core that accepts commands, writes facts and publishes events, with agents working on top
of it. Every element there carries a status (BUILT / DECIDED / PROPOSED / OPEN); the register
is [docs/architecture/decisions.md](docs/architecture/decisions.md).

- **Authority order:** the SaaS constitution and department conventions, then this file,
  then `docs/architecture/`. Where they seem to conflict, stop and report; do not pick a side.
- **Before designing anything beyond a single screen or endpoint**, read
  `docs/architecture/01-overview.md`, then the files the README's "which file to read when"
  table names for the PBI. In the design doc, cite the decision ids (`AD-…`) in *Domain
  impact*, name which of the three flows (`02-flows.md`) the feature extends, and list every
  PROPOSED item you rely on under *Assumptions*. A design that depends on an OPEN item reports
  it on the issue and stops.
- **Statuses:** the design PR that builds a DECIDED item flips its status in `decisions.md`
  and `01-overview.md`. Nothing else in the folder changes outside a PO decision.
- "LOS" means the logistics operating system (PO decision 2026-10-10, AD-19): the separate
  app of decision agents and workflows, not a reporting overview.

## Module boundary rules (enforced by the build)

One ABP module per domain under `backend/modules/`, five projects each
(`Domain.Shared`, `Domain`, `Application.Contracts`, `Application`,
`EntityFrameworkCore`). `test/OpenTms.ArchitectureTests` fails the build on violations.

1. **Schema-per-module.** Each module has its own `DbContext`, its own PostgreSQL schema
   (`orders`, `planning`, `financial`, `masterdata`, `integrations`, `reporting`,
   `platform`) and its own migrations history table. A module's DbContext maps only its
   own schema; no cross-schema joins; never map another module's entity.
2. **Events or published interfaces only.** Cross-module communication runs via events
   (distributed events through the transactional outbox, or the local event bus for a
   reaction inside the same transaction — see "Events and the outbox"; ETOs live in the
   publisher's `Domain.Shared/Events`) or explicitly published interfaces. A module may
   reference another module's `Domain.Shared` and `Application.Contracts` — never its
   `Domain`, `Application`, or `EntityFrameworkCore` projects, and never another module's
   `DbContext`.
3. **Cross-module application-service calls are a design decision, not a default.**
   The `Domain.Shared` and `Application.Contracts` references exist so you can use
   another module's DTOs and event types — they are not an invitation to call its
   application services. Before you call another module's app service synchronously,
   check the approved design doc: that call must be explicitly stated there (gate 1).
   If it is not, do not add it — publish an event on the local event bus and let the
   owning module react. Events are the default for cross-module effects; a synchronous
   call is the documented exception.
4. **Platform is the floor.** Every module may reference Platform; Platform references
   no domain module. The dependency arrow always points downward. Platform changes are
   risk class critical by default and always pass the design gate.
5. New permissions are ABP permission definitions in the owning module's
   `Application.Contracts/Permissions/` — never hardcoded checks.

## Events and the outbox

Design: [docs/designs/outbox-per-tenant.md](docs/designs/outbox-per-tenant.md). Two buses, two
guarantees:

| Bus | Guarantee | Use it for |
|---|---|---|
| `IDistributedEventBus` | Written to `platform."AbpEventOutbox"` in the **same transaction and the same database as the fact** (the tenant's own database; the host database for shared-tier tenants). Published afterwards, **at least once**, with handlers running in the raising tenant's context. | Every event another module, another runtime role or an agent reacts to. |
| `ILocalEventBus` | In-process, inside the current transaction. Nothing survives a crash. | A reaction that belongs to the same transaction. |

- Publish with `IDistributedEventBus.PublishAsync(eto)` from an application-service method that
  changes data. Nothing else is needed. **Publishing outside a transactional unit of work throws**
  (`OutboxRequiresTransactionException`): the fact would be committed without its event.
- Only events whose type lives in an `OpenTms.*` namespace use the outbox, always: the
  `useOutbox: false` parameter is ignored for them. ABP's own events keep their direct in-process
  behavior.
- **Handlers are idempotent** (constitution rule 3): delivery is at least once. An event states a
  fact in the past tense and carries ids, not copies of data (rule 5).
- The `OutboxDispatcher` is signalled by the committing tenant and sweeps every database every five
  minutes (`Outbox:SweepPeriod`); `Outbox:DispatchEnabled` is the per-instance switch. ABP's own
  outbox sender is switched off (it reads only the host database) — never switch it back on.
- Never read or write the outbox table yourself. Unit tests on SQLite bypass the outbox; the
  PostgreSQL-backed tests (`OpenTmsPostgreSqlTestModule`) cover it.

## Derived values and live updates

Design: [docs/designs/49-derived-values-and-live-updates.md](docs/designs/49-derived-values-and-live-updates.md) (AD-07,
flow 2). Every stored calculated value (cost or revenue per leg, CO₂, totals) uses this one mechanism, never its own job:

- A module registers an `IDerivedValueCalculator` per record type (`Configure<DerivedValueOptions>(o => o.AddCalculator<…>("<Module>.<Entity>", watchPermission))`)
  and calls `IDerivedValueScheduler.RequestAsync(type, id, reason)` in every save that changes an input. The save returns
  at once; the calculation runs as a Hangfire job in the tenant's own transaction and is discarded when a newer save
  arrived meanwhile. Reads never calculate.
- Screens watch records through the SignalR hub `/signalr-hubs/record-updates` with `useRecordUpdates` /
  `useDerivedValueStates` (`src/lib/realtime/`) and show `CalculatingIndicator` while a value is pending.
- Multi-instance hosting needs `SignalR:Redis:Configuration` (the Redis backplane); one instance works without.

## Multi-tenancy model

- **Database-per-tenant is the provisioning default.** One **host database** (`OpenTms`)
  holds the tenant store, settings, and the module schemas for shared-tier tenants. Each
  provisioned tenant gets its **own PostgreSQL database containing all module schemas**,
  resolved via ABP's per-tenant connection strings (`AbpTenantConnectionStrings`).
- **The mechanism stays per-tenant configurable** (ABP default): a tenant *without* a
  connection string lives in the host database — a shared-database tier needs no
  rearchitecting. Do not build anything that assumes all tenants have their own database.
- Tenant provisioning (create database and login role, run migrations, seed, invite the
  administrator) is the Platform Hangfire job `TenantProvisioningJob`, started from the host-only
  **Tenants** app (PO decision 2026-10-14, [docs/designs/tenants-and-editions.md](docs/designs/tenants-and-editions.md)).
  Every tenant created there gets its own database `t_<name>_<id>` with its own role; pool settings
  come from `Tenancy:ConnectionPool`. Deleting a tenant there drops its database.
- `OpenTms.DbMigrator` migrates the host database plus **every** tenant database,
  continues past a failing tenant, reports per-tenant results, and exits non-zero on any
  failure. Migrations and seeding live **only** there — with one exception: the provisioning job
  migrates and seeds a *new* tenant's database once; every later migration is the DbMigrator's.
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
| Master data | `/master-data` | All domains (their master-data lists, one nav group per domain; PO 2026-10-16); creates Platform users and organisation units in place from its pickers (PO 2026-10-16, UI-LOOK-004) |
| Integrations | `/integrations` | Integrations |
| Reports | `/reports` | Reporting |
| Tenants (host only; views Tenants and Editions) | `/tenants` | Platform |

**The master-data lists of every domain live in the Master data app** (PO decision 2026-10-16), one parent group per
domain in its nav tree (the Master Data domain's own group is General), never in the domain's operational app.

**Decided by the PO on 2026-10-10:** the **logistics operating system (LOS)** is a separate app
in this list: it runs the planning department through AI agents and workflows (decision agents,
policies, the autonomy ladder, the exception inbox; `docs/architecture/06-agents-and-ai.md`).
Its tile and route arrive with its first approved design; which domain owns its records is still
open (AO-02). "LOS" no longer refers to a reporting overview.

**Every app and every node of an app's navigation tree is an ABP feature** (PO decision 2026-10-14,
[docs/designs/tenants-and-editions.md](docs/designs/tenants-and-editions.md), "App features"): `AppFeatures` in
Platform, named after the tree path, mirrored by the `feature` of each entry in `apps.config.ts`. Adding an app or
a nav node adds its feature in the same change. The host-only Tenants app has none.

Frontend structure: `src/domains/<domain>/` mirrors the backend modules (query hooks,
domain components, and dialogs two apps share, e.g. `domains/platform/components/UserFormDialog`; an app never imports another app); `src/apps/<app>/` holds the views composing them. All server access
goes through the generated typed client (`src/lib/api/client.ts`) wrapped in TanStack
Query — no hand-written fetches. Every user-facing string, shell and app names included,
comes from ABP's localization endpoint (resources in each module's `Domain.Shared`;
languages: nl, en — adding one is a PO decision).

## UI conventions (binding for every screen)

- UI work follows `docs/conventions/ui/ui-rules/`. Before building or reviewing any screen,
  read [docs/conventions/ui/ui-rules/index.md](docs/conventions/ui/ui-rules/index.md) and the
  rule files it lists for the task. Deviations are marked
  `DEVIATION(UI-XXX-nnn): <reason>, approved in <design doc>`; an unmarked one is a reviewer
  blocker. The canonical copy lives in `dev-department/conventions/ui/`; change rules there.
- Tokens come from `frontend/src/styles/opentms-theme.css` (UI-TOK-001: no raw colours, sizes
  or shadows in feature code). `--brand` is set at runtime by `applyTenantTheme()` only.
- The library components the rules name (`EntityDialog`, `ConfirmDialog`, `FormField`,
  `OpenTmsGrid` with `rowActions` / `onOpen`, the grid cells, `useFormat()`, `useIsGranted()`,
  `getApiErrorMessage()`) live in `src/components/ui/` and `src/lib/`. The ones not built yet
  (`EntityLookup`, `LinesGrid`, `StatusBadge`, `FormSection`, `Wizard`, `useOpenRecord`) are
  built by the first feature that needs them, with the API the rule gives, never inline in a
  page. Library gaps in `docs/conventions/ui/component-map.md` §4 need a PO choice first.

## Frontend environment facts (verified 2026-08-12)

- TypeScript is pinned to **5.9** in `frontend/package.json` because `openapi-typescript`
  does not yet accept TypeScript 6 as a peer dependency. Do not raise the pin without
  verifying that its peer range has changed.
- shadcn/ui in this repo generates **Base UI** components (`@base-ui/react`), not Radix:
  compose triggers with the `render` prop. Radix-era `asChild` patterns from older
  examples do not exist here and will not compile.

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
Machine-setup facts for the primary dev machine: [docs/local-dev.md](docs/local-dev.md).

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
- ABP UI packages beyond the auth server's login pages (LeptonX-lite serves only
  `/Account/*`; the product UI is exclusively the React SPA).
