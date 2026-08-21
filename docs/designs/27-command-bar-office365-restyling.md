# Design: Office-365 command-bar restyling (#27)

**PBI:** https://github.com/marinusbrink/opentms-next/issues/27
**Status:** draft
**Date:** 2026-08-21

<!-- All nine sections below are mandatory (design §4.2). "Not applicable" is an
     acceptable answer only with a one-line reason; a missing section is not.
     Gate 1 approves this document by merging the PR — after that, implementers
     build exactly this, so vagueness here becomes iteration later. -->

## Domain impact

Pure frontend — no backend domain modules are changed, no entities, no domain events, no
database schema changes.

**Frontend component library (shared):**

- `frontend/src/components/ui/app-command-bar.tsx` — updated in-place (additive props,
  flag-gated new rendering path)
- `frontend/src/components/ui/__tests__/app-command-bar.test.tsx` — test suite extended

**Platform module (horizontal) — additive only:**

- New ABP feature definition `UI.CommonToolbar` in
  `backend/modules/platform/OpenTms.Platform.Application.Contracts/Features/PlatformFeatureDefinitionProvider.cs`
  (new file). Default value: `"false"`.
- New localization keys in Platform `en.json` and `nl.json`:
  `Shell:CommandBarMore` ("More" / "Meer") and `Shell:CommandBarMoreLabel`
  ("More actions" / "Meer acties").

**Callers requiring an additive update:**

| Screen | File | Change |
|--------|------|--------|
| Roles | `frontend/src/apps/admin/roles/RolesView.tsx` | Set `isPrimary: true` on the "New role" command |
| Users | `frontend/src/apps/admin/users/UsersView.tsx` | Set `isPrimary: true` on the "New user" command |

No cross-domain events. The dependency arrow is: callers → shared UI component → Platform
feature flag. Platform references no domain module — this stays within the floor layer.

## API contract

No new backend HTTP endpoints. The `UI.CommonToolbar` feature flag is surfaced through the
existing ABP application-configuration endpoint
(`GET /api/abp/application-configuration`), which already returns `features.values`.
No OpenAPI spec change; no typed-client regeneration needed.

**Frontend component API — additive, backward-compatible:**

```ts
// frontend/src/components/ui/app-command-bar.tsx

export interface AppCommandBarCommand {
  id: string;
  labelKey: string;
  tooltipKey?: string;        // tooltip text (localization key); defaults to labelKey
  icon?: LucideIcon;
  isPrimary?: boolean;        // true → primary left-most action with distinct visual treatment
  variant?: "default" | "destructive";
  requiresSelection?: boolean;
  disabled?: boolean;
  disabledReasonKey?: string; // tooltip shown on disabled state (localization key)
  onClick: () => void;
}

export interface AppCommandBarProps {
  commands: AppCommandBarCommand[];
  selectionCount?: number;
  className?: string;
}
```

All new fields (`tooltipKey`, `isPrimary`, `disabledReasonKey`) are optional with
`undefined` as the backward-compatible default. Existing callers compile and behave
unchanged without adding them. Under flag-off the new fields are read but ignored in
rendering.

**Flag reading on the frontend:**

```ts
// Inside AppCommandBar, via useApplicationConfiguration():
const isNewStyleEnabled =
  data?.features?.values?.['UI.CommonToolbar'] === 'true';
```

`useApplicationConfiguration` is already present in the codebase
(`src/lib/abp/queries`). No new hook required.

## Migration strategy

**Expand phase (this release):**

1. Add `PlatformFeatureDefinitionProvider.cs` defining `UI.CommonToolbar` with default
   `"false"`. Register in `PlatformApplicationContractsModule`.
2. Add localization keys `Shell:CommandBarMore` and `Shell:CommandBarMoreLabel` to
   Platform `en.json` (English) and `nl.json` (Dutch).
3. Update `AppCommandBarCommand` interface with the three optional fields
   (`tooltipKey`, `isPrimary`, `disabledReasonKey`).
4. Update `AppCommandBar` rendering:
   - Flag-off path: existing rendering unchanged (backward-compatible guarantee).
   - Flag-on path: new Office-365 anatomy (see UI design section).
5. Update `RolesView` and `UsersView`: add `isPrimary: true` to their primary create
   command.
6. Extend `app-command-bar.test.tsx` with new test cases (see Test risk analysis).

**No schema migration.** No database tables touched. No API version bump. No Hangfire jobs.

**Contract step:** not applicable — no existing prop or API is renamed or removed in this
release. The flag-off rendering path is a standing contract guarantee until the flag is
universally enabled and the old path is explicitly retired in a future release (scheduled
by the release manager, outside the scope of this design).

## UI design

### Bar anatomy (flag on)

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ [■ New Role]  │  [✉ E-mail]  [→ Send]  [… More ▾]           3 sel  [🗑 Del] │
└──────────────────────────────────────────────────────────────────────────────┘
```

### Bar container

- Height: `h-10` (~40 px), full content width.
- Background: light surface `bg-[#F8F9FA]`, dark mode: `dark:bg-background` (same as
  NavDrawer; both surfaces match in light and dark).
- No top border, no outer border, no rounded corners on the bar itself.
- Vertical alignment: all items `items-center`.

### Primary action slot (leftmost; rendered only when a command has `isPrimary: true`)

- White surface: `bg-white rounded-t-md px-3 py-1 shadow-sm`.
- Text and icon in the brand colour: `text-brand` (inherits to icon via `currentColor`).
- Icon (if present) + label. Label weight: `font-semibold`.
- Separated from secondary actions by a right-margin gap (`mr-2`) and a visual divider
  (`border-r border-border`).
- Hover state: `hover:bg-brand/20` (20 % brand colour fill) + label underlined
  (`hover:underline`); tooltip opens below the button (`side="bottom"`) with the
  `tooltipKey` value (fallback: `labelKey`).
- Focus: focus-visible ring (existing token) + underline; tab order: primary is first.
- Tooltip accessibility: tooltip text is the button's `aria-describedby` target; screen
  reader announces the label, not just "New".
- The primary action never collapses into the overflow menu.

### Secondary actions (flat row, to the right of the primary slot)

- Layout: horizontal flex row, `gap-1`.
- Each action: icon (if present) + label, `text-sm`, flat button with white background
  (`bg-white`), generous horizontal padding `px-3`.
- Text and icon in the brand colour: `text-brand` (inherits to icon via `currentColor`).
- No button borders, no separators between them.
- An action without an icon renders label-only (no placeholder icon).
- Hover state: `hover:bg-brand/20` (20 % brand colour fill) + label underlined
  (`hover:underline`); tooltip opens below with `tooltipKey` (fallback: `labelKey`).
- Disabled state: `aria-disabled="true"` (not HTML `disabled` — keeps the element
  focusable for tooltip); `opacity-50`; tooltip shows `disabledReasonKey` if provided,
  else `labelKey`. `onClick` is suppressed when `aria-disabled` is true.
- Tab order: secondary actions follow the primary action, left to right.

### Overflow menu

Triggered when secondary actions exceed available width. Detection mechanism:
`ResizeObserver` on the bar container measures available width and determines how many
secondary actions fit; the remainder is collapsed right-to-left (rightmost collapses
first; leftmost stays visible longest).

- Overflow button: `[… More]` label (`Shell:CommandBarMore`), accessible name
  `Shell:CommandBarMoreLabel`, renders as a secondary-style flat button.
- Overflow button appears only when at least one secondary action is collapsed.
- Overflow menu uses the existing `DropdownMenu` / `DropdownMenuContent` /
  `DropdownMenuItem` components.
- Each overflowed action inside the menu: icon + label (both visible in the menu).
- Keyboard: Enter/Space opens the menu; arrow keys navigate items; Escape closes and
  returns focus to the overflow button.
- The primary action is never included in the overflow menu.
- Selection-gated actions (right side) are never included in the overflow menu.

### Selection count and selection-gated actions (right side)

- Unchanged from current implementation.
- Selection count badge and selection-gated command buttons remain right-aligned after a
  `flex-1` spacer.
- No overflow interaction with this group.

### Edge cases

| Situation | Behaviour |
|-----------|-----------|
| No primary action (`isPrimary` absent on all commands) | Primary slot hidden; secondary actions fill from the left. |
| Empty toolbar (all actions hidden by permissions) | Component returns `null` — bar is not rendered. Content layout adjusts. |
| All secondary actions overflow | Only the overflow button is visible in the secondary row. |
| Single secondary action, no overflow | Overflow button not rendered. |
| Action without icon | Renders label-only; no filler icon. |
| Long label text | Label truncated with ellipsis (`truncate max-w-[120px]`) to keep the bar single-line. Tooltip shows the full label. |

### States

All four mandatory states are handled at the **screen** level (existing pattern); the
toolbar itself does not show loading/error states — it renders actions or returns `null`:

| State | Toolbar behaviour |
|-------|------------------|
| Loading | Rendered normally; grid underneath shows skeleton |
| Empty | Rendered normally; grid shows "No results" |
| Error | Rendered normally; grid shows retry block |
| Permission-denied | View returns the permission-denied message; toolbar not rendered |

### Optimistic updates

None — toolbar actions open dialogs or trigger mutations managed by the dialog/form
components. No toolbar-level mutation-then-refresh requires optimistic treatment.

## Test risk analysis

| Part | Risk class | Rationale |
|------|------------|-----------|
| `AppCommandBar` component — shared library, new API contract | **Critical** | Every screen that uses the toolbar is affected at flag-on. An API contract break silently breaks all callers. Existing test suite is already at Critical coverage — it must be extended. |
| ABP feature definition `UI.CommonToolbar` | **High** | Platform addition. Wrong default (`"true"`) would activate the new look for all tenants immediately; wrong flag reading silently disables rollout. |
| Overflow detection (`ResizeObserver`) | **High** | DOM measurement. Failures hide secondary actions — users cannot trigger them. Must be tested with a mocked `ResizeObserver`. |
| Keyboard and accessibility (WCAG 2.1 AA) | **High** | Tab order, overflow menu keyboard navigation (Enter/Space/Arrow/Escape), focus indicators, `aria-disabled` on disabled actions, accessible names for tooltips. |
| Per-screen migration (`isPrimary: true` on callers) | **Medium** | Additive prop; missing it degrades visual style, not functionality. Currently two screens. |
| Localization keys | **Low** | Additive. Missing key falls back to the raw key string — visible, not a functional break. |

**Test engineer guidance:**

- Extend `app-command-bar.test.tsx`:
  - Flag-off path: existing tests pass unchanged (regression guard).
  - Flag-on path: primary action renders with `font-semibold`; secondary action renders
    flat; empty-toolbar returns `null`; no-primary renders secondary from left.
  - Overflow: mock `ResizeObserver` to report zero width → all secondary actions overflow
    → only overflow button visible; mock full width → no overflow button.
  - Disabled action: `aria-disabled="true"` present; `onClick` not called on click; tooltip
    trigger present in DOM.
  - Keyboard: simulate keyboard open/close of overflow menu; assert Escape returns focus.
  - Accessibility: run `axe` on rendered bar with actions.
- Feature flag unit test: render with `useApplicationConfiguration` returning
  `features.values['UI.CommonToolbar'] = 'false'` and `= 'true'`; assert the correct
  rendering path.
- No E2E test required: the feature is pure client-side rendering with no backend
  integration path beyond reading the application-configuration.

## Flag & rollout plan

| Attribute | Value |
|-----------|-------|
| Flag name | `UI.CommonToolbar` |
| Type | ABP feature management (boolean string, `"false"` default) |
| Checked in | Frontend: `features.values['UI.CommonToolbar']` from `useApplicationConfiguration()` |
| Defined in | Platform `Application.Contracts/Features/PlatformFeatureDefinitionProvider.cs` |

**Activation order:**

1. **Health tenant** — internal use only. Verify visual correctness across all migrated
   screens; check overflow at narrow viewport widths; verify keyboard navigation.
2. **Friendly customers** — 2–3 opted-in tenants. Validate in real usage; collect feedback
   on visual and accessibility expectations.
3. **All tenants** — flip the ABP feature definition default from `"false"` to `"true"` in
   a subsequent Platform release. The release manager schedules this step.

**Existing tenant data:** none — the flag has no database footprint. Tenant activation is
purely ABP feature management. No data migration exists or is needed.

**Flag-off guarantee:** flag-off = today's per-screen toolbar rendering, unchanged for
that tenant. Flag-on = new look for all migrated screens for that tenant simultaneously
(PO decision in intake; not per-screen).

## Cost & SLO impact

- **No backend costs added.** No new Cloud SQL queries, no egress, no Cloud Run instance
  changes, no Hangfire jobs, no external API calls.
- **Frontend compute:** `ResizeObserver` is a native browser API; O(1) measurement per
  resize event. Debounced to avoid thrashing during window resize. No perceptible CPU cost.
- **Bundle size:** `DropdownMenu` and `Tooltip` are already imported in the app.
  The new component code (overflow hook + new rendering branch) adds an estimated <5 KB
  to the `app-command-bar` module, well within the per-route bundle budget.
- **Performance budgets touched:**
  - **Web frontend** (bundle size per route in CI) — no budget breach expected; the CI
    check is the hard gate.
  - All other rows (interactive reads, mutations, heavy operations, mobile, availability)
    are unaffected.
- **SLO impact:** none. Core flow availability is unaffected by a client-side rendering
  change.

## Assumptions

1. **Dark-mode bar surface:** Resolved at gate 2. The bar uses `dark:bg-background`,
   matching the NavDrawer (`AppNavPane`). The earlier `dark:bg-card` assumption is
   withdrawn.

2. **Overflow detection via ResizeObserver:** The overflow collapse mechanism uses a
   `ResizeObserver` on the bar container and measures individual action widths in a first
   render pass. This is the standard pattern for this problem and is compatible with the
   Vite/React/Tailwind setup. A pure-CSS container-query alternative was considered and
   rejected (requires CSS `@container` + `@container-style` for per-item visibility,
   adding complexity without meaningful benefit here).

3. **Screen inventory:** At the time of writing, `RolesView` and `UsersView` are the only
   callers of `AppCommandBar`. Gate 1 must confirm this is the complete list of screens
   to migrate in this release, or name additional screens. Any future screen using
   `AppCommandBar` gets the new visual style automatically when the flag is on.

4. **Callers without `isPrimary`:** Under flag-on, a command array where no command has
   `isPrimary: true` renders all non-selection commands as secondary actions (no primary
   slot shown). Gate 1 must confirm this degraded-graceful behaviour is acceptable.

5. **Empty toolbar behaviour:** When all commands are filtered out by permissions (empty
   array), the component returns `null` and the bar disappears. Gate 1 must confirm; the
   only alternative (render an empty bar) was rejected as it wastes vertical space
   without useful information.

6. **ABP feature reading pattern:** `useApplicationConfiguration` returns
   `features.values` as a `Record<string, string>`. The flag check
   `features.values['UI.CommonToolbar'] === 'true'` is consistent with how ABP feature
   management serializes boolean features to JSON. Gate 1 must confirm this matches the
   actual ABP response shape in this project.

7. **Selection-gated actions are not secondary actions:** Commands with
   `requiresSelection: true` remain on the right side of the spacer in both flag-off and
   flag-on modes, using the current destructive-button rendering. The Office-365 anatomy
   applies only to non-selection commands.

## Security quickscan

- **New permissions:** none. The `UI.CommonToolbar` flag is an ABP *feature* (tenant
  self-service rendering control), not a permission gate. No `IPermissionDefinition` is
  added.
- **Input validation:** the component renders localization keys passed in via the
  `commands` prop. Localization keys are static strings resolved server-side; no raw
  user input is rendered. No XSS surface change.
- **Attack surface:** the overflow menu is a client-side dropdown; it triggers no HTTP
  requests and adds no new network boundary.
- **Personal data:** none introduced or touched.
- **GDPR art. 15/17:** not applicable — no personal data storage.
- **WCAG 2.1 AA:** required per intake and captured as acceptance criteria in the test
  risk analysis. This is a delivery requirement, not a security concern, but is noted
  here as it was mandated by the PO.
