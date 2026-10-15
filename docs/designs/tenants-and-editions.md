# Design: Tenants and Editions apps (host only)

**PBI:** none; built on the PO's direct instruction in a working session on 2026-10-14 ("in host
only, introduce a new app called Tenants; users should be able to CRUD tenants; we don't have an
app for editions yet, create this one too, based on abp.io solution architecture and baselines").
The PO's wording plus the answers below are the design; this note records them so gate 2 can
review against it.
**Status:** in review
**Date:** 2026-10-14

> **Platform change — risk class critical** (design §3.1, `/CLAUDE.md` rule 4): it creates and
> drops databases and database roles. Design and implementation travel in one PR because the PO
> asked for the build directly; gate 1 and gate 2 are the same review.
>
> **DEVIATION(constitution-4): the host-only Tenants app (with its Editions view) and "Log in as a tenant
> user" (D14, including its banner) are gated by host-side permissions, not by a per-tenant feature flag,
> approved in this document (PO instruction 2026-10-14: "soft delete + drop database", no flag).** A feature
> value belongs to a tenant; the host has none to carry it. Same reasoning as design #8 and
> `maintenance-view.md`. The app and navigation features (D8) are themselves new tenant-visible switches whose
> defaults follow D9.

## PO decisions (2026-10-14)

| # | Question | Decision |
|---|---|---|
| D1 | Host database for a tenant? | Never. Every tenant created here gets its **own database**. ("Use host database" is gone from the form.) The shared tier stays possible in the mechanism (`/CLAUDE.md`); this screen just does not offer it. |
| D2 | Admin password | Always random, **never shown or sent**. The admin receives an invitation email with a one-time set-password link (ABP's password-reset token). |
| D3 | Connection string | Generated, not typed. Database on the server of the host database by default. Pool: `Minimum Pool Size=0; Maximum Pool Size=10; Connection Idle Lifetime=60; Connection Pruning Interval=10`, from configuration. |
| D4 | Database credentials | An **own login role per tenant**, owner of only that database, random password. `CONNECT` revoked from `PUBLIC`. |
| D5 | Delete | Soft delete of the tenant **and** drop of its database and role in a background job. Irreversible; the UI asks for the tenant name to be typed. |
| D6 | "Should change password", "Send activation email", "Active" | Not on the form: the first two always happen (D2), and ABP's open-source `Tenant` has no active state. |
| D7 | Editions | Built as ABP's commercial Saas module does it, on the open-source pieces: an `Edition` aggregate, `Tenant.EditionId`, feature values under the `"E"` provider of FeatureManagement. |
| D8 | What an edition switches | Each app is a feature and each node of an app's navigation tree is a feature as well; in this PR, because editions are empty without them. |
| D9 | Defaults of the app features | On for screens that were live without a flag (Administration and its nodes; the Master data app and its groups), off for the rest (the Master data lists and Maintenance, which were behind a flag; the placeholder apps). |
| D10 | The ad-hoc features | Replaced, with their stored values migrated: `MasterData.FirstRun`, `MasterData.Relations`, `Platform.Maintenance`; `UI.CommonToolbar` (unused) removed. `Integrations.AddressLookup` stays: a capability, not a navigation node. |
| D11 | Host-only app | The Tenants app (with its Editions view) has no feature: host-side permissions only. |
| D13 | One app | Tenants and Editions are one app, Tenants, with a left pane: Tenants and Editions. Tenants comes before Administration in the launcher. |
| D14 | Log in as | In this PR. The host admin picks a user of the tenant (not only its admin), needs a new host permission, and sees a banner with the way back while impersonating. No time limit and no restriction to ready tenants. |
| D12 | Pickers | A picker (lookup) that another screen uses needs only its app's feature (plus its permission, as before), so lists can be packaged independently. |

## Domain impact

**Module:** Platform (horizontal). No domain module changes. Flows (`02-flows.md`): none of the
three; this is host administration below them. Decisions relied on: **AD-03** (database per tenant
plus a host database), **AD-10** (connections managed deliberately), **AD-13** (Hangfire does the
background work), **AD-18** (application services are the command layer).

### Entities

| Entity | Where | Notes |
|---|---|---|
| `Edition` (new, `FullAuditedAggregateRoot<Guid>`) | `platform."Editions"`, host database only (not `IMultiTenant`) | `DisplayName` (unique among non-deleted, max 256). The table exists in tenant databases too (shared schema) and stays empty there, like `ApplicationInstances`. |
| `Tenant.EditionId` (extension) | `public."AbpTenants"."EditionId"`, `uuid NULL` | ABP's documented way to extend a module entity: `ObjectExtensionManager.MapEfCoreProperty`, one migration in the application (Framework) context. |
| `Tenant` extra properties | JSON, no column | `ProvisioningState` (`Queued`, `Running`, `Succeeded`, `Failed`), `ProvisioningError` (exception type and message, never a connection string), `ProvisioningAt`. Read only for display. |

### Edition features

- Feature values are stored by ABP FeatureManagement under provider `"E"` with the edition id as
  key; edited through ABP's own `api/feature-management/features?providerName=E&providerKey={id}`.
  `FeatureManagementOptions.ProviderPolicies["E"] = Platform.Editions.ManageFeatures`.
- **Resolution — deviation from ABP Saas, recorded here:** Saas puts the edition id in an
  `editionid` claim that ABP's `EditionFeatureValueProvider` reads. A claim is frozen at login
  and absent in background jobs and event handlers, so a feature flag (constitution rule 4) would
  evaluate differently in a job than in a request. Platform replaces that provider with
  `TenantEditionFeatureValueProvider` (same name `"E"`, same position): it resolves the edition of
  `ICurrentTenant.Id` through `ITenantEditionStore`, cached per tenant (distributed cache, 10 min,
  removed when a tenant's edition changes). Order is unchanged: tenant value (`"T"`) over edition
  value (`"E"`) over the default. The cache item is `[IgnoreMultiTenancy]`: it is keyed by tenant id and read from
  any context, and without it ABP would prefix the key with the current tenant, so the host-side update could not
  remove the entry a feature check cached inside the tenant. No data filter is involved.

### Application services (host only, `RemoteService(IsEnabled = false)` + controllers)

`ITenantAdministrationAppService`, route root `api/platform/tenants`:

| Endpoint | Permission | Does |
|---|---|---|
| `GET /` (grid) | `Platform.Tenants` | name, edition, database name, server, provisioning state, created |
| `GET /{id}` | `Platform.Tenants` | one tenant; the connection string with `Password` masked |
| `GET /connection-preview?name=` | `Platform.Tenants.Create` | the connection string the name would get, password masked (the form shows it read-only) |
| `POST /` | `Platform.Tenants.Create` | create tenant + connection string, queue provisioning |
| `PUT /{id}` | `Platform.Tenants.Update` | name and edition. The database keeps its name (it is derived once). |
| `DELETE /{id}` | `Platform.Tenants.Delete` | soft delete, queue the drop |
| `POST /{id}/retry-provisioning` | `Platform.Tenants.Create` | re-queue a failed provisioning (every step is idempotent) |
| `POST /{id}/resend-invitation` | `Platform.Tenants.Update` | new set-password link to the tenant admin |

`IEditionAdministrationAppService`, route root `api/platform/editions`: grid (name, tenant count),
get, create, update, delete (`409` with the tenant count when tenants use it; no force — move the
tenants first). The tenant form's edition picker is `GET api/platform/tenants/edition-lookup` on the Tenants
service, so a user with `Platform.Tenants` but without `Platform.Editions` can still pick an edition.

Permissions (all `MultiTenancySides.Host`): `Platform.Tenants`, `Platform.Tenants.Create`,
`Platform.Tenants.Update`, `Platform.Tenants.Delete`, `Platform.Tenants.Impersonation`, `Platform.Editions`,
`Platform.Editions.Create`, `Platform.Editions.Update`, `Platform.Editions.Delete`,
`Platform.Editions.ManageFeatures`. The existing `Platform.TenantProvisioning.Manage` keeps its endpoint, which
now follows the same rules as "Retry provisioning": host only, only a `Failed` tenant, queued after commit. Its
request DTO lost the unused `DatabaseName` (names are generated, D3).

### Provisioning (the stub becomes real)

`TenantProvisioningJob` (Hangfire, `Platform.TenantProvisioning`), args `{ TenantId }` only — **no
address, password or connection string in job arguments** (Hangfire stores them in plain text). The
admin email waits on the tenant as the extra property `PendingAdminEmail` and is removed when
provisioning succeeds. Each step is idempotent, so a retry continues where it stopped:

1. **Database** — `ITenantDatabaseProvisioner` (port in `Platform.Domain`, Npgsql adapter in
   `Platform.EntityFrameworkCore`): connects to the server's `postgres` database with the
   provisioning login (`Tenancy:Provisioning:ConnectionString`, defaults to the host connection
   string), then `CREATE ROLE … LOGIN PASSWORD …` (or `ALTER ROLE … PASSWORD` when it exists),
   `CREATE DATABASE … OWNER …` when absent, `REVOKE CONNECT … FROM PUBLIC`. Identifiers are
   generated (`[a-z0-9_]`, quoted); the password is passed as a literal through
   `quote_literal` server-side, never concatenated.
2. **Schema** — every `IDbSchemaMigrator` under `ICurrentTenant.Change(tenantId)`, Framework first,
   exactly as `OpenTmsDbMigrationService` does. The migrations run as the tenant's own role, so it
   owns every object.
3. **Seed** — `IDataSeeder` with a random admin password (`RandomPasswordGenerator`, the policy of
   `IdentityOptions`), which is discarded.
4. **Invite** — once (`InvitationSentAt` extra property): a password-reset token for the admin and an
   email with the link `{App:SelfUrl}/Account/ResetPassword?userId=…&__tenant=…&resetToken=…`
   (ABP's `IAppUrlProvider.GetResetPasswordUrlAsync("MVC")`, the page LeptonX-lite already serves).

State goes to `ProvisioningState` after each run. A failure records the exception type and
message and logs the tenant id (no email, rule 5). It is **not** rethrown: a Hangfire retry could
overlap the user's "Retry provisioning", which only a `Failed` tenant accepts, so runs never overlap.

**Naming.** Database and role are both `t_<slug>_<the whole tenant id, 32 hex>`, slug = the tenant name
lowercased, non-`[a-z0-9]` replaced by `_`, cut so the whole name stays within 63 bytes. Unique
by the id part, stable across retries, readable in `psql`.

**Provisioning login (constitution rule 7).** `Tenancy:Provisioning:ConnectionString` names the login that
creates and drops databases and roles (CREATEDB, CREATEROLE), so the login every request uses carries no such rights.
Provisioning and the drop job **refuse** to run with the host database's login, whether the setting is empty or names
that same login, unless `Tenancy:Provisioning:AllowHostLogin` is on. Only `appsettings.Development.json` turns it on
(local development has one superuser); there the host login is used and a warning is logged once. The refusal is an
`InvalidOperationException` naming the setting, recorded on the tenant as a failed provisioning (follow-up of
2026-10-15). The login itself, a secret, is configured by the deployment work.

The provisioning login is not a superuser (Cloud SQL has none either). PostgreSQL 16+ lets such a login create a
database owned by a role, and drop it, only as a member of that role, so after creating or updating the tenant role
the provisioner runs `GRANT <role> TO CURRENT_USER` (repeating it is a no-op). The provisioning login therefore can act
as every tenant role it created, which it could already do by setting their passwords.

**Invitation.** A tenant is Ready once seeded; an invitation that cannot be sent does not undo that. It is noted on
the tenant (`InvitationNotSent: <exception type>`) and "Resend invitation" tries again. Only the exception type is
logged or stored, never its message, which for mail servers often contains the address (rule 5). The invitation
job does not rethrow, so Hangfire never retries it into a second mail; a mail sent whose bookkeeping then fails can
still be sent twice by a retry of provisioning, which is accepted (an extra invitation, no harm).

**DbMigrator.** It skips tenants whose `ProvisioningState` is set and not `Succeeded`: those belong to their
provisioning job, and seeding them would give the administrator ABP's default password (D2). Tenants without a
state (from before this app) are migrated as always.

**Delete during provisioning** is refused (`Queued`, `Running`): a drop racing the job could leave the database
behind. Deleting removes `PendingAdminEmail` from the tenant row (GDPR art. 17).

**Connection string.** Built with `NpgsqlConnectionStringBuilder` from the host connection string's
`Host` and `Port` (`Tenancy:Database:Host`/`Port` override them), the generated database, role and
password, and the pool settings from `Tenancy:ConnectionPool` (D3). The full string is stored as the
tenant's `Default` connection string (ABP's `AbpTenantConnectionStrings`, plain text as ABP
stores it; encrypting tenant connection strings is out of scope). It never leaves the backend
unmasked.

### Delete

`DELETE` soft-deletes the tenant (ABP `ISoftDelete`; the DbMigrator, the outbox sweep and the
Databases tab stop seeing it) and queues `TenantDatabaseDropJob { TenantId, DatabaseName, RoleName }`.
The job drops `DATABASE … WITH (FORCE)` (PostgreSQL 13+) and the role. **Guards**, checked in the
app service *and* again in the job: the tenant has its own connection string; its database is not
the host database (name and server compared); the name matches the generated pattern `t_*`.
A tenant on the shared tier is soft-deleted only.

## App features (D8–D12)

**Names follow the tree.** A node's ancestors are its dot-prefixes, so `Apps.MasterData.Reference.Countries`
is on only when `Apps.MasterData` and `Apps.MasterData.Reference` are on too. ABP's feature checker does not
combine parent and child, so every check passes the whole chain. Constants: `AppFeatures`
(`Platform.Application.Contracts/Features`); definitions: `PlatformFeatureDefinitionProvider`, group `Apps`,
each node a child of the one above it, so the edition's Features tab shows the app tree. Platform defines them
because apps are views, not modules, and Platform is the one place every module may reference. The tree
mirrors `frontend/src/app/apps.config.ts`; adding an app or a node adds its feature in the same change.

**All features after this change (34):**

| # | Feature | Parent | Default | Checked by |
|---|---|---|---|---|
| 1 | Apps.Dashboard | | false | shell |
| 2 | Apps.Planboard | | false | shell |
| 3 | Apps.Transport | | false | shell |
| 4 | Apps.Finance | | false | shell |
| 5 | Apps.MasterData | | true | shell; class attribute of every master-data service (pickers need only this) |
| 6 | Apps.MasterData.Relations | Apps.MasterData | true | shell; chain of 7–9 |
| 7 | Apps.MasterData.Relations.Relations | Apps.MasterData.Relations | false | shell; `RelationAppService` (not its pickers) |
| 8 | Apps.MasterData.Relations.AddressTypes | Apps.MasterData.Relations | false | shell; `AddressTypeAppService` |
| 9 | Apps.MasterData.Relations.RelationCategories | Apps.MasterData.Relations | false | shell; `RelationCategoryAppService` |
| 10 | Apps.MasterData.Transport | Apps.MasterData | true | shell; chain of 11–13 |
| 11 | Apps.MasterData.Transport.Products | Apps.MasterData.Transport | false | shell; `ProductAppService` |
| 12 | Apps.MasterData.Transport.TransportUnits | Apps.MasterData.Transport | false | shell; `TransportUnitAppService` |
| 13 | Apps.MasterData.Transport.Temperatures | Apps.MasterData.Transport | false | shell; `TemperatureAppService` |
| 14 | Apps.MasterData.Fleet | Apps.MasterData | true | shell; chain of 15–17 |
| 15 | Apps.MasterData.Fleet.Drivers | Apps.MasterData.Fleet | false | shell; `DriverAppService` |
| 16 | Apps.MasterData.Fleet.VehicleTypes | Apps.MasterData.Fleet | false | shell; `VehicleTypeAppService` |
| 17 | Apps.MasterData.Fleet.TransportCharacteristics | Apps.MasterData.Fleet | false | shell; `TransportCharacteristicsAppService` (not its picker) |
| 18 | Apps.MasterData.Reference | Apps.MasterData | true | shell; chain of 19–21 |
| 19 | Apps.MasterData.Reference.Countries | Apps.MasterData.Reference | false | shell; `CountryAppService` (not its pickers) |
| 20 | Apps.MasterData.Reference.Currencies | Apps.MasterData.Reference | false | shell; `CurrencyAppService` |
| 21 | Apps.MasterData.Reference.GeographicalRegions | Apps.MasterData.Reference | false | shell; `GeographicalRegionAppService` (not its pickers) |
| 22 | Apps.Integrations | | false | shell |
| 23 | Apps.Reports | | false | shell |
| 24 | Apps.Administration | | true | shell (host always on); chain of 25–31 |
| 25 | Apps.Administration.Users | Apps.Administration | true | shell; `UserAdministrationController` (tenant side) |
| 26 | Apps.Administration.Roles | Apps.Administration | true | shell; `UserRoleAdministrationController` except the list (picker) |
| 27 | Apps.Administration.OrganizationUnits | Apps.Administration | true | shell; `OrganizationUnitAdministrationController` except `GET all` (picker) |
| 28 | Apps.Administration.AuditLogs | Apps.Administration | true | shell; chain of 29–30 |
| 29 | Apps.Administration.AuditLogs.FunctionalLogbook | Apps.Administration.AuditLogs | true | shell; functional actions of `LogbookAdministrationController` |
| 30 | Apps.Administration.AuditLogs.TechnicalLogbook | Apps.Administration.AuditLogs | true | shell; technical actions of `LogbookAdministrationController` |
| 31 | Apps.Administration.Maintenance | Apps.Administration | false | shell; `MaintenanceAdministrationController` (tenant side) |
| 32 | Integrations.AddressLookup | | false | `AddressLookupAppService`, `RelationEditor` (unchanged) |
| 33 | SettingManagement.Enable | | true | ABP (unchanged) |
| 34 | SettingManagement.AllowChangingEmailSettings | SettingManagement.Enable | false | ABP (unchanged) |

Removed: `MasterData.FirstRun`, `MasterData.Relations`, `Platform.Maintenance`, `UI.CommonToolbar`.

**Pickers (D12)** need only the app feature: every `GetLookupAsync`, `Relation.GetLookupPageAsync` (Drivers),
`Relation.GetAddressesAsync`, `GeographicalRegion.GetMatchesAsync` and `Country.GetDefaultAsync` (Relations),
`TransportCharacteristics.GetLookupPageAsync` (Vehicle types). Administration's role list and `organization-units/all`
are pickers too (the user editor; the relation editor picks organization units), so they keep their permission only.

**Host side.** Administration is marked `hostAlwaysEnabled` in the frontend and its controllers use
`RequiresTenantAppFeature`, which checks only when a tenant is current: the host has no tenant or edition to carry
a value. Master data keeps ABP's `[RequiresFeature]`, which on the host evaluates the defaults, as before.

**Frontend.** `app-features.ts` evaluates the chain from the application configuration. The launcher hides an app
that is off or whose nodes are all off; the nav pane hides nodes that are off; the shell renders
`FeatureUnavailable` for a route that is off (once the configuration has loaded); the Master data and Administration
roots redirect to the first node that is on. Views no longer check features themselves.

**Migration.** `MoveFeatureValuesToAppTree` (application context, raw SQL on ABP's `AbpFeatureValues`) copies every
stored tenant and edition value of an old feature to each node it covered, unless that node already has a value.
**Expand only** (constitution rule 2): the old values stay, so a previous release running during a rolling deploy
still finds them; their definitions are gone from the code, so they are inert. **Contract step:**
`RemoveOldFeatureNames` (follow-up PR of 2026-10-15, after the expand step merged; no release with the old names was
ever deployed) deletes the old values, their `AbpFeatures` rows, and the groups `MasterData`, `Platform` and `UI` when
no feature is left in them. Its `Down` restores nothing: the values were copied to the app tree, and no code reads
the old names. `MasterData.FirstRun` → the ten lists that
are not Relations or Drivers; `MasterData.Relations` → Relations and Drivers; `Platform.Maintenance` → Maintenance.

## Log in as a tenant user (D14)

ABP's commercial Saas/Account Pro modules offer "Login with this tenant/user" through an `Impersonation` token
grant. The open-source packages carry the building blocks only: the impersonator claims
(`AbpClaimTypes.ImpersonatorUserId`, `…TenantId`, `…UserName`, `…TenantName`), `ICurrentUser.FindImpersonator*()`,
and the audit log's `ImpersonatorUserId`/`ImpersonatorTenantId` columns. The flow is built on those, with the normal
authorization-code flow instead of a custom grant, so the SPA never handles a token it did not get through PKCE:

1. **Pick.** Row action "Log in as…" (permission `Platform.Tenants.Impersonation`, host side, seeded for the host
   admin role) opens a picker of the tenant's users: `GET api/platform/tenants/{id}/users` reads them in the
   tenant's own context (no filter disabled). No email address in the list.
2. **Ticket.** `POST api/platform/tenants/{id}/impersonate/{userId}` checks the user exists and is active and
   returns `{App:SelfUrl}/Account/Impersonate?ticket=…`. The ticket (`ImpersonationTicket`: expected session user
   and tenant, target user and tenant) is protected by ASP.NET Core data protection, time-limited to five minutes,
   and carries no secret.
3. **Switch.** The SPA stores the target tenant, removes its tokens from storage (not through the OIDC context:
   that would start a sign-in that cancels the navigation) and navigates to the page. `Pages/Account/Impersonate`
   (auth server) redeems the ticket only for the session it names: no session → log in first, then back; another
   user or tenant → refused (back to the SPA with a message, previous tenant restored). It signs the browser in as
   the target with the impersonator claims and sends it to the SPA's `/admin`, which signs in through the code flow.
4. **Claims.** `ImpersonationClaimsPrincipalHandler` (an `IAbpOpenIddictClaimsPrincipalHandler`) copies the
   impersonator claims from the auth cookie (authorize) or the code/refresh token (token endpoint) into the access
   and identity tokens; `SecurityStampValidatorOptions.OnRefreshingPrincipal` keeps them in the cookie when Identity
   refreshes it. ABP's audit log then records the impersonator on every request.
5. **Banner and return.** While `currentUser.impersonatorUserId` is set, a strip under the app bar says who is
   logged in as whom, with "Back to host". `POST api/platform/impersonation/return` issues a return ticket whose
   target is the impersonator; the page checks the cookie's impersonator claims name it, and signs back in without
   them.

Logs carry user and tenant ids only.

### Shared data-protection keys (follow-up of 2026-10-15)

Tickets and the auth cookie are protected with ASP.NET Core data protection, so every instance needs the same keys.
The key ring lives in the host database, `platform."DataProtectionKeys"` (entity `DataProtectionKeyRecord`, a Platform
migration; tenant databases have the table and keep it empty). `HostDataProtectionKeyStore` (Platform.Domain) reads
and adds keys under `ICurrentTenant.Change(null)` in a unit of work of its own, because the key manager may load its
key ring inside a tenant's request; `HostDatabaseXmlRepository` (API host) is the `IXmlRepository` on it, and the
application name is fixed to `OpenTms` instead of the content root path. Rows are only added (ASP.NET Core revokes by
adding an element); there is one key per 90 days. The keys are stored as ASP.NET Core writes them, not encrypted at
rest: the host database already holds every tenant's connection string, so whoever reads it has more than these keys.
Encrypting them with a key-management service is a deployment choice that `ProtectKeysWith…` adds without a schema
change. Cost: one row per 90 days, read about once a day per instance (constitution rule 6: negligible).

### ABP endpoints that are not served (follow-up of 2026-10-15)

ABP's own `IdentityUserController` (`/api/identity/users…`, including `lookup`), `IdentityRoleController`
(`/api/identity/roles…`) and the TenantManagement `TenantController` (`/api/multi-tenancy/tenants…`) are removed
from the API (`AbpAspNetCoreMvcOptions.ControllersToRemove` in `OpenTmsHttpApiModule`). Administration and Tenants
replace them, and only those check the app features (D8–D12) and the product's rules; ABP's tenant endpoints would
also create a tenant without provisioning and return its connection string, password included, which must never leave
the backend unmasked. The application services behind them stay registered: our own controllers use them. No client
used these endpoints (the SPA calls Administration and Tenants only, and nothing is deployed), so removing them breaks
no consumer (constitution rule 2). `/api/abp/multi-tenancy/tenants/by-name` (tenant switch), feature management and
permission management stay.

## UI

One app, **Tenants** (`/tenants`, `domains: ["Platform"]`), with a left pane of two views (PO 2026-10-14):
**Tenants** (`/tenants/list`, permission `Platform.Tenants`) and **Editions** (`/tenants/editions`,
`Platform.Editions`). `AppDefinition.requiredPermission` takes a list (any one of them) and `AppView` got its own
`requiredPermission`, so the tile shows with either permission and each pane entry with its own; the bare
`/tenants` goes to the first view the user may see. Host-side permissions keep the app away from tenants.
Screens follow the list and
dialog rules (UI-PAGE, UI-GRID, UI-DLG, UI-FORM, UI-ACT, UI-PERM, UI-FB):

- **Tenants list:** name, edition, database, server, status (`StatusBadge`: queued / running /
  ready / failed), created. Commands: New tenant. Row actions: Edit, Retry provisioning (failed
  only), Resend invitation (ready only), Delete.
- **Tenant dialog:** *Tenant* section — Name\*, Edition (lookup, optional); *Administrator* section
  (create only) — Admin email\*; *Database* section — the generated connection string, read-only,
  password masked, with a one-line hint that the database is created in the background. Plus the
  record-identification tab.
- **Delete tenant:** `ConfirmDialog`, destructive, with a warning that the database is dropped and
  an input that must equal the tenant name before Delete enables.
- **Editions list:** name, tenant count. Commands: New edition. Row actions: Edit, Delete.
- **Edition dialog:** *General* tab (Name\*) and a *Features* tab (edit only) built from ABP's
  feature-management response: groups as sections, `ToggleStringValueType` as a checkbox,
  `SelectionStringValueType` as a select, `FreeTextStringValueType` as an input.

All strings in `Platform` (views) and `OpenTms` (app names) localization, `nl` and `en`.

## Cost & SLO impact

- Provisioning holds one connection to the server's `postgres` database and, while migrating,
  one pool to the new database (min 0: released after 60 s idle). It never runs in a request.
- The edition lookup for features is one cached read per tenant per 10 minutes.
- D3 caps every tenant pool at 10 per instance with no idle floor; at 1,000 tenants the idle cost
  is zero and the busy ceiling is `instances × active tenants × 10`. AP-08 will turn the numbers into
  a budget per role; until then they are configuration (`Tenancy:ConnectionPool`).

## Assumptions (PROPOSED items relied on)

- **AP-08** (pool budget as configuration): the `Tenancy:ConnectionPool` section is shaped so the
  budget can drive it later; nothing else depends on it.
- **AP-09** (more database servers): the tenant connection string is complete per tenant and the
  host comes from configuration, so a second server is a configuration change.
- **AO-06** (which pooler) stays OPEN and is not needed: when a pooler arrives, only `Host`/`Port`
  in `Tenancy:Database` change; migrations keep connecting directly.

## Packages

Platform now references these ABP packages: `Volo.Abp.Features` and `Volo.Abp.TenantManagement.Domain` (Domain),
`Volo.Abp.FeatureManagement.Domain`, `Volo.Abp.Emailing` and `Volo.Abp.UI.Navigation` (Application). All are
version 10.6.0 like the rest, open source, and already loaded by the application; no new third-party dependency.

## Notes for the reviewer

- `/CLAUDE.md` says migrations and seeding "live only" in the DbMigrator, and also that tenant
  provisioning "(create database, run migrations, seed)" is a Platform Hangfire job. This build
  follows the second sentence for **new tenants only**; the DbMigrator still owns every later
  migration of every database. `/CLAUDE.md` and `04-data-and-tenancy.md` are updated to say so.
- `IEmailSender` is the `NullEmailSender` in development: locally the invitation is logged (without
  the address) but not delivered. Wiring real mail is outside this PR.
- GDPR: the admin email lives in the tenant's own Identity user (existing art. 15/17 handling) and,
  until provisioning succeeds, as `PendingAdminEmail` on the tenant row in the host database. Deleting
  the tenant soft-deletes that row; it is not stored elsewhere.
- `AdministrationPermissionDataSeedContributor` (existing) now saves the unit of work before it grants:
  ABP's own permission seeder may have added the same grants unsaved, and in the host process it runs
  first, so a fresh tenant database failed on the unique index. Found while building this.
- The edition cache (`TenantEditionStore`) is per instance today (AP-13): another instance sees an
  edition change within its ten-minute expiry.
