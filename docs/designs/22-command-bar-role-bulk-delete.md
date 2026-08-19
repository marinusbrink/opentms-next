# Design: Shared command bar with role bulk delete (#22)

**PBI:** https://github.com/marinusbrink/opentms-next/issues/22
**Status:** draft
**Date:** 2026-08-19

`DEVIATION(constitution-4): command-bar chrome is identical for all tenants with no new capability or backend surface — rollback is a PR revert; gate 1 must confirm. Role bulk delete falls under the same Administration bootstrap-deadlock justification as design #8; access is gated by the new Platform.Administration.Roles.BulkDelete permission — gate 1 must confirm.`

<!-- All nine sections below are mandatory (design §4.2). "Not applicable" is an
     acceptable answer only with a one-line reason; a missing section is not.
     Gate 1 approves this document by merging the PR — after that, implementers
     build exactly this, so vagueness here becomes iteration later. -->

## Domain impact

**Module affected: Platform only.** No business-domain module is touched. The
dependency arrow points strictly downward.

### Backend — new files

All files live inside the Platform module.

**`OpenTms.Platform.Application.Contracts/Administration/`** (extending design #8)

| Type | Name | Notes |
|---|---|---|
| C# record | `BulkDeleteRolesRequestDto` | Wraps `GridSelectionDto` from design #6 |
| C# record | `BulkDeleteRolesResponseDto` | `{ DeletedCount, SkippedRows: SkippedRoleDto[] }` |
| C# record | `SkippedRoleDto` | `{ Id, Name, Reason }` — reason is a localization key |

**`OpenTms.Platform.Application.Contracts/Permissions/`** (extending `PlatformPermissions.cs`)

| Constant | Value | Side |
|---|---|---|
| `PlatformPermissions.Administration.Roles.BulkDelete` | `Platform.Administration.Roles.BulkDelete` | Both |

**`OpenTms.Platform.Application/Administration/`** (extending design #8)

| Type | Change |
|---|---|
| `IUserRoleAppService` | Add `BulkDeleteAsync(BulkDeleteRolesRequestDto input)` returning `BulkDeleteRolesResponseDto` |
| `UserRoleAppService` | Implement `BulkDeleteAsync` — iterates resolved role ids; skips static roles, force-deletes all others (ABP Identity removes role from users automatically on deletion); returns counts and skipped list |

### Frontend — new and changed files

**`src/components/ui/`** (shared library)

| File | Change |
|---|---|
| `app-command-bar.tsx` | **New** `AppCommandBar` component (see UI design) |

**`src/apps/admin/components/`**

| File | Change |
|---|---|
| `BulkDeleteRolesDialog.tsx` | **New** role bulk-delete confirmation and result dialog (parallel to `BulkDeleteUsersDialog`) |

**`src/apps/admin/users/UsersView.tsx`**

- Remove: standalone `[+ New User]` button above the grid.
- Remove: inline contextual bar "N users selected — [Delete selected]" (introduced by design #8's description; implementation is replaced here).
- Add: `<AppCommandBar>` with two commands: New User (always enabled when permitted) and Delete selected (requires selection, variant destructive).

**`src/apps/admin/roles/RolesView.tsx`**

- Remove: standalone `[+ New Role]` button above the grid.
- Add: checkbox column to the `OpenTmsGrid` (`rowSelection` prop) and `onSelectionChange` wiring.
- Add: `<AppCommandBar>` with two commands: New Role (always enabled when permitted) and Delete selected (requires selection, variant destructive).
- Add: `BulkDeleteRolesDialog` triggered by the Delete selected command.

**`src/domains/platform/administration-roles.ts`** (extending design #8)

| Hook | Change |
|---|---|
| `useBulkDeleteRoles` | **New** mutation hook — `POST .../roles/bulk-delete`; on success invalidates the roles query |

**Localization** (additive, `OpenTms.Platform.Domain.Shared/Localization/`)

| Key | nl | en |
|---|---|---|
| `Administration:NSelected` | `{0} geselecteerd` | `{0} selected` |
| `Administration:ConfirmBulkDeleteRoles` | `U staat op het punt {0} rollen te verwijderen. Rollen die aan gebruikers zijn toegewezen worden van die gebruikers verwijderd. Dit kan niet ongedaan worden gemaakt.` | `You are about to delete {0} roles. Roles assigned to users will be removed from those users. This cannot be undone.` |
| `Permission:Administration.Roles.BulkDelete` | `Rollen verwijderen (meerdere)` | `Bulk delete roles` |

No cross-domain events. No new EF Core migrations. No new external dependencies.

---

## API contract

Base route: `/api/platform/administration` (established by design #8).

### `POST /api/platform/administration/roles/bulk-delete`

Deletes multiple roles in one call. Applies business rules per role and reports skipped rows with the reason.

- **Auth:** `Platform.Administration.Roles.BulkDelete`
- **Request:**
```json
{
  "selection": {
    "mode": "Explicit",
    "explicitIds": [
      "5fa85f64-5717-4562-b3fc-2c963f66afa9",
      "9ab12f90-1122-4812-aabc-3d963f00bb12"
    ],
    "filterRequest": null,
    "excludedIds": []
  }
}
```
When `mode` is `FilterBased`, the app service re-executes the role filter against ABP Identity to resolve the affected role ids server-side before applying rules.

- **Skip logic:**
  - Static roles (`IsStatic = true`): skipped, reason `Administration:StaticRole`.
  - Roles with assigned users: force-deleted (ABP Identity cascades role removal from `AbpUserRoles`); not a skip reason — counted in `deletedCount`.
  - Already-deleted roles (concurrent deletion): treated as successfully deleted, counted in `deletedCount`.

- **Response 200:**
```json
{
  "deletedCount": 3,
  "skippedRows": [
    {
      "id": "5fa85f64-5717-4562-b3fc-2c963f66afa9",
      "name": "admin",
      "reason": "Administration:StaticRole"
    }
  ]
}
```
`reason` values are localization keys; the frontend resolves them via the localization resource.

- **Idempotency:** the operation is idempotent — re-running the same selection on already-deleted roles counts them as deleted; rules re-evaluated on remaining roles.

- **Response 400:** `GridSelectionDto` validation failure (invalid shape or empty id list for Explicit mode).

### Typed client

After implementation, run `./scripts/generate-openapi.sh && cd frontend && npm run generate:client` to expose the new endpoint in `src/lib/api/client.ts`.

No existing endpoint is changed or removed.

---

## Migration strategy

### Expand (this release)

All steps are purely additive.

1. Add `PlatformPermissions.Administration.Roles.BulkDelete` constant to `PlatformPermissions.cs`.
2. Register it in `PlatformPermissionDefinitionProvider.cs` under the existing `Administration.Roles` group.
3. Add `BulkDeleteRolesRequestDto`, `BulkDeleteRolesResponseDto`, `SkippedRoleDto` to `OpenTms.Platform.Application.Contracts/Administration/`.
4. Add `BulkDeleteAsync` to `IUserRoleAppService` and implement in `UserRoleAppService`.
5. Regenerate the OpenAPI spec and the typed TS client.
6. Add `app-command-bar.tsx` to `frontend/src/components/ui/`.
7. Add `BulkDeleteRolesDialog.tsx` to `frontend/src/apps/admin/components/`.
8. Update `UsersView.tsx`: remove standalone button + inline contextual bar; add `<AppCommandBar>`.
9. Update `RolesView.tsx`: remove standalone button; add checkbox column + `onSelectionChange`; add `<AppCommandBar>` + `BulkDeleteRolesDialog`.
10. Add `useBulkDeleteRoles` hook to `administration-roles.ts`.
11. Add localization keys (nl + en) to `OpenTms.Platform.Domain.Shared/Localization/`.

### Contract step

Not applicable. This release is purely additive: one new endpoint, three new DTOs, one new permission, new frontend component and dialogs. Nothing is renamed or removed.

---

## UI design

### Structural decision: views render `AppCommandBar` themselves

The command bar is dynamic — its commands and selection state react to user interaction and cannot be expressed in a static config file. Two implementation strategies were evaluated:

- **Context injection (shell renders the slot, views inject commands via hook):** achieves full-width rendering at the shell level; adds hook stability requirements (views must `useMemo` commands to prevent infinite re-render loops) and a new context throughout the shell — disproportionate complexity for two views.
- **View renders (chosen):** each view renders `<AppCommandBar>` as the first child of its content area. The component enforces consistent visual treatment (background, border, button styles, selection count, disabled states); the view owns command semantics. This matches the existing pattern for `OpenTmsGrid` and the dialog components.

If more apps across the product adopt the command bar, context injection can be introduced in a future refactor without breaking `AppCommandBar`'s API.

### Component: `AppCommandBar`

**File:** `frontend/src/components/ui/app-command-bar.tsx`

**Props:**
```ts
interface AppCommandBarProps {
  commands: AppCommandBarCommand[];
  selectionCount?: number;  // omit when the view has no selection context
  className?: string;
}

interface AppCommandBarCommand {
  id: string;
  labelKey: string;          // localization key
  icon?: LucideIcon;
  variant?: 'default' | 'destructive';
  requiresSelection?: boolean; // disabled when selectionCount === 0 or undefined
  disabled?: boolean;          // additional override (e.g. mutation in-flight)
  onClick: () => void;
}
```

**Visual spec:**
```
┌─────────────────────────────────────────────────────────────────────┐
│  [+ New User]   ···············  3 selected  [🗑 Delete selected]  │
│  bg-[#F8F9FA] dark:bg-card  border border-border rounded-md h-12  │
└─────────────────────────────────────────────────────────────────────┘
                 mb-4 below the bar
```

- **Background:** `bg-[#F8F9FA] dark:bg-card` (matching the nav pane treatment from design #21).
- **Border:** `border border-border rounded-md` — creates a card-like surface distinct from the grid card below.
- **Height:** `h-12` — matches app bar height for visual rhythm.
- **Padding:** `px-4` horizontal.
- **Layout:** `flex items-center gap-3`.
- **Margin below:** `mb-4` separating the bar from the grid card.
- **Empty-bar policy:** the bar **always renders** regardless of whether commands are empty or selection is zero. This prevents content-pane height jumps. When all commands are disabled (empty grid, no selection), the bar renders at full height with all buttons in disabled state.

**Layout within bar:**
- Left side: commands where `requiresSelection` is `false` or `undefined` (primary actions — New).
- Spacer (`flex-1`) dividing primary from selection-scoped actions.
- Right side: `selectionCount` badge (shown only when `selectionCount > 0`) + commands where `requiresSelection` is `true` (Delete selected).

**Selection count badge:**
- Text: `t('Administration:NSelected', [selectionCount])` (e.g. "3 selected").
- Shown only when `selectionCount > 0`.
- Typography: `text-sm text-muted-foreground`.

**Commands:**
- Rendered as `Button` from `src/components/ui/button.tsx`.
- `variant="default"` for primary commands (New).
- `variant="destructive"` for delete commands.
- A command with `requiresSelection: true` is `disabled` when `(selectionCount ?? 0) === 0`.
- A command with `disabled: true` (prop) is always disabled regardless of selection.
- Icons rendered at 16 px via Lucide (`size={16}`).
- Button label: `t(command.labelKey)`.

**Keyboard / a11y:**
- Each button is reachable via `Tab` in DOM order.
- Disabled buttons carry `aria-disabled="true"` (not removed from tab order, per WCAG guidance for expected controls).
- Bar itself has no special ARIA role — it is a group of action buttons, not a navigation landmark.

**Dark mode:**
`bg-[#F8F9FA]` is light-mode only; `dark:bg-card` uses the dark theme's card surface. This follows the pattern established in design #21. The border (`border-border`) adapts automatically in dark mode.

**#F8F9FA token ownership:**
Design #21 introduced `bg-[#F8F9FA]` as an arbitrary value for the nav pane background. This design follows the same pattern. If #21 merges first (expected, as it is a smaller change), the usage is already established; if #22 merges first, it establishes it. Neither PBI introduces a named CSS custom property for `#F8F9FA` — a named token is warranted only if a future PBI uses it in a third place.

**Stacked-bars separation:**
The `AppCommandBar` has a `border` (all sides, `rounded-md`) and renders with `mb-4` above the grid card. The `OpenTmsGridToolbar` sits inside the grid card (which has its own card background and top rounding). With 16 px of vertical space between the command bar's bottom edge and the grid card top, and the visible card boundary of the grid, the two surfaces read as clearly separate at all viewport sizes. No additional visual treatment is required.

---

### Screen 1 — Users (`/admin/users`) — updated

```
┌──────────────────────────────────────────────────────────────┐
│  [+ New User]              ·  3 selected  [🗑 Delete (3)]    │  ← AppCommandBar
├──────────────────────────────────────────────────────────────┤
│  (mb-4 gap)                                                   │
│  OpenTmsGrid  gridId="platform.administration.users"         │
│   [🔍 Search…          ] [Columns] [Reset]                   │  ← OpenTmsGridToolbar
│   ☐ | Username | Email  | Name     | Roles   | Active        │
│   ────────────────────────────────────────────────────       │
│   ☑ | j.vries  | j.…    | Jan de V.| Planner | ✓            │
│   ☑ | m.jansen | m.…    | Maria J. | Planner | ✓            │
│   ☐ | admin    | a.…    | Admin    | Admin   | ✓            │
└──────────────────────────────────────────────────────────────┘
```

**`AppCommandBar` commands for Users view:**

| Command | `id` | `labelKey` | Icon | `variant` | `requiresSelection` |
|---|---|---|---|---|---|
| New User | `new-user` | `Administration:NewUser` | `UserPlus` (Lucide) | `default` | `false` |
| Delete selected | `bulk-delete-users` | `Administration:BulkDelete` | `Trash2` (Lucide) | `destructive` | `true` |

`selectionCount` is derived from `OpenTmsGrid`'s `onSelectionChange` callback:
- Explicit mode: `explicitIds.length`.
- FilterBased mode: `filteredCount` from the last grid response minus `excludedIds.length`.
- The view holds `selectionCount` in local React state, updated by `onSelectionChange`.

Selection is cleared (reset to 0) when the grid re-fetches after a sort or filter change. AG Grid's SSRM clears selection automatically on `api.refreshServerSide({ purge: true })`; `onSelectionChange` is then called with an empty `Explicit` selection.

**Delete in-flight:** the Delete selected command is additionally `disabled={bulkDeleteMutation.isPending}` to prevent double-click.

**New User permission guard:** the New User command is hidden (not rendered in the `commands` array) when the user lacks `Platform.Administration.Users.Create`. The guard uses the same `IPermissionChecker`-derived client-side check established by design #8.

**Removing the existing inline contextual bar:** design #8 described a contextual bar that appeared above the grid when ≥ 1 row was checked. The command bar replaces this pattern entirely. No standalone `[+ New User]` button or separate contextual bar remains.

---

### Screen 2 — Roles (`/admin/roles`) — updated

```
┌──────────────────────────────────────────────────────────────┐
│  [+ New Role]              ·  2 selected  [🗑 Delete (2)]    │  ← AppCommandBar
├──────────────────────────────────────────────────────────────┤
│  (mb-4 gap)                                                   │
│  OpenTmsGrid  gridId="platform.administration.roles"         │
│   [🔍 Search…          ] [Columns] [Reset]                   │
│   ☐ | Role name | Default | Public | Users | (static)        │
│   ────────────────────────────────────────────────────       │
│   ☑ | Planner  | No      | Yes    | 7     |                  │
│   ☑ | Dispatcher| No     | Yes    | 3     |                  │
│   ☐ | Admin    | No      | Yes    | 2     | static           │
└──────────────────────────────────────────────────────────────┘
```

**`AppCommandBar` commands for Roles view:**

| Command | `id` | `labelKey` | Icon | `variant` | `requiresSelection` |
|---|---|---|---|---|---|
| New Role | `new-role` | `Administration:NewRole` | `ShieldPlus` (Lucide) | `default` | `false` |
| Delete selected | `bulk-delete-roles` | `Administration:BulkDelete` | `Trash2` (Lucide) | `destructive` | `true` |

**Checkbox column:** the roles `OpenTmsGrid` gains `rowSelection: { mode: 'multiRow', selectAll: 'filtered' }` and the `onSelectionChange` prop. Static roles can still be checked — the backend skip logic handles them (reports as skipped); disabling the checkbox on static rows is a UX enhancement deferred to a future PBI.

**New Role permission guard:** same pattern as Users — command hidden when user lacks `Platform.Administration.Roles.Create`.

**Delete selected permission guard:** command hidden when user lacks `Platform.Administration.Roles.BulkDelete`.

---

### Dialogs

**`BulkDeleteRolesDialog`** — new in `src/apps/admin/components/BulkDeleteRolesDialog.tsx`

Parallel to `BulkDeleteUsersDialog`. Props: `{ isOpen, onClose, selection: GridSelectionDto, selectionCount: number }`.

Step 1 — confirmation:
> "You are about to delete {selectionCount} roles. Roles assigned to users will be removed from those users. This cannot be undone."
> Cancel / Delete buttons.

Step 2 — result (shown after the API call returns):
> "Deleted {deletedCount} roles."
If `skippedRows` is non-empty, a list of skipped roles is shown:
```
Skipped (1):
  admin — System role
```
Reason strings resolved from the localization resource using the `reason` key from `SkippedRoleDto`. Close button dismisses; roles grid is invalidated.

---

### All UI states

| State | `AppCommandBar` | Views |
|---|---|---|
| Loading | Renders at full height with all commands disabled (`disabled={true}`) | Existing grid loading overlay (unchanged) |
| Empty (no filter) | New command enabled (if permitted); Delete disabled (0 selected) | Existing empty state (unchanged) |
| Error | Same as empty — grid error overlay does not affect the bar | Existing error handling (unchanged) |
| Permission-denied | Bar renders without commands for which the user lacks permission; if all commands are hidden, the bar renders empty but preserves height | Existing permission-denied card (unchanged) |
| Delete in-flight | Delete command `disabled`; New command remains active | Dialog shows spinner |

### Optimistic updates

None designated. Role bulk delete involves server-side skip logic; the result (deleted/skipped counts) is presented after the round-trip. At administration volumes, a brief loading state with a grid refresh on success is the correct trade-off.

---

## Test risk analysis

This change is in the horizontal platform shell (new shared `AppCommandBar`) and in the Platform module (new role bulk-delete endpoint). Per design §3.1, **any change touching the platform layer is risk class critical by default.**

| Part | Risk class | Rationale |
|---|---|---|
| `AppCommandBar` component | **Critical** | Shared library component; all future views that adopt it depend on its props contract. A breaking API change or render regression here is cross-product. |
| `UserRoleAppService.BulkDeleteAsync` — static-role skip logic | **Critical** | Incorrectly deleting a static role (e.g. the `admin` role) causes irrecoverable tenant lockout. The guard must be tested in isolation before integration. |
| `UserRoleAppService.BulkDeleteAsync` — FilterBased resolution | **High** | Server-side re-execution of the filter must match the client's selection intent; pagination edge cases (role added between select-all and delete) must be handled gracefully. |
| Permission definition (`Roles.BulkDelete`) | **Critical** | Incorrect permission definition can silently grant or deny access across the tenant fleet. |
| `UsersView` refactor (remove standalone button + contextual bar, add `AppCommandBar`) | **High** | Behavioural change scoped to one view; the existing bulk-delete flow must work identically through the new `AppCommandBar` trigger. |
| `RolesView` additions (checkbox column, `AppCommandBar`, `BulkDeleteRolesDialog`) | **High** | New user-visible behaviour; selection tracking, dialog confirmation, and grid invalidation must all work correctly. |
| `BulkDeleteRolesDialog` | **Medium** | Dialog logic is parallel to `BulkDeleteUsersDialog` (proven pattern from design #8); lower risk by analogy. |
| Localization key additions | **Low** | Additive only; a wrong string is visible but not a data or security issue. |
| Selection-count accuracy (FilterBased mode) | **High** | `filteredCount − excludedIds.length` must correctly represent the number of items that will be deleted server-side. A discrepancy between the displayed count and the actual delete count is a UX correctness issue. |

### E2E coverage

The §4.5 matrix mandates E2E tests for critical-risk parts.

Mandated journeys (Playwright, extending `frontend/e2e/shell-nav.spec.ts` or a new `admin-command-bar.spec.ts`):
1. Login → `/admin/roles` → select two non-static roles → assert Delete command enabled → click Delete → confirm → assert result dialog → assert grid refreshed (roles gone).
2. Login → `/admin/roles` → select static role only → assert Delete command enabled → confirm → assert result shows 0 deleted, 1 skipped (static).
3. Login → `/admin/users` → `AppCommandBar` New User command → assert dialog opens (smoke only; full create flow covered by design #8 tests).
4. Empty grid → assert `AppCommandBar` renders at full height with Delete disabled.

---

## Flag & rollout plan

### Command bar chrome

**No feature flag.**
`DEVIATION(constitution-4): command-bar chrome adds no new capability, data, or backend surface. The shell is identical for all tenants and functionally unchanged from the user's perspective (the same create and delete actions exist, just surfaced differently). A per-tenant flag would force `AppCommandBar` to carry both the old button layout and the new bar until cleanup. Rollback is a revert of the PR. Gate 1 must formally confirm this exemption.**

### Role bulk delete

**No feature flag.**
`DEVIATION(constitution-4): role bulk delete falls under the Administration bootstrap-deadlock justification established in design #8. The Administration app already carries that deviation (approved by PO in issue #8); this PBI extends it with an additional action within the same app. Access is gated by the new Platform.Administration.Roles.BulkDelete permission, which is the appropriate gate for admin capability. A per-tenant feature flag would require the Administration UI (and specifically the permission management screen) to be available to toggle the flag — circular. Gate 1 must formally confirm this exemption.**

Rollout:
1. Deploy Platform module changes (new permission, new endpoint, localization keys).
2. Deploy frontend changes (`AppCommandBar`, updated views, `BulkDeleteRolesDialog`).
3. Verify command bar renders correctly on both views on the health tenant.
4. Confirm the `admin` role has the new `Platform.Administration.Roles.BulkDelete` permission seeded (extend the DbMigrator seeder entry from design #8).
5. No per-tenant activation step.

---

## Cost & SLO impact

**GCP impact:** negligible.

| Resource | Impact |
|---|---|
| Cloud SQL (ABP Identity schema) | One new endpoint reads from existing `AbpRoles` / `AbpUserRoles`. Bulk delete iterates per role; at tens of roles per tenant the delete loop is trivial and within ABP's existing indices. |
| Cloud Run | No new min instances. Role bulk delete is infrequent administrative traffic. |
| Egress | `BulkDeleteRolesResponseDto` payloads are < 1 KB. Negligible. |
| External API calls | None. |
| Per-tenant margin | Unaffected — no paid external service involved. |

**Performance budgets touched:**

- **Mutations (p95 < 500 ms):** `POST .../roles/bulk-delete` iterates per role via ABP Identity's role repository. At tens of roles per tenant the loop completes well within 500 ms. If a tenant ever accumulates hundreds of roles, the endpoint must become asynchronous (Hangfire job) — deferred, consistent with the same note in design #8 for user bulk delete.
- **Web frontend bundle:** `AppCommandBar` ships in `src/components/ui/` and is tree-shaken — it is included only in route chunks that import it (currently the `admin` chunk). The admin chunk is already lazy-loaded per design #8. CI bundle-size budget is not expected to regress.
- **INP < 200 ms:** the selection count state update (`onSelectionChange` → local state → re-render of `AppCommandBar`) is a simple React state update. No heavy computation.
- **Interactive reads (LCP):** the command bar renders synchronously with the view; it adds no new async on the critical path.
- **Availability SLO (99.9 %):** the new endpoint is administrative traffic, not a core flow. A transient unavailability does not consume the core-flow error budget.

---

## Assumptions

1. **Selection count in FilterBased mode** is derived client-side as `filteredCount − excludedIds.length`, where `filteredCount` comes from the last `GridResponse<T>` received by `OpenTmsGrid`. If AG Grid's SSRM does not expose `filteredCount` from the last block response to the `onSelectionChange` callback, the implementer reads it from component state and passes it alongside `GridSelectionDto`. The count shown in the command bar must match the number of items the server will actually delete.

2. **Selection cleared on re-fetch/re-sort.** AG Grid's SSRM calls `api.refreshServerSide({ purge: true })` on sort or filter change, which clears the selection. `onSelectionChange` fires with an empty `Explicit` selection; the command bar disables Delete immediately. The implementer confirms this AG Grid behavior in the pinned version before relying on it.

3. **Static role identification.** `IsStatic = true` on `IdentityRole` correctly identifies all system-protected roles (including the `admin` role). The bulk-delete skip guard uses the same check as the single-delete guard in design #8 (assumption 2 of that design). If the ABP version uses a different guard mechanism, the implementer adjusts and notes the deviation.

4. **ABP Identity cascade-removes roles from users on role deletion.** The `UserRoleAppService.BulkDeleteAsync` implementation relies on this cascade (same as the `force=true` path in design #8, assumption 5 of that design). The implementer confirms cascade behavior in the ABP version used.

5. **FilterBased mode server-side re-resolution** for roles uses the same `GridRequest` filter shape as the roles grid block-fetch endpoint (`GET .../roles`). The `UserRoleAppService.BulkDeleteAsync` implementation passes the `filterRequest` to ABP Identity's role list query to resolve affected role ids server-side.

6. **`AppCommandBar` renders at zero commands.** When a view renders `<AppCommandBar commands={[]} />` (e.g. a user with no permissions), the bar renders as an empty strip at `h-12`, preserving height but showing no buttons. The implementer must verify the height is maintained in CSS (e.g. via `min-h-12` or an explicit `h-12` class) so no reflow occurs.

7. **Permission-hidden commands vs. disabled commands.** Commands for which the user lacks permission are **not rendered** in the `commands` array (the view guards them before passing). This is different from `requiresSelection`-disabled commands, which are always rendered but disabled. The distinction matters for keyboard navigation (hidden commands are not focusable). This policy is enforced at the view level, not inside `AppCommandBar`.

8. **Design #21 merges before #22.** The `#F8F9FA` arbitrary value for `bg-[#F8F9FA]` is used in one place after #21 merges (the nav pane). After #22 merges, it is used in two places (nav pane + command bar). Neither PBI introduces a named CSS custom property; if a third consumer appears, a token (`--color-surface-subtle` or similar) should be introduced via a PO-approved design.

9. **`ShieldPlus` is available in the Lucide version pinned by this project.** If not, the implementer substitutes the nearest available icon (`ShieldCheck` from design #18 is already included) and notes the substitution.

10. **`useBulkDeleteRoles` exposes `isPending`.** TanStack Query v5's `useMutation` returns `isPending` (not the v4 `isLoading`). The implementer confirms the pinned TanStack Query version and uses the correct property name.

11. **`BulkDeleteRolesDialog` reuses the same dialog primitive as `BulkDeleteUsersDialog`.** Both use `@base-ui/react` `Dialog` with the `render` prop, established by design #8. No additional dependency is needed.

12. **The DbMigrator seeder grants `Platform.Administration.Roles.BulkDelete` to the `admin` role** for both host and tenant databases, extending the seeder entries from design #8. The implementer adds this permission to the same seeder block.

---

## Security quickscan

### New ABP permission definitions

One new permission constant: `Platform.Administration.Roles.BulkDelete`. Defined via `PlatformPermissionDefinitionProvider` under the existing `Administration.Roles` group — no hardcoded role or id checks anywhere in the app service or controller layer.

### Input validation boundaries

| Input | Validation | Location |
|---|---|---|
| `BulkDeleteRolesRequestDto.Selection` | `GridSelectionDto` validated per design #6 contract (`mode` enum, `explicitIds` non-empty for Explicit, `filterRequest` shape for FilterBased) | DTO attribute + `IValidatableObject` |
| `explicitIds` entries | GUID format; ABP model binding rejects non-GUIDs | ABP controller model binding |
| `filterRequest` (FilterBased) | `GridRequest` validation from design #6 (bounds on startRow/endRow, wildcardSearch max 200 chars) | `GridRequest` validation |

### Attack surface changes

- One new HTTP endpoint (`POST .../roles/bulk-delete`), protected by `Platform.Administration.Roles.BulkDelete`.
- No anonymous or unauthenticated access path.
- No new cross-tenant data access: ABP Identity's `ICurrentTenant` scopes all role queries; a host-admin attempting to delete tenant roles in tenant context requires explicit tenant-context switching, which ABP enforces.
- FilterBased resolution re-executes the filter through ABP Identity's `GetListAsync` with EF Core parameterized queries — no SQL injection vector.
- The `AppCommandBar` component introduces no new client-side data handling, fetch calls, or dynamic code paths beyond the existing button and localization patterns.

### GDPR (art. 15 / art. 17)

Roles are not personal data — they are structural access-control entities. No GDPR art. 15/17 obligation arises from role bulk delete. The deletion of a user's role assignments (via cascade) does not itself constitute erasure of personal data.

No new personal data is introduced, processed, or logged by this PBI. Constitution rule 5 applies: the bulk-delete app service logs only entity ids and operation outcomes (e.g. `LogInformation("Role {RoleId} deleted by {ActorId}", roleId, currentUser.Id)`).
