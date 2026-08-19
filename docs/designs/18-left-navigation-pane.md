# Design: Left navigation pane instead of tabs in Administration app

**PBI:** https://github.com/marinusbrink/opentms-next/issues/18
**Status:** draft
**Date:** 2026-08-18

`DEVIATION(constitution-4): no customers yet, confirmed by PO in issue #18`

<!-- All nine sections below are mandatory (design §4.2). "Not applicable" is an
     acceptable answer only with a one-line reason; a missing section is not.
     Gate 1 approves this document by merging the PR — after that, implementers
     build exactly this, so vagueness here becomes iteration later. -->

## Domain impact

Frontend shell only — no backend modules changed, no new entities, no domain events.

Changed files:

| File | Change |
|---|---|
| `frontend/src/app/apps.config.ts` | Add `AppView` interface; add optional `views?: readonly AppView[]` to `AppDefinition`; declare two views for the `admin` app (`Users`, `Roles`) |
| `frontend/src/app/shell/AppNavPane.tsx` | **New** shared pane component in the shell chunk |
| `frontend/src/app/shell/AppShell.tsx` | Render `AppNavPane` when the active app declares views; adjust layout to row-within-column |
| `frontend/src/apps/admin/index.tsx` | Remove tab bar; simplify to redirect + pathname-driven view switch |
| `backend/modules/platform/OpenTms.Platform.Domain.Shared/Localization/Platform/en.json` | Add two localization keys (additive) |
| `backend/modules/platform/OpenTms.Platform.Domain.Shared/Localization/Platform/nl.json` | Add two localization keys (additive) |

No backend application services, no new database tables, no cross-domain events.

## API contract

No new or changed endpoints. The ABP localization endpoint
(`/api/abp/application-localization`) continues to serve all strings; the new keys in
the Platform resource are returned through it without any contract change.

New localization keys (additive — no existing key removed or renamed):

| Key | en | nl |
|---|---|---|
| `Shell:NavCollapse` | "Collapse navigation" | "Navigatie inklappen" |
| `Shell:NavExpand` | "Expand navigation" | "Navigatie uitklappen" |

Existing keys reused unchanged: `Administration:Users`, `Administration:Roles`.

## Migration strategy

This change is entirely frontend with additive localization key additions. There is no
database schema involved and therefore no expand/contract concern.

**Expand (this release):**

- Add optional `views` field to `AppDefinition` — backward-compatible: every app that
  does not set `views` (all apps except `admin` in this PBI) continues to render
  full-width, unaffected.
- Add two new localization keys to Platform `en.json` and `nl.json` — additive, no
  existing key touched.
- Add `AppNavPane` to the shell chunk.
- Replace the tab bar in `AdministrationApp` with the shell-rendered pane — UI
  replacement, not a data or API contract change.

**Contract step:** Not applicable — no database schema or HTTP API contract changes.

## UI design

### Layout

`AppShell` changes from a pure column layout to a two-zone layout when the active app
declares views:

```
┌──────────────────────────────────────────────────┐
│  App bar (full width, h-12, #0f6cbd)             │
├─────────────┬────────────────────────────────────┤
│  AppNavPane │  <main> content area               │
│  expanded   │  (flex-1, p-6, unchanged)           │
│  ~250px     │                                     │
├─────────────┤                                     │
│  collapsed  │                                     │
│  icon rail  │                                     │
│  ~56px      │                                     │
└─────────────┴────────────────────────────────────┘
```

Apps with no declared `views` keep the current full-width layout — the `AppNavPane` is
not rendered and the `<main>` retains its full width.

### `AppNavPane` component

**File:** `frontend/src/app/shell/AppNavPane.tsx` (shell chunk — **not** lazy-loaded)

**Props:**

```ts
interface AppNavPaneProps {
  app: AppDefinition;   // provides nameKey and views
  collapsed: boolean;
  onToggleCollapsed: () => void;
}
```

The `collapsed` boolean is owned by `AppShell` so the content area can reflow on toggle.

**Expanded state (~250px wide):**

- Collapse toggle at top: chevron-left icon, accessible label `t("Shell:NavCollapse")`,
  calls `onToggleCollapsed`
- Each view entry (in declaration order from `app.views`):
  - Icon (24 px) + localized label via `t(view.nameKey)`, left-aligned
  - Full-row interactive; `<Link to={view.path}>` for entries without children
  - Active entry (pathname starts with `view.path`): filled accent bar on left edge +
    `aria-current="page"`
  - Entry with `children`: chevron-right on right edge; click expands/collapses the
    sub-list in place and does **not** navigate
  - Long labels: `truncate` class + `<Tooltip>` wrapping the label text (never wraps to
    a second line)

**Collapsed / icon-rail state (~56px wide):**

- Expand toggle at top: chevron-right icon, accessible label `t("Shell:NavExpand")`,
  calls `onToggleCollapsed`
- Each entry: icon only, centered; `<Tooltip>` showing `t(view.nameKey)` on hover/focus
- Click on entry without children: navigates (same as expanded)
- Click on entry **with** children while collapsed: calls `onToggleCollapsed()` to
  expand the pane (does not silently do nothing; flyout variant deferred to a future PBI)

**Narrow viewports (`< md`, `< 768 px`):**

- Pane is forced to icon-rail regardless of the user's expanded preference
- The toggle remains interactive so users on a narrow device can opt in to expanded view

**Markup structure:**

```html
<nav aria-label="{localizedAppName} navigation">
  <button aria-label="{t('Shell:NavCollapse')}" …>…</button>
  <ul>
    <li>
      <a href="/admin/users" aria-current="page">…</a>
    </li>
    <li>
      <a href="/admin/roles">…</a>
    </li>
  </ul>
</nav>
```

**Keyboard navigation:** `Tab` reaches every entry and the collapse toggle in DOM order;
`Enter`/`Space` activates the focused element. Active entry carries `aria-current="page"`.

### `AppView` data model extension in `apps.config.ts`

```ts
export interface AppView {
  /** Localization key for the view label. */
  nameKey: string;
  /** Route path (exact or prefix) for active-state matching and navigation. */
  path: string;
  icon: LucideIcon;
  /** Sub-items for expandable categories. No app uses this in this PBI;
   *  support must exist in the component so Transport can adopt it later. */
  children?: readonly AppView[];
}
```

`AppDefinition` gains one optional field:

```ts
views?: readonly AppView[];  // absent → no pane, full-width layout
```

Admin app entry (in `apps.config.ts`):

```ts
{
  id: "admin",
  nameKey: "App:Administration",
  path: "/admin",
  domains: ["Platform"],
  icon: Shield,
  tileClass: "bg-gray-700",
  views: [
    { nameKey: "Administration:Users", path: "/admin/users", icon: Users },
    { nameKey: "Administration:Roles", path: "/admin/roles", icon: ShieldCheck },
  ],
}
```

(`Users` and `ShieldCheck` are existing Lucide icons; see assumption 6.)

### `AdministrationApp` (simplified)

After removing the tab bar, `AdministrationApp` retains only:

1. Redirect: `/admin` or `/admin/` → `/admin/users` (same as now)
2. Pathname switch: `/admin/roles` renders `<RolesView />`; everything else renders
   `<UsersView />`

No navigation chrome remains inside the component; the pane is entirely the shell's
responsibility.

### All UI states

| State | Users/Roles views | Pane |
|---|---|---|
| Loading | existing loading skeleton (unchanged) | static from config — no async |
| Empty | existing empty state (unchanged) | always renders (two entries) |
| Error | existing error handling (unchanged) | unaffected |
| Permission-denied | existing permission-denied card (AC 8, unchanged) | entry stays visible; pane never empties due to permissions |

### Optimistic updates

Not applicable — the pane contains only navigation (no mutations).

## Test risk analysis

| Part | Risk class | Rationale |
|---|---|---|
| `AppNavPane` new component | **critical** | Part of the horizontal Platform shell; every domain's app renders inside `AppShell`. A regression here breaks all apps simultaneously. |
| `AppShell` layout change | **critical** | Horizontal platform shell (domain map §3.1). Content reflow affects every app. |
| Collapse toggle + reflow | **high** | Layout correctness in both states and on viewport resize; screenshot regression tests recommended per performance-budgets §Web frontend |
| `AdministrationApp` tab removal | **medium** | Behavioural change scoped to one app; covered by the existing test suite from #8 |
| Keyboard navigation / a11y | **medium** | Functional correctness testable with focused unit + E2E tests; no cross-app risk |
| `apps.config.ts` extension | **low** | Additive data-only change; no existing app behaviour changes |
| Localization key additions | **low** | Two additive keys in Platform resource; no existing key removed |

### E2E coverage

The §4.5 matrix mandates E2E tests for critical-risk parts. `AppNavPane` and `AppShell` are both critical-risk.

**Delivered:** Playwright (`@playwright/test`) is added to the project as a dev dependency. The test file `frontend/e2e/shell-nav.spec.ts` covers the mandated minimum journey (login → `/admin/users` → click Roles → assert `/admin/roles` renders) plus pane-collapse toggle and the full-width layout path for apps without views. Config lives in `frontend/playwright.config.ts`.

Component tests (Vitest + RTL, jsdom) remain the fast inner loop; the Playwright suite runs against a live server and exercises the TanStack Router navigation lifecycle in a real browser.

## Flag & rollout plan

`DEVIATION(constitution-4): no customers yet, confirmed by PO in issue #18`

No feature flag is required. The change ships on merge and deployment.

Canary activation order: not applicable — no customers on the platform.

Existing tenant data: not applicable — frontend-only, no schema or data migration.

## Cost & SLO impact

- **No new API calls.** `AppNavPane` derives its entries entirely from `apps.config.ts`
  (static compile-time data). No request added to the critical-render path.
- **Shell bundle delta.** `AppNavPane` ships in the shell chunk. Estimated addition:
  < 4 KB minified+gzipped. Lucide icons (`Users`, `ShieldCheck`) are included
  already in the bundle via `WaffleLauncher` and other shell components; the Tailwind
  classes are purge-included. The Administration bundle stays lazy-loaded (AC 10).
- **Performance budgets touched:**
  - *Interactive reads (p95 < 300 ms / LCP < 2.5 s):* the pane renders synchronously
    with the shell; no new async on the critical path. Budget unaffected.
  - *INP < 200 ms:* collapse toggle and link clicks are simple state updates / router
    navigations. No heavy computation.
  - *Web frontend bundle size:* CI bundle-size check will capture the shell chunk delta;
    expected to remain within budget given the < 4 KB estimate.
- **Cloud SQL / Cloud Run / egress:** no change.
- **Per-tenant margin:** no change.

## Assumptions

1. The three-route pattern for Administration (`/admin`, `/admin/users`, `/admin/roles`
   all rendering `AdministrationApp`) is retained as-is; the component simplifies to a
   redirect + pathname switch.
2. `AppView.children` is modelled in the type from day one for forward compatibility, but
   no real consumer exists in this PBI. Tests for the sub-item expansion behaviour use a
   fixture-only `AppDefinition` (no production app declares children).
3. The `md` Tailwind breakpoint (768 px) is the threshold below which the pane is forced
   to icon-rail, consistent with existing shell breakpoints.
4. Collapsed state is local React state in `AppShell` (not persisted: resets on page
   load). Session persistence is explicitly out of scope.
5. Flyout behaviour for collapsed entries with children is out of scope for this PBI.
   Clicking such an entry while collapsed expands the pane.
6. Lucide `Users` and `ShieldCheck` are the icon choices for the Users and Roles views
   respectively. The design team may swap them without requiring a design revision.
7. The pane's `<nav aria-label="…">` label uses the active app's localized name (already
   available via `activeApp.nameKey`). No additional localization key is needed for the
   label.
8. `findAppByPath` already returns the correct `AppDefinition` for all `/admin/*` paths
   via `pathname.startsWith("/admin/")`. No change to that function is needed.
9. `AppNavPane` receives `collapsed`/`onToggleCollapsed` from `AppShell` rather than
   managing its own state, so the shell can reflow the `<main>` width on toggle.

## Security quickscan

- **No new ABP permission definitions.** The pane never hides entries based on
  permissions (AC 8: entries remain visible; the content pane shows the
  permission-denied card exactly as implemented in #8).
- **No user input in the pane.** All paths are static config values; labels come from
  the ABP localization endpoint (a trusted server response). No injection vector.
- **No attack surface change.** No new endpoints, no new client-side data handling, no
  new service-to-service calls.
- **No personal data.** Navigation chrome only — confirmed by PBI privacy note.
- **GDPR art. 15/17:** not applicable.
