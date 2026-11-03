# Design 73: Platform — role and user permissions screen

**PBI:** Marinus, 2026-11-03 (chat): "a screen where an administrator changes a role's permissions". Design 71 listed it
under *Out of scope*.
**Status:** approved (PO 2026-11-03, answers in chat). Risk class **critical** (Platform change, authorization in every
tenant).
**Date:** 2026-11-03

## PO decisions (2026-11-03)

1. **Roles and users.** Grants change on a role (provider `R`) and, as the rare exception, directly on a user
   (provider `U`). Role-based stays the default; the manual says so.
2. **Two new permissions**, defined in Platform `Application.Contracts/Permissions/`:
   `Platform.Administration.Roles.ManagePermissions` guards a role's grants,
   `Platform.Administration.Users.ManagePermissions` guards a user's direct grants.
3. **No feature flag** (constitution rule 4 decided by the PO for this change, as for design 71): the screen is behind
   the two new permissions, which only `admin` holds.
4. **Predefined (static) roles are editable.** The seed of design 71 never adds back a grant that was removed, so a
   tenant's change sticks. The dialog says that the role is predefined. The `admin` role stays **read-only**: nobody
   can remove the administrators' own access.
5. **Side only.** The dialog shows what ABP's endpoint returns for the current side (host or tenant). ABP also hides a
   permission whose feature requirement is off, but no OpenTMS permission has one today; tying permissions to app
   features is a follow-up (see *Out of scope*).
6. **ABP's own permission groups are hidden** (`AbpIdentity`, `AbpTenantManagement`, `FeatureManagement`,
   `SettingManagement`): no OpenTMS screen sits behind them. Grants in those groups are left as they are.

## Domain impact

**Module:** Platform. No tables, no migration. No new endpoint: the screen uses ABP's permission-management API
(`GET`/`PUT /api/permission-management/permissions?providerName=…&providerKey=…`), already in the committed contract and
the generated client. Flow: none of the three flows of `02-flows.md`; this is Platform set-up data, like design 71.

- **Permission definitions** (`PlatformPermissionDefinitionProvider`, both sides): `ManagePermissions` as a child of
  `Platform.Administration.Roles` and of `Platform.Administration.Users`. Localized as
  `Permission:Administration.Roles.ManagePermissions` and `Permission:Administration.Users.ManagePermissions` (nl, en).
- **Provider policies** (`PlatformApplicationModule`, `PostConfigure<PermissionManagementOptions>` so it wins over the
  Identity module's own `AbpIdentity.*.ManagePermissions`): `ProviderPolicies["R"]` = the role permission,
  `ProviderPolicies["U"]` = the user permission. ABP checks the policy on every read and write of that provider.
- **Seed:** `AdministrationPermissionDataSeedContributor` grants both to `admin` (ABP's own seed also gives `admin`
  every permission of its side). No predefined role of design 71 gets them (design 71: no role holds user or role
  administration).
- **`admin` is read-only, enforced on the server:** `OpenTmsPermissionAppService` replaces ABP's `PermissionAppService`
  (Platform.Application) and rejects `UpdateAsync` for provider `R`, key `admin` with
  `BusinessException(OpenTms.Platform:AdminRolePermissionsReadOnly)`, mapped to HTTP 409 in `OpenTmsHttpApiModule` (ABP's
  default 403 would read as a missing permission). Everything else is ABP's behavior.
- **What ABP already enforces (kept, tested):** a permission of the other side cannot be granted (`PermissionManager`
  rejects it); a caller outside the `admin` role can only grant or revoke permissions they hold themselves
  (`isEditable` false in the response for the others); writes go to the current tenant's `PermissionGrants` through
  the tenant's connection (constitution rule 1).

## Screens

- **Roles view:** row action **Permissions** (icon `ShieldCheck`; not in the UI-ACT-006 table, no listed action fits),
  shown only with `Platform.Administration.Roles.ManagePermissions` (UI-PERM-002). For `admin` it is disabled with the
  reason "the administrator role always has every permission" (business state, UI-PERM-005).
- **Users view:** row action **Permissions**, shown only with `Platform.Administration.Users.ManagePermissions`.
- **`PermissionsDialog`** (`apps/admin/components/`, shared by the two views of the Administration app):
  `EntityDialog` `variant="editor"`, `size="md"`, title "Permissions" + the role's localized name
  (`roleDisplayName`) or the user name, ribbon with Save (UI-ACT-002), dirty guard (UI-DLG-008), busy guard
  (UI-DLG-010), errors in an `Alert` at the top (UI-FB-004), success closes with a toast (UI-FB-002).
  - The body is one scrolling section (UI-DLG-006) with a `FormSection` per module group, in the order ABP returns
    them, titled with the group's localized name. Each section has an "All" checkbox (checked, unchecked or mixed).
  - Permissions are a tree: a child is indented under its parent (ABP's `parentName`). Checking a child checks its
    parent; unchecking a parent unchecks its children (ABP grants a child only together with its parent).
  - A search field filters the permissions by localized name.
  - A predefined role shows an `Alert` (UI-FB-008): its grants can be changed, and a later release does not add back
    a removed grant.
  - User mode: a permission the user holds through a role is shown checked and disabled with "Through role: …"
    (localized role names). Only direct grants are changed. The `Alert` says to prefer roles.
  - A permission the caller may not change (`isEditable` false) is disabled.
  - Save sends only the changed permissions in one `PUT`. Hooks in `domains/platform/administration-permissions.ts`
    (`usePermissionGrants`, `useUpdatePermissionGrants`), TanStack Query over the generated client.

## Permissions and features (in full)

New permissions:

- `Platform.Administration.Roles.ManagePermissions` (host and tenant; child of `Platform.Administration.Roles`)
- `Platform.Administration.Users.ManagePermissions` (host and tenant; child of `Platform.Administration.Users`)

Granted by the seed to: `admin` only (host and every tenant).

Features: **none added or changed.** The views stay under the existing app features `Apps.Administration.Users` and
`Apps.Administration.Roles`.

## Localization (nl, en; Platform resource)

`Permission:Administration.Roles.ManagePermissions`, `Permission:Administration.Users.ManagePermissions`,
`OpenTms.Platform:AdminRolePermissionsReadOnly`, `Administration:Permissions`, `Administration:PermissionsSaved`,
`Administration:PermissionsSearch`, `Administration:PermissionsAll`, `Administration:PermissionsNone`,
`Administration:PredefinedRolePermissionsNote`, `Administration:UserPermissionsNote`,
`Administration:GrantedThroughRole`, `Administration:AdminRolePermissionsReadOnly`.

## Out of scope

- Tying permissions to app features (`RequireFeatures`), so that a tenant without an app does not see its
  permissions. That also stops such a permission from being granted at runtime, a system-wide authorization change
  that needs its own design.
- A screen for ABP's own permission groups.
- Organisation-unit roles (ABP grants through the units' roles already follow the role grants).

## Test risk analysis (critical)

- Backend (`RolePermissionManagementTests`, SQLite):
  - the two permissions exist on both sides as children of their parents; `admin` holds them, no predefined role does;
  - the provider policies point at the new permissions: a caller with `AbpIdentity.Roles.ManagePermissions` but
    without `Platform.Administration.Roles.ManagePermissions` is refused (read and write), likewise for users;
  - a role's grant is added and removed; a user's direct grant is added and removed;
  - `admin`'s grants cannot be changed (`AdminRolePermissionsReadOnly`), its grants are unchanged afterwards;
  - a host-only permission cannot be granted inside a tenant;
  - the `GET` inside a tenant returns no host-only permission;
  - **negative tenant isolation:** tenant A removes a grant from its `Planner` role; tenant B's `Planner` and the host
    keep theirs; tenant A cannot read or change tenant B's role by name (it gets its own role of that name, never B's).
- `AdminRolePermissionsErrorCodeTests`: the refusal travels as HTTP 409.
- Frontend (vitest): the dialog hides ABP's groups, renders the tree with indentation, cascades parent/child, sends only
  the changed permissions, shows role grants disabled in user mode, the predefined-role note, the row actions follow the
  permissions and `admin` is disabled with its reason.

## Flag & rollout plan

No flag (decision 3). The first DbMigrator run after the merge grants the two permissions to `admin` everywhere.

## Cost & SLO impact

One `GET` (all permission definitions of the side, already cached by ABP) when the dialog opens and one `PUT` per save.
No background work.
