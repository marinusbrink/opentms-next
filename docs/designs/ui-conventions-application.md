# Design: Apply the UI conventions to the existing front-end

**PBI:** none — built on the PO's direct instruction (2026-10-10) to apply the UI conventions
package (`docs/conventions/ui/`, from `dev-department` commit `120bb72`) to the current UI
**Status:** in review
**Date:** 2026-10-10

<!-- All nine sections below are mandatory (design §4.2). "Not applicable" is an
     acceptable answer only with a one-line reason; a missing section is not.
     Gate 1 approves this document by merging the PR — after that, implementers
     build exactly this, so vagueness here becomes iteration later. -->

> **DEVIATION(constitution-4): presentation-layer application of the UI conventions to the
> two existing admin screens, no data or business-logic change; the same grounds the PO
> accepted for PBI #30 (no flag-off maintenance path for a restyle, rollback is a revision
> revert), approved in docs/designs/ui-conventions-application.md**
>
> Constitution rule 4 asks for a per-tenant flag on new user-visible behaviour. This change
> makes the admin screens follow the conventions every future screen follows (context menu,
> confirm dialogs, editor dialogs, toasts, token colours). A flag would mean maintaining the
> old rendering of every touched component beside the new one, for two screens that have no
> external users yet. **The PO confirms this deviation by merging the PR**; if a flag is
> wanted after all, the convention-conformant components stay and the admin views get a
> `UI.Conventions` switch around the row-action and dialog wiring.

## Domain impact

No backend domain change. Platform's localization resource (`Domain.Shared/Localization/Platform`)
gets the library keys the rules name (`Close`, `Cancel`, `Save`, `Saving`, `Delete`, `Deleting`,
`Edit`, `Discard`, `UnsavedChanges`, `DiscardChangesQuestion`, `ThisCannotBeUndone`,
`UnexpectedError`, `PermissionDenied`, `FieldRequired`, `Grid:Refresh`, `Grid:Export`,
`Duration:*`) and the outcome keys of the admin screens (`Administration:UserSaved`, `…Deleted`,
`…DeleteUserQuestion`, …). Two existing texts lose their em dash (UI-FB-007) and the bulk-delete
confirmations lose their "This cannot be undone." sentence, which now renders as an `Alert`.

**Frontend, per the package's install steps and `component-map.md` §5:**

| Area | Change | Rules |
|---|---|---|
| `docs/conventions/ui/` | the package, unchanged (canonical copy stays in `dev-department`) | — |
| `src/styles/opentms-theme.css`, `grid.css`, `index.css` | the theme tokens; `--brand` moves out of `index.css`; selected-row text colour | UI-TOK-001 |
| `lib/theme/apply-tenant-theme.ts` | `--brand` from `App.UiManagement.Theme` / `CustomColor` with the product blue as fallback; applied once the application configuration loads | UI-TOK-003, D-01 |
| `lib/format` | `useFormat()`: en → en-GB, 24-hour clock, three date formats, number kinds, durations | UI-FMT-001 to -009 |
| `lib/api/errors.ts`, `lib/auth/permissions.ts` | `getApiErrorMessage`, `useIsGranted`, `useIsFeatureEnabled` replace the seven and two copies | UI-FB-004, UI-PERM-001 |
| `components/ui/grid-theme.ts` | AG Theming API from the tokens; the legacy `ag-theme-quartz.css` import goes | UI-TOK-004, D-04 |
| `components/ui/grid-cells.tsx` | `NumberCell`, `AmountCell`, `DateCell`, `DateTimeCell`, `BoolCell` | UI-GRID-004 |
| `OpenTmsGrid` | `rowActions` → context menu (AG built-ins off), `onOpen` → double click and Enter, `filters` slot, `onExport`, Refresh; the block-error toast through Sonner | UI-GRID-005/007/008, UI-FB-001 |
| `OpenTmsGridFooter` | counts through `useFormat` | UI-FMT-001 |
| `EntityDialog`, `ConfirmDialog`, `FormField`, `PermissionDenied`, `Alert warning`, `Checkbox`, `Sonner` | the library components the rules specify; `EntityDialog` adds the **Close** command itself so the dirty guard is uniform | UI-DLG-*, UI-FORM-*, UI-FB-008, UI-PAGE-005 |
| Admin `UsersView`, `RolesView` | action column and inline delete popover removed; context menu + double click; `BoolCell` / `DateCell` / `NumberCell`; `ConfirmDialog` for deletes (the role-with-users conflict is a second confirmation with a warning `Alert`); shared `PermissionDenied` | D-09, D-21, UI-GRID-010 |
| Admin dialogs | `UserFormDialog` / `RoleFormDialog` become `editor` dialogs (brand bar, Save + Close commands, dirty guard, validation on blur/submit, inline server error, read-only mode without Update); `ResetPasswordDialog` and the bulk deletes become `action` dialogs with `[Cancel] [verb]` footers; success toasts name the subject | UI-DLG-002 to -011, UI-FORM-006/010, UI-FB-002, UI-PERM-004 |
| Shell | `bg-surface-page`, `text-brand-foreground`; `<Toaster position="bottom-right">` mounted once | UI-TOK-001/002, D-33 |

**Not in this change** (built the first time a feature needs them, per the package's README):
`EntityLookup`, `LinesGrid`, `StatusBadge`, `FormSection`, `Wizard`, `useOpenRecord()` and the
deep-link record host. The admin editors therefore open from local view state, which UI-PAGE-003
allows; the `?open=` deep link (UI-DLG-007) arrives with the first domain record. The app-tile
colours in `apps.config.ts` belong to the shell, which the conventions exclude.

## API contract

Not applicable — no endpoint, DTO or controller changes; `openapi/opentms-next.json` is unchanged.

## Migration strategy

Not applicable — no schema change.

## UI design

The package *is* the UI design; this section lists what a reviewer sees on the two screens.

- **Users / Roles list:** command bar (New first, Delete as selection command), full-height grid,
  no action column. Right-click: Edit, Reset password (users), separator, Delete (red). Static
  roles: Edit and Delete disabled with the tooltip "System roles cannot be changed". Double click
  or Enter opens the editor; without Update permission it opens read-only. Booleans are a check
  icon; dates are `09-10-2026`; user counts right-aligned.
- **States:** skeleton while the grid loads; the grid's localized empty overlay; block error with
  retry and a Sonner error toast after repeated failures; `PermissionDenied` panel without the
  view permission.
- **Editor (user, role):** 38px brand bar with the title ("New user" / "User admin") and the X;
  under it the strip with **Save** (primary) and **Close**; the form in one column, labels above,
  asterisks on required fields, messages under the field after blur or submit; a failed save as a
  red `Alert` at the top; Enter submits; Esc / X / Close ask "Discard your changes?" when dirty;
  the backdrop never closes it; while saving everything is disabled and Save reads "Saving…".
- **Reset password:** action dialog, plain header, footer `[Cancel] [Reset password]`.
- **Delete (single):** confirm dialog "Delete user" / "Delete user admin? This cannot be undone."
  with `[Cancel] [Delete]`, focus on Cancel. A role still assigned to users gets a second confirm
  with a warning `Alert` naming the count.
- **Delete (bulk):** action dialog "Delete users" with the count, a red `Alert` "This cannot be
  undone.", footer `[Cancel] [Delete]`; then the per-item result with `[Close]`; the summary toast
  "3 users deleted" follows Close.
- **Optimistic updates:** none (unchanged; the design for the admin screens designated none).

## Test risk analysis

| Part | Risk class | Coverage |
|---|---|---|
| `useFormat` (en-GB, 24h, separators, date-only values) | medium | unit tests `lib/__tests__/format.test.ts` |
| `getApiErrorMessage`, `useIsGranted` / `useIsFeatureEnabled`, `resolveBrand` | medium | unit tests |
| Context-menu composition (order, multi-row filter, separator, disabled reason), open-row guard | medium | unit tests `opentms-grid-actions.test.ts` |
| `EntityDialog` (brand header, Close injected after Save, first-field focus, dirty guard, busy lock, action footer) | medium | component tests |
| `ConfirmDialog` (verb, order, focus on Cancel when destructive, busy label, inline failure) | medium | component tests |
| `FormField` (label, asterisk, aria, error/hint), grid cells, grid theme | low/medium | component and unit tests |
| Toolbar Refresh / Export / filters; command bar token classes | medium | updated component tests |
| Admin screens end to end (right-click delete, double-click edit, bulk delete journeys) | medium | the existing Playwright journeys, adjusted to the new dialogs; not run in CI (unchanged situation) |

Tenant isolation: nothing reads or writes tenant data differently; permission gating is unchanged
and hidden-not-disabled (UI-PERM-002) is covered by the permission hook tests and the view code.

## Flag & rollout plan

See the deviation at the top: no flag. Rollback is a Cloud Run revision revert.

## Cost & SLO impact

None on infrastructure. The AG Grid legacy CSS (about 200 KB) leaves the grid chunk; `sonner` adds
about 12 KB to the main bundle. No budget row changes; `OpenTmsGrid` stays lazy-loaded.

## Assumptions

1. The PO accepts the rule-4 deviation above (as for PBI #30).
2. Applying the package means its install steps plus `component-map.md` §5 (changes to existing
   components); the "build on first need" components stay out until a feature needs them.
3. `EntityDialog` owns the **Close** command (injected after the primary) so features cannot
   bypass the dirty guard; the rules' command order (UI-ACT-002) is kept.
4. A user who may view but not update opens the editor read-only (UI-PERM-004); for roles, system
   roles open read-only for everyone (business state).
5. The tenant settings `App.UiManagement.Theme` / `CustomColor` do not exist in this backend yet;
   `applyTenantTheme` reads them when they do and uses the product blue until then (D-01 kept).
6. The command bar still carries the `UI.CommonToolbar` flag paths on `main`; PR #32 removes them.
   This change only replaces the raw colours in both paths, so the merge conflict is one line.
7. The context menu items are text only; AG Grid takes HTML icons, and rendering lucide icons to
   markup for every menu open is not worth the cost for now.
8. Dark mode stays defined but unswitchable (proposals B5); the toaster renders light.

## Security quickscan

- **Permissions:** no new policies. Every command, menu item and dialog is gated by the same
  `Platform.Administration.*` policies as before, now through `useIsGranted`.
- **Input validation:** client-side required-field checks only; the server validates as before.
- **Attack surface:** unchanged; no new endpoints.
- **Personal data:** the admin screens show usernames and e-mail addresses as before; no new
  storage or logging. Toasts name the subject type, not the person ("User saved").
