# Design: Administration app — user and role management (#8)

**PBI:** https://github.com/marinusbrink/opentms-next/issues/8
**Status:** draft
**Date:** 2026-08-14

> **DEVIATION(constitution-4): bootstrap-deadlock justification, approved in issue #8
> comments by PO (marinusbrink).** The Administration app is baseline platform
> capability — a default-off per-tenant feature flag creates a bootstrap deadlock:
> activating flags or users for a new tenant requires the very administration the flag
> would hide. Access control is already enforced through ABP permissions, which is the
> appropriate gate for admin capability. Deviation is scoped to the app shell and the
> initial two views (Users, Roles); future optional admin views (e.g. audit-log UI) get
> their own flag decision.

---

## Domain impact

**Module affected: Platform only.** No business-domain module is touched. The
dependency arrow points strictly downward.

This PBI adds a UI layer on top of ABP Identity (users and roles). ABP Identity is
already part of the ABP framework scaffold in the Platform module — no new entities or
schemas are introduced.

### Backend — new files

All files live inside the Platform module.

**`OpenTms.Platform.Application.Contracts/Administration/`**

| Type | Name | Notes |
|---|---|---|
| C# record | `AdministrationUserRowDto` | Grid row projection for a user: `Id`, `UserName`, `Email`, `Name`, `Surname`, `IsActive`, `RoleNames`, `CreationTime` |
| C# record | `AdministrationUserCreateDto` | Create form payload: `UserName`, `Email`, `Name`, `Surname`, `Password`, `RoleNames` |
| C# record | `AdministrationUserUpdateDto` | Edit form payload (no password): `UserName`, `Email`, `Name`, `Surname`, `IsActive`, `RoleNames` |
| C# record | `AdministrationResetPasswordDto` | `{ NewPassword: string }` — explicit reset action |
| C# record | `BulkDeleteUsersRequestDto` | Wraps `GridSelectionDto` from design #6 |
| C# record | `BulkDeleteUsersResponseDto` | `{ DeletedCount, SkippedRows: SkippedRowDto[] }` |
| C# record | `SkippedRowDto` | `{ Id, UserName, Reason }` — reason is a localized key |
| C# record | `AdministrationRoleRowDto` | Grid row projection for a role: `Id`, `Name`, `IsDefault`, `IsPublic`, `UserCount`, `IsStatic` |
| C# record | `AdministrationRoleCreateUpdateDto` | Create and edit payload for roles: `Name`, `IsDefault`, `IsPublic` |
| C# record | `RoleDeleteCheckDto` | Body of the 409 response: `{ UserCount, RoleName }` |
| C# interface | `IUserAppService` | Users CRUD + grid + bulk delete + reset password |
| C# interface | `IUserRoleAppService` | Roles CRUD + grid + delete-with-force |

**`OpenTms.Platform.Application.Contracts/Permissions/`**
(extends existing `PlatformPermissions.cs` and `PlatformPermissionDefinitionProvider.cs`)

| Constant | Value | Side |
|---|---|---|
| `PlatformPermissions.Administration.Users` | `Platform.Administration.Users` | Both |
| `PlatformPermissions.Administration.Users.Create` | `Platform.Administration.Users.Create` | Both |
| `PlatformPermissions.Administration.Users.Update` | `Platform.Administration.Users.Update` | Both |
| `PlatformPermissions.Administration.Users.Delete` | `Platform.Administration.Users.Delete` | Both |
| `PlatformPermissions.Administration.Users.BulkDelete` | `Platform.Administration.Users.BulkDelete` | Both |
| `PlatformPermissions.Administration.Users.ResetPassword` | `Platform.Administration.Users.ResetPassword` | Both |
| `PlatformPermissions.Administration.Roles` | `Platform.Administration.Roles` | Both |
| `PlatformPermissions.Administration.Roles.Create` | `Platform.Administration.Roles.Create` | Both |
| `PlatformPermissions.Administration.Roles.Update` | `Platform.Administration.Roles.Update` | Both |
| `PlatformPermissions.Administration.Roles.Delete` | `Platform.Administration.Roles.Delete` | Both |

Permissions are defined for **both** `MultiTenancySides.Host | MultiTenancySides.Tenant`
so host admins and tenant admins each receive the appropriate subset via role grants.
ABP Identity's built-in tenant-awareness scopes the underlying data automatically
(tenant admin sees only their tenant's users; host admin sees host-level users).

**`OpenTms.Platform.Application/Administration/`**

| Type | Name | Notes |
|---|---|---|
| C# class | `UserAppService` | Implements `IUserAppService`; delegates to `IIdentityUserAppService` with added business-rule guards |
| C# class | `UserRoleAppService` | Implements `IUserRoleAppService`; delegates to `IIdentityRoleAppService` |

### Frontend — new files

**`src/apps/admin/`**

| File | Purpose |
|---|---|
| `index.tsx` | Exports `AdministrationApp`; top-level route wrapper with tab nav |
| `users/UsersView.tsx` | Users grid screen |
| `roles/RolesView.tsx` | Roles grid screen |
| `components/UserFormDialog.tsx` | Shared create/edit dialog — create includes password field, edit omits it |
| `components/RoleFormDialog.tsx` | Create/edit dialog for roles (name, isDefault, isPublic) |
| `components/ResetPasswordDialog.tsx` | Reset password action dialog |
| `components/BulkDeleteUsersDialog.tsx` | Bulk-delete confirmation + skipped-rows result |
| `components/DeleteRoleConfirmDialog.tsx` | Role delete with user-count impact warning |

**`src/components/ui/`** (shared component library)

| File | Purpose |
|---|---|
| `role-multi-select.tsx` | Reusable role multi-select dropdown — fetches roles via `POST .../roles/grid` and renders as a multi-select; added to the shared library so future forms needing role assignment can reuse it |

**`src/domains/platform/`** (new domain folder for Platform)

| File | Purpose |
|---|---|
| `administration-users.ts` | `useUsersGrid`, `useCreateUser`, `useUpdateUser`, `useDeleteUser`, `useBulkDeleteUsers`, `useResetPassword` hooks |
| `administration-roles.ts` | `useRolesGrid`, `useCreateRole`, `useUpdateRole`, `useDeleteRole` hooks |

All hooks use TanStack Query and the generated typed API client. No hand-written fetches.

### Config changes

**`frontend/src/app/apps.config.ts`**
- Add `"Platform"` to the `DomainName` union type (purely additive — no existing value changes).
- Add the Administration app entry to `APPS`.

**`frontend/src/apps/registry.tsx`**
- Import `AdministrationApp` from `@/apps/admin`.
- Add `admin: AdministrationApp` to the `components` record.

### Events

None. User and role management is a local administrative action. No cross-domain events
are needed; other domains react to ABP's built-in user/role lifecycle hooks if needed.

---

## API contract

Base route: `/api/platform/administration`

ABP's dynamic controller generation maps the app service methods to HTTP automatically.
Non-conventional methods carry explicit `[HttpPost]` / `[HttpDelete]` route attributes
on the app service interface.

---

### Users

#### `POST /api/platform/administration/users/grid`

Grid block fetch for the users list.

- **Auth:** `Platform.Administration.Users`
- **Request:** `GridRequest` (from design #6; `wildcardSearch` maps to ABP Identity's
  `filter` parameter; `sortModels[0]` maps to `sorting`; paging via `startRow`/`endRow`)
- **Supported sort columns:** `userName`, `email`, `name`, `surname`, `creationTime`
- **Supported column filters:** `userName` (text/contains), `email` (text/contains)
- **Response 200:**
```json
{
  "rows": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "userName": "j.vries",
      "email": "j.vries@example.nl",
      "name": "Jan",
      "surname": "de Vries",
      "isActive": true,
      "roleNames": ["Admin", "Planner"],
      "creationTime": "2026-01-15T09:34:00Z"
    }
  ],
  "totalCount": 42,
  "filteredCount": 42
}
```

`filteredCount` equals `totalCount` when no filter or wildcard is active. ABP Identity's
`GetListAsync` provides `totalCount`; `filteredCount` is obtained via a second count query
with the filter applied. If the second query would exceed 300 ms budget at the expected
volume (tens of users), the app service may return `totalCount` for `filteredCount` with
a note logged at debug level — acceptable given the small user volumes stated in the PBI.

---

#### `POST /api/platform/administration/users`

Create a new user.

- **Auth:** `Platform.Administration.Users.Create`
- **Request:**
```json
{
  "userName": "j.vries",
  "email": "j.vries@example.nl",
  "name": "Jan",
  "surname": "de Vries",
  "password": "SecureP@ss1",
  "roleNames": ["Planner"]
}
```
- **Response 200:** `AdministrationUserRowDto` (the created user)
- **Validation:** userName required, unique within tenant; email required, valid format;
  password required, must satisfy ABP Identity password policy.
- **Response 422:** validation failure with field-level errors.

---

#### `PUT /api/platform/administration/users/{id}`

Update an existing user. Password is **not** a field here.

- **Auth:** `Platform.Administration.Users.Update`
- **Request:**
```json
{
  "userName": "j.vries",
  "email": "j.vries@example.nl",
  "name": "Jan",
  "surname": "de Vries",
  "isActive": true,
  "roleNames": ["Planner", "Dispatcher"]
}
```
- **Business rules enforced:**
  - Removing the admin role from a user who is the last holder → `400 Bad Request`
    with `{ "errorCode": "Administration:LastAdminRoleHolder" }`.
  - Assigning a role that does not exist → `422` validation error.
- **Response 200:** updated `AdministrationUserRowDto`.

---

#### `DELETE /api/platform/administration/users/{id}`

Delete a single user.

- **Auth:** `Platform.Administration.Users.Delete`
- **Business rules enforced:**
  - Self-deletion (`currentUser.Id == id`) → `400` with
    `{ "errorCode": "Administration:SelfDeletionNotAllowed" }`.
  - Last holder of the admin role → `400` with
    `{ "errorCode": "Administration:LastAdminRoleHolder" }`.
- **Response 204:** deleted.
- **Response 400:** rule violation (see above).
- **Response 404:** user not found (concurrent deletion — fail gracefully).

---

#### `POST /api/platform/administration/users/bulk-delete`

Delete multiple users in one call. Applies all business rules per row and reports skipped
rows with the reason.

- **Auth:** `Platform.Administration.Users.BulkDelete`
- **Request:**
```json
{
  "selection": {
    "mode": "Explicit",
    "explicitIds": [
      "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "7cb12f90-1122-4812-aabc-3d963f00bb12"
    ],
    "filterRequest": null,
    "excludedIds": []
  }
}
```
  When `mode` is `FilterBased`, the app service re-executes the filter to resolve the
  affected user ids server-side before applying rules.

- **Response 200:**
```json
{
  "deletedCount": 4,
  "skippedRows": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "userName": "admin",
      "reason": "Administration:LastAdminRoleHolder"
    },
    {
      "id": "7cb12f90-1122-4812-aabc-3d963f00bb12",
      "userName": "j.vries",
      "reason": "Administration:SelfDeletionNotAllowed"
    }
  ]
}
```

`reason` values are localization keys; the frontend resolves them via the localization
resource. An already-deleted user (concurrent deletion) is treated as successfully
deleted and counted in `deletedCount`.

- **Idempotency:** the operation is idempotent. Re-running the same selection produces
  the same outcome (rows already deleted → counted as deleted, rules re-evaluated on
  remaining rows).

---

#### `POST /api/platform/administration/users/{id}/reset-password`

Admin sets a new password for a user. The user's current password is not required.

- **Auth:** `Platform.Administration.Users.ResetPassword`
- **Request:**
```json
{
  "newPassword": "NewP@ss123"
}
```
- **Validation:** password must satisfy ABP Identity password policy (min length, complexity).
- **Response 204:** password changed.
- **Response 422:** password policy violation with field-level errors.

Implemented via `IIdentityUserRepository` + `UserManager.ResetPasswordAsync` with a
generated token — the ABP-idiomatic admin password-reset path that does not require the
current password.

---

### Roles

#### `POST /api/platform/administration/roles/grid`

- **Auth:** `Platform.Administration.Roles`
- **Request:** `GridRequest` (`wildcardSearch` maps to ABP Identity role name filter)
- **Supported sort columns:** `name`, `creationTime`
- **Response 200:**
```json
{
  "rows": [
    {
      "id": "5fa85f64-5717-4562-b3fc-2c963f66afa9",
      "name": "Planner",
      "isDefault": false,
      "isPublic": true,
      "userCount": 7,
      "isStatic": false
    }
  ],
  "totalCount": 5,
  "filteredCount": 5
}
```

`userCount` is resolved via a join in the app service (ABP Identity repository). At
volumes of tens of roles and tens of users, this is negligible.

---

#### `POST /api/platform/administration/roles`

- **Auth:** `Platform.Administration.Roles.Create`
- **Request:**
```json
{ "name": "Dispatcher", "isDefault": false, "isPublic": true }
```
- **Response 200:** created `AdministrationRoleRowDto`.

---

#### `PUT /api/platform/administration/roles/{id}`

- **Auth:** `Platform.Administration.Roles.Update`
- **Request:** same shape as create.
- **Response 200:** updated `AdministrationRoleRowDto`.

---

#### `DELETE /api/platform/administration/roles/{id}?force={false|true}`

Two-phase delete. Without `?force=true`, the endpoint checks whether the role has
assigned users and returns a 409 if so, giving the frontend data to show the impact
confirmation.

- **Auth:** `Platform.Administration.Roles.Delete`
- **Phase 1** (`force=false` or omitted):
  - If role has no users → delete immediately, `204`.
  - If role has users → `409 Conflict`:
    ```json
    { "roleName": "Planner", "userCount": 7 }
    ```
- **Phase 2** (`force=true`): delete the role unconditionally. ABP Identity removes
  the role from all users automatically.
- **Response 404:** role not found (concurrent deletion — fail gracefully).
- **Note:** static roles in ABP (e.g. the `admin` role, `IsStatic = true`) cannot be
  deleted and return `400` with ABP's built-in error. The frontend disables the delete
  action for static roles based on the `isStatic` field in `AdministrationRoleRowDto`.

---

### Typed client

After implementation run:
```bash
./scripts/generate-openapi.sh
cd frontend && npm run generate:client
```

All ten endpoints are exposed in `src/lib/api/client.ts`. The frontend consumes them
exclusively through the generated client wrapped in TanStack Query.

---

## Migration strategy

### Expand (this release)

1. Extend `PlatformPermissions.cs` with the `Administration` nested class and all ten
   permission constants. Extend `PlatformPermissionDefinitionProvider.cs` to register
   them under the existing `Platform` group.
2. Add `AdministrationUserRowDto`, `AdministrationUserCreateDto`,
   `AdministrationUserUpdateDto`, `AdministrationResetPasswordDto`,
   `BulkDeleteUsersRequestDto`, `BulkDeleteUsersResponseDto`, `SkippedRowDto`,
   `AdministrationRoleRowDto`, `AdministrationRoleCreateUpdateDto`, `RoleDeleteCheckDto`
   to `OpenTms.Platform.Application.Contracts/Administration/`.
3. Add `IUserAppService` and `IUserRoleAppService` to the same
   folder.
4. Add `UserAppService` and `UserRoleAppService` to
   `OpenTms.Platform.Application/Administration/`.
5. Add localization keys (nl + en) to `OpenTms.Platform.Domain.Shared/Localization/`
   (see UI design section for the full key list).
6. Regenerate the OpenAPI spec and the typed TS client.
7. Add `"Platform"` to the `DomainName` union in `frontend/src/app/apps.config.ts`.
8. Add the Administration app entry to `APPS` in `apps.config.ts`.
9. Add `AdministrationApp` to `frontend/src/apps/registry.tsx`.
10. Add `RoleMultiSelect` to `src/components/ui/role-multi-select.tsx` (shared library
    component; used by `UserFormDialog` for role assignment).
11. Implement `src/apps/admin/` (all views and dialogs) and `src/domains/platform/`
    (all query/mutation hooks).

### No EF Core migrations

ABP Identity's schema (`AbpUsers`, `AbpRoles`, `AbpUserRoles`, `AbpSettings`) already
exists in every host and tenant database. No new EF Core migration is needed.

### Contract step

Not applicable. This release is purely additive: new endpoints, new DTOs, new
permissions, new frontend app. Nothing is renamed or removed.

---

## UI design

### App entry

The Administration app tile appears in the waffle app launcher:
- **id:** `admin`
- **nameKey:** `App:Administration` (localized: nl `Beheer`, en `Administration`)
- **path:** `/admin`
- **domains:** `["Platform"]`
- **icon:** `Shield` (lucide-react)
- **tileClass:** `bg-gray-700`

`/admin` redirects to `/admin/users`. The app uses a two-tab navigation bar (inside the
app, below the global app bar): **Users** | **Roles**.

---

### Screen 1 — Users (`/admin/users`)

```
┌──────────────────────────────────────────────────────────────┐
│ [Users] [Roles]      (tab navigation, app-scoped)            │
├──────────────────────────────────────────────────────────────┤
│ [+ New User]                                                  │
│                                                               │
│ OpenTmsGrid  gridId="platform.administration.users"          │
│  [🔍 Search…                         ] [Columns] [Reset]     │
│  ☐ | Username | Email      | Name       | Roles  | Active    │
│  ────────────────────────────────────────────────────────    │
│  ☐ | j.vries  | j.vries@…  | Jan de V.  | Planner | ✓       │
│  ☐ | admin    | admin@…    | Admin User | Admin   | ✓        │
│  (infinite scroll via SSRM)                                   │
│  Footer: Totaal: 42                                           │
└──────────────────────────────────────────────────────────────┘
```

**Columns (default visible):** Username, Email, Full name (Name + Surname combined),
Roles (comma-separated role names), Active (boolean badge), Created.

**Row actions** (inline action column, always visible):
- **Edit** → opens `UserFormDialog` in edit mode (no password field)
- **Reset password** → opens `ResetPasswordDialog`
- **Delete** → inline confirmation popover ("Delete {userName}?") → `DELETE .../users/{id}`

**Bulk selection:** when ≥ 1 row is checked, a contextual bar appears above the grid:
"N users selected — [Delete selected]". Clicking "Delete selected" opens
`BulkDeleteUsersDialog`.

**Top-level action:** `[+ New User]` button (shown only when user has
`Platform.Administration.Users.Create`) → opens `UserFormDialog` in create mode.

**Permission-denied state:** if the user lacks `Platform.Administration.Users`, the tab
still appears (to signal the feature exists) but the grid area shows a permission-denied
card ("You do not have permission to view users.") — no grid rendered.

---

### Screen 2 — Roles (`/admin/roles`)

```
┌──────────────────────────────────────────────────────────────┐
│ [Users] [Roles]      (tab navigation)                        │
├──────────────────────────────────────────────────────────────┤
│ [+ New Role]                                                  │
│                                                               │
│ OpenTmsGrid  gridId="platform.administration.roles"          │
│  [🔍 Search…                         ] [Columns] [Reset]     │
│  Role name  | Default | Public | Users | (static)            │
│  ─────────────────────────────────────────────────────────   │
│  Admin      | No      | Yes    | 2     | static badge        │
│  Planner    | No      | Yes    | 7     |                     │
│  Footer: Totaal: 5                                            │
└──────────────────────────────────────────────────────────────┘
```

**Columns:** Role name, Is default (badge), Is public (badge), User count, Static (shown
as a badge, no action; no delete or edit for static roles).

**Top-level action:** `[+ New Role]` button (shown only when user has
`Platform.Administration.Roles.Create`) → opens `RoleFormDialog` in create mode.

**Row actions:**
- **Edit** → opens `RoleFormDialog` in edit mode (fields: role name, isDefault, isPublic).
  Disabled for static roles.
- **Delete** → triggers phase-1 `DELETE .../roles/{id}`. If 204 → grid refreshes. If
  409 → opens `DeleteRoleConfirmDialog` showing impact. Disabled for static roles.

**Permission-denied state:** same pattern as Users — card shown instead of grid when
`Platform.Administration.Roles` is absent.

---

### Dialogs

**`UserFormDialog`** (create and edit mode)

Create mode fields: Username\*, Email\*, First name, Surname, Password\*,
Roles (multi-select of available role names via `POST .../roles/grid` with no filter).

Edit mode fields: same minus password. Form header changes to "Edit user {userName}".

Validation: all `*` fields required, email must be valid format, password must satisfy
password-policy tooltip (shown inline).

Form submit → `POST .../users` or `PUT .../users/{id}`. On success → dialog closes,
grid invalidated. On 422 → field-level error messages shown inline.

**`RoleFormDialog`** (create and edit mode)

Create mode fields: Role name\*, Is default (checkbox), Is public (checkbox). Form header:
"New role".

Edit mode fields: same as create. Form header changes to "Edit role {name}". Disabled
entirely for static roles (edit action in the grid does not open the dialog for static
roles).

Validation: name required, max 256 characters.

Form submit → `POST .../roles` or `PUT .../roles/{id}`. On success → dialog closes, grid
invalidated. On 422 or error → inline error message shown.

---

**`ResetPasswordDialog`**

Single field: New password\* (with show/hide toggle). Submit → `POST .../users/{id}/reset-password`.
Success → success toast, dialog closes. 422 → inline validation error.

**`BulkDeleteUsersDialog`**

Step 1 — confirmation: "You are about to delete {N} users. This cannot be undone. Continue?"
with Cancel / Delete buttons.

Step 2 — result (shown after the API call returns): "Deleted {deletedCount} users." If
`skippedRows` is non-empty, a list of skipped users is shown:
```
Skipped (2):
  admin — Last admin role holder
  j.vries — Own account cannot be deleted
```
Reason strings are resolved from the localization resource using the `reason` key from
`SkippedRowDto`. Close button dismisses, grid is refreshed.

**`DeleteRoleConfirmDialog`**

"The role '{roleName}' is currently assigned to {userCount} users. Deleting this role
removes it from those users. This action cannot be undone."
Cancel / Delete buttons. Confirm → `DELETE .../roles/{id}?force=true`. Grid refreshes.

---

### States (per screen)

| State | Trigger | Rendering |
|---|---|---|
| Loading | First SSRM block in flight | AG Grid built-in loading overlay |
| Loaded | Block fetch succeeded | Rows and footer visible |
| Empty (no filter) | `totalCount === 0`, no filter | "No users found" overlay + `[+ New User]` call-to-action (Users screen); "No roles found" + `[+ New Role]` (Roles screen) |
| Empty (filtered) | `filteredCount === 0`, filter active | "No matches — filter is active" + [Clear filters] button (inherited from `OpenTmsGrid`) |
| Block error | Block fetch failed | Auto-retry then manual retry (inherited from `OpenTmsGrid`) |
| Permission-denied | User lacks the view permission | Full-width permission-denied card; no grid rendered |

---

### Component reuse

| Component needed | Source |
|---|---|
| `OpenTmsGrid<TRow>` | design #6 (`src/components/ui/opentms-grid.tsx`) |
| `Button` | `src/components/ui/button.tsx` |
| `Input` | `src/components/ui/input.tsx` |
| `Label` | `src/components/ui/label.tsx` |
| Dialog (modal) | `@base-ui/react` — `Dialog` component with `render` prop |
| Popover (inline confirm) | `src/components/ui/popover.tsx` |
| `Separator` | `src/components/ui/separator.tsx` (tab nav divider) |
| Multi-select for roles in form | New component `role-multi-select.tsx` in `src/components/ui/` (Base UI, no new external dep) |
| Toast / notification | Base UI `Popup` or equivalent — same component established by design #6 |

`RoleMultiSelect` is added to `src/components/ui/` (not a one-off in the app folder)
so future forms that need role assignment can reuse it.

---

### Optimistic updates

No optimistic updates are designated for this design. All create, update, delete,
bulk-delete, and reset-password operations involve server-side business-rule validation
(self-delete guard, last-admin guard, role-has-users check, password policy). At admin
volumes (tens of users per tenant), a brief loading indicator with a grid refresh on
success is the correct trade-off — an optimistic update that silently reverts on rule
violation creates worse UX than an explicit round-trip.

Exception: grid-settings save and reset are optimistic by inheritance from the
`OpenTmsGrid` component per design #6.

---

### Localization keys

Added to `OpenTms.Platform.Domain.Shared/Localization/` (both `nl` and `en`):

| Key | nl | en |
|---|---|---|
| `App:Administration` | Beheer | Administration |
| `Administration:Users` | Gebruikers | Users |
| `Administration:Roles` | Rollen | Roles |
| `Administration:NewUser` | Nieuwe gebruiker | New user |
| `Administration:NewRole` | Nieuwe rol | New role |
| `Administration:EditUser` | Gebruiker bewerken | Edit user |
| `Administration:EditRole` | Rol bewerken | Edit role |
| `Administration:ResetPassword` | Wachtwoord opnieuw instellen | Reset password |
| `Administration:DeleteUser` | Gebruiker verwijderen | Delete user |
| `Administration:DeleteRole` | Rol verwijderen | Delete role |
| `Administration:BulkDelete` | Geselecteerden verwijderen | Delete selected |
| `Administration:SelfDeletionNotAllowed` | Eigen account kan niet worden verwijderd | Own account cannot be deleted |
| `Administration:LastAdminRoleHolder` | Laatste beheerder kan niet worden verwijderd | Last admin role holder cannot be deleted or removed |
| `Administration:ConfirmBulkDelete` | U staat op het punt {0} gebruikers te verwijderen. Dit kan niet ongedaan worden gemaakt. | You are about to delete {0} users. This cannot be undone. |
| `Administration:RoleHasUsersWarning` | De rol '{0}' is toegewezen aan {1} gebruikers. Het verwijderen ervan verwijdert de rol bij alle gebruikers. | The role '{0}' is currently assigned to {1} users. Deleting it removes the role from those users. |
| `Administration:Deleted` | Verwijderd: {0} | Deleted: {0} |
| `Administration:Skipped` | Overgeslagen: {0} | Skipped: {0} |
| `Administration:PermissionDenied` | U heeft geen toestemming voor deze weergave. | You do not have permission to view this. |
| `Administration:StaticRole` | Systeemrol | System role |
| `Permission:Administration.Users` | Gebruikers beheren | Manage users |
| `Permission:Administration.Users.Create` | Gebruikers aanmaken | Create users |
| `Permission:Administration.Users.Update` | Gebruikers bewerken | Edit users |
| `Permission:Administration.Users.Delete` | Gebruikers verwijderen | Delete users |
| `Permission:Administration.Users.BulkDelete` | Meerdere gebruikers verwijderen | Bulk delete users |
| `Permission:Administration.Users.ResetPassword` | Wachtwoord opnieuw instellen | Reset user password |
| `Permission:Administration.Roles` | Rollen beheren | Manage roles |
| `Permission:Administration.Roles.Create` | Rollen aanmaken | Create roles |
| `Permission:Administration.Roles.Update` | Rollen bewerken | Edit roles |
| `Permission:Administration.Roles.Delete` | Rollen verwijderen | Delete roles |

---

## Test risk analysis

**Platform-layer rule:** every change in this PBI touches the horizontal Platform layer.
All parts carry **critical** as the baseline risk class (design §3.1). The table
below shows where justification raises or maintains that class.

| Part | Risk class | Rationale |
|---|---|---|
| Permission definitions (new constants + provider) | **Critical** | Incorrect permission definitions can silently grant or deny access across the entire tenant fleet; any misconfiguration is a security issue |
| `UserAppService` — self-delete guard | **Critical** | A failing guard allows an admin to delete their own account, causing an irrecoverable lockout |
| `UserAppService` — last-admin-role guard | **Critical** | A failing guard allows removal of the last admin, causing tenant lockout |
| `UserAppService` — bulk delete with skip logic | **High** | Incorrect skip reporting misleads the admin; wrong iteration deletes rows that should be skipped |
| `UserAppService` — reset password path | **High** | Incorrect use of `UserManager` token generation could create a security bypass; must use ABP-idiomatic admin reset, not the self-service path |
| `UserRoleAppService` — role-delete two-phase (409 / force) | **High** | A skipped phase-1 check could delete a role from users without confirmation; concurrent deletion after phase-1 must be handled gracefully |
| `BulkDeleteUsersRequestDto` with `GridSelectionDto` | **High** | FilterBased mode re-resolves the filter server-side; pagination edge cases (row added between select-all and delete) must be handled |
| Frontend permission-denied state | **High** | A missing permission check in the view renders admin data to unauthorized users in the browser |
| Tenant isolation | **Critical** | ABP Identity is tenant-scoped via `ICurrentTenant`; any call that bypasses this (e.g. host-context operations on tenant endpoints) is a cross-tenant leak |
| Localization keys | **Low** | Additive only; a wrong string is visible but not a data or security issue |
| UI states (loading / empty / error / permission-denied) | **Medium** | Functional but not data-sensitive; missing states degrade UX |

---

## Flag & rollout plan

**No feature flag.** `DEVIATION(constitution-4)` approved by PO (issue #8): the
Administration app is baseline platform capability. A default-off flag creates a
bootstrap deadlock for newly provisioned tenants. Access is controlled by the new
`Platform.Administration.*` permissions granted to the admin role.

Rollout:

1. Deploy Platform module changes (permissions, app services, localization).
2. Deploy frontend changes (apps.config.ts, registry, `src/apps/admin/`,
   `src/domains/platform/`).
3. Verify both grids load correctly on the health tenant.
4. Confirm the `admin` role has the new `Platform.Administration.*` permissions seeded
   (handled by ABP's permission seeder, or manually grant via the permission management
   UI if the seeder does not include them).
5. No per-tenant activation step beyond confirming the admin role grant.

Future optional admin views (e.g. audit-log UI) get their own flag decision at their own
design — this deviation is explicitly scoped to the app shell and the Users and Roles
views only.

---

## Cost & SLO impact

**GCP impact:** negligible.

| Resource | Impact |
|---|---|
| Cloud SQL (ABP Identity schema) | No new table. Reads from existing `AbpUsers`, `AbpRoles`, `AbpUserRoles`. At tens of users per tenant the query volume is trivial and fully within ABP's existing indices. |
| Cloud Run | No new min instances. User/role CRUD is infrequent administrative traffic. |
| Egress | Grid rows are small (< 2 KB per block of 50 rows). Negligible. |
| External API calls | None. |
| Per-tenant margin | Unaffected — no paid external service involved. |

**Performance budgets touched:**

- **Interactive reads:** `POST .../users/grid` and `POST .../roles/grid` — ABP Identity's
  `GetListAsync` is indexed on `(TenantId, UserName)` / `(TenantId, NormalizedName)`.
  At tens of records per tenant, p95 is expected well within the 300 ms budget. No
  additional indices needed beyond the ABP scaffold.
- **Mutations:** create/update/delete/bulk-delete/reset-password — all delegate to
  `IIdentityUserAppService` or `UserManager`; single-row operations expected p95 < 500 ms.
  Bulk delete iterates per-row but at tens of users the total stays within budget;
  if tenant user count ever grows to thousands, bulk delete must become asynchronous
  (Hangfire job) — flagged as a future concern, not in scope here.
- **Web frontend bundle:** `src/apps/admin/` is a new app chunk. It must be lazy-loaded
  via `React.lazy` / dynamic `import()` at the route level (consistent with the pattern
  expected from design #6 for `OpenTmsGrid`). CI bundle-size budget check must not
  regress any other route.
- **Availability SLO (99.9 %):** user/role management is administrative traffic, not a
  core flow (order creation, invoicing, planning). A transient unavailability of the
  Administration app does not consume the availability budget for core flows.

---

## Assumptions

1. ABP Identity's `IIdentityUserAppService` and `IIdentityRoleAppService` are available
   as injectable dependencies inside `OpenTms.Platform.Application` — Platform depends on
   ABP Identity (a framework module), which is already a dependency of the ABP scaffold.
   If Platform's module DI does not currently declare the ABP Identity module as a
   dependency, the implementer adds it and documents it.

2. The "admin role" for the last-holder guard is identified as the role where
   `IsStatic = true AND Name = "admin"` (ABP's built-in admin role). If the deployment
   uses a different name or identification strategy, the implementer adjusts the guard
   and notes the deviation in a code comment referencing this design.

3. ABP Identity's `UserManager.ResetPasswordAsync` (with a generated token from
   `UserManager.GeneratePasswordResetTokenAsync`) is the correct admin-side password-
   reset mechanism — it does not require the current password. If this API behaves
   differently in the ABP OpenIddict integration, the implementer must find the
   equivalent ABP-idiomatic path and flag any deviation.

4. The `isStatic` flag on `IdentityRole` is exposed through ABP's `IIdentityRoleAppService`
   or is accessible from the role entity, so `AdministrationRoleRowDto.IsStatic` can be
   populated without a raw EF Core query. If not, the implementer queries the role entity
   directly via `IIdentityRoleRepository`.

5. ABP Identity automatically removes a role from all users when the role is deleted
   (cascade behavior in the EF Core configuration). The implementer confirms this in the
   ABP version used before removing the `force=true` re-check.

6. The `OpenTmsGrid` component from design #6 is merged and deployed before this PBI's
   implementation begins. If design #6 is not merged, the implementation must wait or
   implement both in the same release (coordinated with the implementer of design #6).

7. At the expected volumes (tens of users, tens of roles per tenant), ABP Identity's
   default paging + counting is within the 300 ms interactive-read budget without
   additional optimization. If a tenant grows beyond ~1 000 users, the grid endpoint
   needs a dedicated count query strategy — deferred to a performance PBI at that time.

8. The `RoleMultiSelect` component in `UserFormDialog` fetches available roles via a
   paginated call to `POST .../roles/grid` with a large `endRow` (e.g. 200) on dialog
   open. At tens of roles this is acceptable. If the role count grows, a search-as-you-
   type pattern is needed — deferred.

9. The `App:Administration` localization key and the two tab keys (`Administration:Users`,
   `Administration:Roles`) are added to the `OpenTms.Platform.Domain.Shared/Localization/`
   JSON resources. The implementer confirms the resource files for `nl` and `en` exist
   there and adds to both.

10. The seeder (in `OpenTms.DbMigrator`) grants the new `Platform.Administration.*`
    permissions to the default `admin` role for both host and tenant databases. The
    implementer adds seeder entries to ensure a freshly migrated database has admin
    access to the new views without manual permission management.

---

## Security quickscan

### New ABP permission definitions

Ten new permission constants under the `Platform` group (see Domain impact section).
All are defined via `PlatformPermissionDefinitionProvider` — no hardcoded role or id
checks anywhere in the app service or controller layer.

### Input validation boundaries

| Input | Validation | Location |
|---|---|---|
| `userName` | Required; `[MaxLength(256)]`; uniqueness checked by ABP Identity | DTO attribute + ABP Identity |
| `email` | Required; `[EmailAddress]`; `[MaxLength(256)]` | DTO attribute |
| `name` / `surname` | `[MaxLength(64)]` each | DTO attribute |
| `password` (create) | Required; ABP Identity password policy (min length, complexity) | ABP `UserManager` |
| `newPassword` (reset) | Required; same password policy | ABP `UserManager` |
| `roleNames` | Each name `[MaxLength(256)]`; existence validated in app service | App service |
| `GridRequest` | Inherited from design #6 (startRow/endRow bounds, wildcardSearch max 200 chars, filter max 500 chars) | Design #6 contract |
| `selection.filterRequest` (bulk delete) | Same `GridRequest` validation | Design #6 contract |
| `{id}` (all path params) | GUID format; ABP model binding rejects non-GUIDs | ABP controller |
| Role `name` | Required; `[MaxLength(256)]`; checked by ABP Identity | DTO attribute + ABP Identity |

### Attack surface changes

- 10 new HTTP endpoints, all protected by specific `Platform.Administration.*` permissions.
- No anonymous or unauthenticated access path.
- No new cross-tenant data access: ABP Identity's `ICurrentTenant` scopes all queries;
  host-context operations (host admin viewing host users) require the caller to be in
  host context, which ABP enforces.
- Bulk-delete `FilterBased` mode: the server re-resolves the filter from the stored
  `GridRequest` — it does not trust client-supplied id lists for the filter path. SQL
  injection is not a risk because the filter is applied through ABP Identity's
  `GetListAsync` with EF Core parameterized queries.
- The reset-password endpoint (`POST .../users/{id}/reset-password`) must verify that
  `{id}` belongs to the current tenant before resetting. ABP Identity's `UserManager`
  with `ICurrentTenant` scope handles this; the app service must not call
  `FindByIdAsync` with disabled data filter.

### GDPR (art. 15 / art. 17)

**Personal data introduced by this PBI:**

| Field | Location | Art. 15 (export) | Art. 17 (erasure) |
|---|---|---|---|
| `UserName` | `AbpUsers` (ABP Identity) | Viewable in the admin UI; a formal machine-readable export endpoint is **not in scope** for this PBI — a future data-export PBI must address art. 15 for admin records | Fulfilled by user deletion via this UI: `DELETE .../users/{id}` removes the `AbpUsers` record and all cascade-linked records (`AbpUserRoles`, `AbpUserClaims`, `AbpUserLogins`, `AbpUserTokens`, `AbpSettings` at user scope) |
| `Email` | `AbpUsers` (ABP Identity) | Same as above | Same as above |
| `Name` / `Surname` | `AbpUsers` (ABP Identity) | Same as above | Same as above |

**No PII in logs** (constitution rule 5): the app service logs only entity ids and
operation outcomes. User names and email addresses are never written to structured logs.
Example correct log: `LogInformation("User {UserId} deleted by {ActorId}", userId, currentUser.Id)`.

**Data breach surface:** the Administration app shows user names and email addresses in
the browser. Access is gated by `Platform.Administration.Users`; a role misconfiguration
that grants this permission to a non-admin user is the primary risk vector. The
implementer must confirm the seeder grants this permission **only** to the `admin` role,
not to `Default` or any other pre-seeded role.
