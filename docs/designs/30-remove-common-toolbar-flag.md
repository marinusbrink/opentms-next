# Design: Remove UI.CommonToolbar feature flag (#30)

**PBI:** https://github.com/marinusbrink/opentms-next/issues/30
**Status:** draft
**Date:** 2026-08-22

<!-- All nine sections below are mandatory (design §4.2). "Not applicable" is an
     acceptable answer only with a one-line reason; a missing section is not.
     Gate 1 approves this document by merging the PR — after that, implementers
     build exactly this, so vagueness here becomes iteration later. -->

> **DEVIATION(constitution-4): presentation-only change with no flag-off maintenance path;
> canary purpose already served via PBI #27 gate-2 review; orphan flag-off branch has
> ongoing maintenance cost not justified by rollout safety, approved in
> docs/designs/30-remove-common-toolbar-flag.md**
>
> Constitution rule 4 requires new user-visible behaviour to go behind a feature flag.
> This PBI *removes* a flag, not adds one. The PO has accepted the deviation on three
> grounds: (1) this is a presentation-layer-only change — no data, no business logic, no
> external call; (2) the rollout canary purpose was served by PBI #27, which completed its
> full gate-2 review cycle with the new style; (3) removing the flag-off rendering branch
> eliminates an ongoing maintenance burden that would otherwise follow every future
> command-bar change. The rollback path if the new style has a UI defect is a Cloud Run
> revision revert, not a flag flip — deliberately accepted (see §Flag & rollout plan).

## Domain impact

Pure frontend and Platform-module cleanup — no functional domain modules are changed,
no entities, no domain events, no new schema.

**Platform module (horizontal) — removals:**

- `backend/modules/platform/OpenTms.Platform.Application.Contracts/Features/PlatformFeatureDefinitionProvider.cs`
  — Delete the file entirely. `PlatformFeatureDefinitionProvider.Define()` currently
  registers one feature, `UI.CommonToolbar`; no other feature is registered there.
  Deleting the provider removes the feature definition from ABP's registry. ABP will
  no longer include `UI.CommonToolbar` in `GET /api/abp/application-configuration`
  responses.

- `backend/modules/platform/OpenTms.Platform.Application.Contracts/Features/PlatformFeatures.cs`
  — Delete the file entirely. The class exposes two constants (`GroupName = "Platform"`,
  `CommonToolbar = "UI.CommonToolbar"`); after the provider is gone no production code
  references them.

**Backend tests — removals:**

- `backend/test/OpenTms.Application.Tests/Platform/Features/PlatformFeatureDefinitionTests.cs`
  — Delete the file entirely. All three tests assert properties of `PlatformFeatures`
  constants that no longer exist after the files above are deleted. Keeping them would
  be a compile error.

**Frontend component library (shared) — simplification:**

- `frontend/src/components/ui/app-command-bar.tsx`
  — Remove the `useApplicationConfiguration` import, the `isNewStyleEnabled`
  declaration (line 292), and the entire flag-off rendering block (lines 299–351,
  the `if (!isNewStyleEnabled) { return … }` branch that renders the old h-12 bar).
  Move the empty-commands guard (`if (commands.length === 0) return null`) to the
  top of `AppCommandBar` before any other statement, then unconditionally delegate
  to `AppCommandBarNewStyle`. The `selectionLabelKeys` / `isPrimary` props,
  `AppCommandBarNewStyle`, and all sub-components introduced by PBI #27 are unchanged.

**Frontend tests — simplification:**

- `frontend/src/components/ui/__tests__/app-command-bar.test.tsx`
  — Remove the `useApplicationConfiguration` mock setup and the two separate
  `describe` blocks ("flag-off path" and "flag-on path"). Merge into a single
  `describe("AppCommandBar")` block that always exercises the new-style rendering
  path. Remove all test cases that assert the old bar's h-12 class, the old plain
  Button rendering, or the mock switching between flag values.

**No cross-domain events.** The dependency arrow remains: callers (admin screens) →
shared UI component. Platform removes a definition; no domain module is affected.

## API contract

No new HTTP endpoints and no endpoint changes. The only observable API difference is
that `GET /api/abp/application-configuration` will no longer include a
`UI.CommonToolbar` key in `features.values` once the `PlatformFeatureDefinitionProvider`
is removed. This is a non-breaking removal: the frontend no longer reads that key; no
other consumer is known.

No OpenAPI spec change — the application-configuration endpoint is an ABP framework
endpoint not reflected in `openapi/opentms-next.json`. No typed-client regeneration
is needed.

**Frontend component API — unchanged.** `AppCommandBarProps` (`commands`,
`selectionCount`, `className`) is identical to what PBI #27 shipped. Callers
(`UsersView.tsx`, `RolesView.tsx`) require no changes.

## Migration strategy

There is no schema migration in this release.

**Why:** The `UI.CommonToolbar` feature flag values are stored in ABP's shared
`AbpFeatureValues` table (managed by the ABP framework, not by a module migration).
Removing the feature definition from the provider does not drop that table or any
column — ABP simply ignores rows for unknown feature names. Per the intake assessment,
existing per-tenant `AbpFeatureValues` rows with `Name = 'UI.CommonToolbar'` become
orphan rows: harmless, invisible to the application, but untidy.

**Contract step (later release) — orphan-row cleanup:**

The cleanup is deferred to a subsequent release, consistent with constitution rule 2
(expand/contract). It runs as an **idempotent DbMigrator step** — not a Hangfire job,
not manual SQL — so it runs automatically on every environment in sequence (host + all
tenant databases) and produces a per-run log without requiring operator intervention.

Implementation: add a `CleanupOrphanFeatureValues` method to `OpenTms.DbMigrator`'s
`DbMigratorHostedService` that executes:

```sql
DELETE FROM "AbpFeatureValues"
WHERE "Name" = 'UI.CommonToolbar';
```

Idempotency is guaranteed because the `DELETE` is a no-op when no rows match — there
is no state to check first. The method is called after all migrations complete, logs the
row count deleted per database (structured log, no PII), and continues past zero-row
results without error. If a tenant database is unreachable, the migrator follows its
existing per-tenant error-continuation policy and logs the failure.

The release manager schedules this cleanup PR at least one release after this design
merges and deploys, per constitution rule 2.

## UI design

**Screens affected:** None visually — the Office-365-style command bar introduced by
PBI #27 is already the correct final appearance. This PBI removes the toggle, not the
style.

**Component change — `AppCommandBar`:**

Before (simplified):

```tsx
const isNewStyleEnabled = appConfig?.features?.values?.["UI.CommonToolbar"] === "true";
if (!isNewStyleEnabled) {
  return <OldH12Bar commands={...} />;   // flag-off path — deleted
}
if (commands.length === 0) return null;
return <AppCommandBarNewStyle ... />;
```

After:

```tsx
if (commands.length === 0) return null;
return <AppCommandBarNewStyle ... />;
```

**States:** The component already handles all required states through its sub-components
(loaded with commands, empty/null guard, disabled commands). No new states are introduced
or removed. Loading and permission-denied states are handled at the screen level
(`UsersView`, `RolesView`) before the command bar is rendered.

**Optimistic updates:** Not applicable — the command bar is a pure presentational
dispatcher; it triggers mutations in its callers but manages no local state and performs
no server interaction.

## Test risk analysis

| Part | Risk class | Rationale |
|---|---|---|
| `PlatformFeatureDefinitionProvider` removal (Platform layer) | **Critical** | Platform is the horizontal floor; any regression there affects every module. Design §3.1: Platform changes are risk class critical by default. |
| `AppCommandBar` component simplification | **Critical** | Shared library component consumed by every screen that carries a command bar; a regression is immediately user-visible across all admin views. |
| `PlatformFeatureDefinitionTests` deletion | **Medium** | Test-code removal; risk is leaving a gap, not introducing a defect. The gap is accepted: the constants the tests covered are deleted. |
| Orphan-row cleanup step (DbMigrator, later release) | **High** | Touches the host database and every tenant database; incorrect SQL or a missing idempotency guard could cause data loss or a failed migration run. The idempotent `DELETE WHERE Name = 'UI.CommonToolbar'` pattern mitigates this to high rather than critical. |

**Test engineer guidance:**

- Verify `AppCommandBar` always renders the Office-365-style bar regardless of what
  `useApplicationConfiguration` returns (or when it returns `undefined`).
- Verify `UsersView` and `RolesView` render correctly with the simplified component.
- Verify `GET /api/abp/application-configuration` no longer includes `UI.CommonToolbar`
  in `features.values` after the backend change deploys.
- For the later-release DbMigrator step: assert the cleanup runs without error on a
  database with orphan rows, produces the expected deletion count in logs, and is a
  no-op (zero rows deleted, no error) on a database with no orphan rows.

## Flag & rollout plan

**No feature flag.** This PBI removes the `UI.CommonToolbar` flag; the new-style bar
becomes unconditional. Per the PO-declared DEVIATION(constitution-4) above, no
replacement flag is introduced.

**Activation order:** Not applicable — the change takes effect for all tenants the
moment the new frontend bundle is served. Traffic splitting during Cloud Run deployment
means some requests may briefly receive the old bundle (which reads the now-absent
feature flag key as `undefined`, falling back to the old bar style without crashing)
and some the new bundle (which unconditionally renders the new style). This mixed-bundle
window is the same as any other frontend deploy; the intake confirms no crash on either
side.

**Rollback path:** Cloud Run revision revert. There is no flag to flip off; reverting
to the previous revision restores the flag-gated component. The PO has explicitly
accepted this as the rollback mechanism.

**Existing tenant data:** No tenant data is affected by this release. The orphan
`AbpFeatureValues` rows are addressed in the later-release contract step described in
§Migration strategy.

## Cost & SLO impact

**GCP resources:** No change. No new Cloud SQL tables, no additional egress, no change
to Cloud Run min instances, no external API calls.

**Performance budgets:**

- *Interactive reads (p95 < 300 ms):* Unaffected. The command bar is rendered
  client-side from already-fetched application-configuration data; removing the flag
  check eliminates one boolean read from the features map, an immeasurably small
  improvement.
- *Web frontend (LCP < 2.5 s, INP < 200 ms):* The removed `if (!isNewStyleEnabled)`
  branch and the `useApplicationConfiguration` read-path are eliminated from the render
  cycle. Effect on bundle size is negligible (≈ 40 lines removed); effect on INP is
  marginally positive.
- *Availability (99.9% / month):* Unaffected — the command bar is a UI decorator with
  no server-side dependency of its own.

**Per-tenant margin:** No change. No new infrastructure, no new external calls.

## Assumptions

1. **`PlatformFeatureDefinitionProvider` is the sole registrant of `UI.CommonToolbar`.**
   Confirmed by reading the file: only one `group.AddFeature(PlatformFeatures.CommonToolbar, …)`
   call exists, and no other provider in the codebase references `PlatformFeatures.CommonToolbar`.

2. **`PlatformFeatureDefinitionProvider` registers no other feature definitions.**
   Confirmed by reading the file: `Define()` contains exactly one `group.AddFeature` call.
   Deleting the file removes no active feature definitions other than `UI.CommonToolbar`.

3. **The flag-off rendering path (lines 299–351 of `app-command-bar.tsx`) is fully dead
   code after PBI #27 merges.** PBI #27 set `UI.CommonToolbar = "true"` for all
   tenants as part of its rollout. The frontend always reads `"true"` (flag on) from
   live data, so the `if (!isNewStyleEnabled)` branch never executes in production.
   No other caller of `AppCommandBar` passes a flag-off signal.

4. **`UsersView.tsx` and `RolesView.tsx` are the only callers of `AppCommandBar`.**
   Confirmed by grep across `frontend/src` — no other file imports `app-command-bar`.

5. **ABP ignores `AbpFeatureValues` rows for unknown feature names without error.**
   Standard ABP framework behaviour: the feature management system resolves values
   only for registered feature names; unregistered names are silently excluded from the
   `application-configuration` response. No exception or log error is produced.

6. **The old bar's h-12 class and plain-Button rendering are visually distinct from the
   new Office-365-style bar.** They cannot be confused at the component level — the
   flag-off branch returns a different JSX tree with a different container class. After
   this PBI neither branch remains; both are replaced by the single unconditional path.

7. **The DbMigrator's per-tenant error-continuation policy already exists** (documented
   in `CLAUDE.md`). The orphan-row cleanup step relies on this; no new error-handling
   mechanism needs to be added.

8. **No localization keys are added or removed.** The `Shell:CommandBarMore` and
   `Shell:CommandBarMoreLabel` keys introduced by PBI #27 remain; they are used by the
   new-style bar that continues to operate. No keys introduced by the flag-off path
   exist (the old bar used no localization keys of its own).

## Security quickscan

**New permissions:** None. No ABP permission definitions are added or changed.

**Input validation:** Not applicable. This change removes a feature flag check from a
presentational component; it introduces no new user input boundaries.

**Attack surface:** No change. No new endpoints, no new data flows, no new external
integrations.

**Personal data (GDPR):** None. The command bar is a UI navigation component; it
renders action buttons and handles click events. It reads no personal data and stores
nothing. No GDPR art. 15 or art. 17 paragraph is required (confirmed in intake).
