# Design: Platform grid component — OpenTmsGrid (#6)

**PBI:** https://github.com/marinusbrink/opentms-next/issues/6
**Status:** draft
**Date:** 2026-08-13

<!-- All nine sections below are mandatory (design §4.2). "Not applicable" is an
     acceptable answer only with a one-line reason; a missing section is not.
     Gate 1 approves this document by merging the PR — after that, implementers
     build exactly this, so vagueness here becomes iteration later. -->

## Domain impact

**Module affected: Platform only.**

This PBI is horizontal infrastructure. No business-domain module is touched. The
dependency arrow points strictly downward: Platform adds the grid contract and the
settings service; domain modules will reference Platform's `Application.Contracts` to
consume the contract in their own list endpoints.

### New files — Backend (`OpenTms.Platform.Application.Contracts/Grid/`)

| Type | Name | Notes |
|---|---|---|
| C# record | `GridRequest` | Shared request DTO for every server-side grid block fetch |
| C# record | `GridResponse<T>` | Generic response wrapper; typed per-consumer |
| C# record | `SortModel` | `(string ColId, string Sort)` — "asc" or "desc" |
| C# record | `ColumnFilterModel` | Per-column filter; `FilterType` ∈ `text\|number\|date` |
| C# record | `GridSelectionDto` | Bulk-selection contract: explicit id list **or** filter-based |
| C# record | `GridSettingsDto` | Persisted column state + active sort per user per grid-id |
| C# record | `ColumnStateDto` | Per-column: `ColId`, `Visible`, `Order`, `Width?` |
| C# interface | `IGridSettingsAppService` | Two-method contract: `GetAsync` / `SetAsync` / `ResetAsync` |

### New files — Backend (`OpenTms.Platform.Application/Grid/`)

| Type | Name | Notes |
|---|---|---|
| C# class | `GridSettingsAppService` | Implements `IGridSettingsAppService` via `ISettingManager` |
| C# class | `GridSettingDefinitionProvider` | Declares the ABP setting definition for the grid settings key |
| C# class | `GridSettingsController` | Conventional ABP controller; exposes `IGridSettingsAppService` |

### New files — Frontend (`src/components/ui/`)

| File | Purpose |
|---|---|
| `opentms-grid.tsx` | `OpenTmsGrid<TRow>` component — AG Grid Enterprise SSRM wrapper |
| `opentms-grid-toolbar.tsx` | Wildcard search input + column chooser + reset-to-defaults button |
| `opentms-grid-footer.tsx` | Total-count and filtered-count footer row |

### Settings storage

Grid settings are stored via ABP's `ISettingManager` at `UserLevel` scope using the
`ForCurrentUserAsync` API. No new EF Core entity or table is introduced; the existing
ABP-managed `AbpSettings` table (part of the ABP Framework core schema in every
database) holds the values.

Setting name pattern: `OpenTms.Platform.Grid.Settings.{gridId}`.

`gridId` is validated as `^[a-zA-Z0-9_.-]{1,100}$` before being used as a key suffix.

### Events

None. This PBI adds no domain events and requires no cross-module communication.

### Future grouping

`GridRequest` includes `RowGroupCols` (list of column ids to group by) and `GroupKeys`
(the path of group keys for a child block request) as first-class fields, defaulting to
empty lists. The platform contract ships them now so that a future grouping PBI adds no
breaking changes to `GridRequest` or `GridResponse<T>`. Consuming grids ignore these
fields until the grouping PBI activates them.

---

## API contract

### Grid block fetch (consumer-owned, contract defined here)

Each consuming grid defines its own endpoint. The endpoint accepts `GridRequest` and
returns `GridResponse<T>` where `T` is the consuming module's row DTO. No shared
Platform endpoint handles grid data — Platform defines only the shapes.

**`GridRequest`**
```json
{
  "startRow": 0,
  "endRow": 100,
  "sortModels": [
    { "colId": "reference", "sort": "asc" }
  ],
  "columnFilters": {
    "status": {
      "filterType": "text",
      "type": "contains",
      "filter": "active"
    }
  },
  "wildcardSearch": "rotterdam",
  "rowGroupCols": [],
  "groupKeys": []
}
```

Constraints (enforced by Platform middleware / model validation):
- `startRow` ≥ 0, `endRow` > `startRow`, `endRow − startRow` ≤ 200
- `wildcardSearch`: max 200 characters; null if absent
- `columnFilters` values: `filter` and `filterTo` each max 500 characters
- `rowGroupCols` and `groupKeys`: max 10 elements each (unused in this PBI; validated for the future)

**`GridResponse<T>`**
```json
{
  "rows": [ /* T[] */ ],
  "totalCount": 84312,
  "filteredCount": 1204
}
```

`filteredCount` equals `totalCount` when no filter is active; the frontend hides the
"filtered" indicator in that case.

**`GridSelectionDto`** — used by consuming bulk-action endpoints (not a grid endpoint itself)
```json
{
  "mode": "FilterBased",
  "explicitIds": [],
  "filterRequest": { /* GridRequest at the moment of select-all */ },
  "excludedIds": ["3fa85f64-5717-4562-b3fc-2c963f66afa6"]
}
```

`mode` ∈ `Explicit | FilterBased`. When `Explicit`, `filterRequest` is null and
`explicitIds` carries the ids. When `FilterBased`, `explicitIds` is empty and
`filterRequest` carries the full current filter state. `excludedIds` carries rows
de-selected after select-all.

### Grid settings endpoints (Platform-owned)

Base route: `/api/platform/grid-settings`

**`GET /api/platform/grid-settings/{gridId}`**
- Auth: authenticated user (no additional ABP permission)
- Response `200`:
```json
{
  "columnStates": [
    { "colId": "reference", "visible": true, "order": 0, "width": 180 },
    { "colId": "status",    "visible": true, "order": 1, "width": 120 }
  ],
  "sortModels": [
    { "colId": "reference", "sort": "asc" }
  ]
}
```
- Response `404`: no saved settings for this grid-id (client falls back to code defaults)

**`PUT /api/platform/grid-settings/{gridId}`**
- Auth: authenticated user
- Request body: `GridSettingsDto` (same shape as the GET response above)
- Response `204`

**`DELETE /api/platform/grid-settings/{gridId}`**
- Auth: authenticated user
- Deletes the saved settings entry; client reverts to code defaults
- Response `204`

### Typed client

After implementation, run `./scripts/generate-openapi.sh && cd frontend && npm run generate:client` to expose the three endpoints in `src/lib/api/client.ts`. The frontend component calls them via the generated client wrapped in TanStack Query.

---

## Migration strategy

### Expand (this release)

1. Add `GridRequest`, `GridResponse<T>`, `GridSelectionDto`, `GridSettingsDto`,
   `ColumnStateDto`, `SortModel`, `ColumnFilterModel` to
   `OpenTms.Platform.Application.Contracts/Grid/`.
2. Add `IGridSettingsAppService` to `OpenTms.Platform.Application.Contracts/Grid/`.
3. Add `GridSettingDefinitionProvider` (registers the ABP setting definition) and
   `GridSettingsAppService` to `OpenTms.Platform.Application/Grid/`.
4. Add `GridSettingsController` (conventional ABP controller) to the HttpApi.Host.
5. Add `ag-grid-enterprise` and `ag-grid-react` (pinned compatible version — see
   Assumption 4) to `frontend/package.json`.
6. Add `OpenTmsGrid`, `OpenTmsGridToolbar`, `OpenTmsGridFooter` to
   `frontend/src/components/ui/`.
7. Regenerate the OpenAPI spec and the typed TS client.

No EF Core migration is required — ABP's `AbpSettings` table already exists in every
tenant database and every host database.

### Contract step

Not applicable. This release is purely additive: new DTOs, a new settings endpoint, and
a new frontend component. Nothing is renamed or removed. No contract cleanup step is
needed in a later release.

---

## UI design

### Component: `OpenTmsGrid<TRow>`

**Props interface:**
```ts
interface OpenTmsGridProps<TRow> {
  gridId: string;
  columnDefs: ColDef<TRow>[];
  fetchRows: (request: GridRequest) => Promise<GridResponse<TRow>>;
  searchableColumns?: string[]; // defaults to visible text columns
  onSelectionChange?: (selection: GridSelectionDto) => void;
  className?: string;
}
```

The component is generic over the row type so each consuming grid retains full type
safety on its column definitions and cell renderers.

### Structure

```
┌─────────────────────────────────────────────────────────┐
│ Toolbar                                                   │
│  [🔍 Search…                        ] [Columns] [Reset]  │
├─────────────────────────────────────────────────────────┤
│ AG Grid (SSRM, infinite scroll)                           │
│  ☐ | Col A     | Col B     | Col C   | …                 │
│  ─────────────────────────────────────────────────────   │
│  ☐ | value     | value     | value   |                   │
│  ☐ | value     | value     | value   |                   │
│  (loads next block on scroll)                             │
├─────────────────────────────────────────────────────────┤
│ Footer: Totaal: 84 312  (Gefilterd: 1 204)               │
└─────────────────────────────────────────────────────────┘
```

**Component reuse** — existing components from `src/components/ui/`:
- `Input` (`input.tsx`) — wildcard search field in the toolbar
- `Button` (`button.tsx`) — "Columns" chooser button, "Reset" button, retry button in error rows
- `Tooltip` (`tooltip.tsx`) — reset button tooltip ("Reset to defaults")
- `Skeleton` (`skeleton.tsx`) — not used in the grid itself (AG Grid provides its own
  loading overlays); may be used in the footer during initial load before `totalCount`
  is known

**Localization**: every user-facing string uses ABP localization keys added to
`OpenTms.Platform.Domain.Shared/Localization/` (both `nl` and `en`):

| Key | nl | en |
|---|---|---|
| `Grid:Search` | Zoeken… | Search… |
| `Grid:Columns` | Kolommen | Columns |
| `Grid:Reset` | Standaard herstellen | Reset to defaults |
| `Grid:Empty` | Geen resultaten | No results |
| `Grid:EmptyFiltered` | Geen overeenkomsten — filter is actief | No matches — a filter is active |
| `Grid:ClearFilters` | Filters wissen | Clear filters |
| `Grid:Total` | Totaal: {0} | Total: {0} |
| `Grid:Filtered` | Gefilterd: {0} van {1} | Filtered: {0} of {1} |
| `Grid:RetryingBlock` | Opnieuw laden… | Retrying… |
| `Grid:RetryBlock` | Opnieuw proberen | Retry |
| `Grid:BlockError` | Fout bij laden — | Failed to load — |

### States

| State | Trigger | Rendering |
|---|---|---|
| **Loading (initial)** | First block fetch in flight | AG Grid built-in loading overlay with localized text `Grid:Empty` skeleton; footer shows `Skeleton` |
| **Loaded** | Block fetch succeeded | Grid rows visible; footer shows `Grid:Total` (+ `Grid:Filtered` if filter active) |
| **Empty (no filter)** | `totalCount === 0`, no active filter | AG Grid `overlayNoRowsTemplate` with `Grid:Empty` |
| **Empty (filtered)** | `filteredCount === 0`, filter active | `Grid:EmptyFiltered` overlay + `Button` labeled `Grid:ClearFilters` that calls `api.setFilterModel(null)` |
| **Block error — auto-retry** | Block fetch failed, first attempt | Inline AG Grid error row showing `Grid:RetryingBlock`; one automatic retry after 2 s |
| **Block error — manual retry** | Auto-retry also failed | Inline error row with `Button` labeled `Grid:RetryBlock` |
| **Repeated failures** | Second manual retry also fails | Toast notification (Base UI `Popup` or equivalent — see Assumption 11); grid row remains with retry button |
| **AG Grid key missing/expired** | `LicenseManager.isLicenseValid()` returns false | Grid renders normally with AG Grid watermark; `logger.error` with structured log `{event: 'AgGridLicenseInvalid'}` — never blocks |
| **Permission-denied** | Not applicable at component level | Consuming grid checks permissions before rendering `OpenTmsGrid`; the component itself is permission-agnostic |

### Column personalization

On mount: `GET /api/platform/grid-settings/{gridId}`. On `404` or settings-parse
failure, fall back silently to code-default column definitions.

On any column-state change (visibility toggle, resize, reorder, sort change):
debounced `PUT /api/platform/grid-settings/{gridId}` with 1 000 ms debounce — the user
sees the change immediately (no perceived latency). Failed save is logged (structured,
not a toast — the user's current session state is correct, only persistence failed).

**Reset to defaults**: `DELETE /api/platform/grid-settings/{gridId}` fires immediately
(no debounce); local state reverts to the code-default column definitions without
waiting for the response (optimistic). If the API call fails, the local revert is
permanent for this session — the next mount re-loads settings and may restore the old
state, which is acceptable.

### Multi-select / select-all

AG Grid `rowSelection: { mode: 'multiRow', selectAll: 'filtered' }`.

When the user checks individual rows, `onSelectionChange` is called with
`{ mode: 'Explicit', explicitIds: [...] }`.

When the user clicks the header checkbox ("select all filtered rows"), `onSelectionChange`
is called with `{ mode: 'FilterBased', filterRequest: <current GridRequest>, excludedIds: [] }`.
Subsequent row de-selections append to `excludedIds`.

### Optimistic updates

The only mutation from this component is grid settings save (PUT) and reset (DELETE).
Reset is optimistic: local column state reverts immediately; the API call runs in the
background. Settings save is silently debounced; no confirmation UI is shown.

---

## Test risk analysis

**Platform-layer rule**: every change in this PBI touches the horizontal Platform layer.
All parts carry **critical** risk class as the baseline per design §3.1. The table below
shows the floor class and the rationale; the test engineer determines specific test
execution.

| Part | Risk class | Rationale |
|---|---|---|
| `GridRequest` / `GridResponse<T>` / `GridSelectionDto` DTOs | **Critical** | Platform contract; a breaking field change breaks every future consumer at once |
| `GridSettingsAppService` — save/load via `ISettingManager` | **Critical** | Tenant isolation: settings must never cross user or tenant boundaries; rely on ABP's `CurrentUser`/`CurrentTenant` context — a misconfiguration leaks preferences across tenants |
| `GridSettingsController` — HTTP layer | **High** | Standard ABP controller pattern; risk is mis-routing or wrong auth guard; covered by integration tests |
| `OpenTmsGrid` React component — block-fetch + SSRM | **Critical** | All future list screens depend on this; a rendering regression is a cross-product regression |
| Empty / error state handling + auto-retry logic | **High** | Reliability path; incorrect retry logic causes infinite loops or silent data loss |
| Column personalization load/save/reset | **High** | Debounce + optimistic reset; race conditions possible if user resets while a debounced save is in-flight |
| AG Grid key validation (watermark path) | **Medium** | Non-blocking by design; test: missing key → watermark rendered, no crash, structured log emitted |
| Wildcard search and per-column filter input | **High** | Input is forwarded to the server; no Platform-level SQL execution, but the contract validation bounds must be tested (max length, empty string handling) |
| Localization strings | **Low** | Additive only; wrong string is visible but not a data or security issue |

---

## Flag & rollout plan

**No feature flag for this PBI.** Library-only — no user-visible screen ships. The
component and the settings endpoint are deployed as inert infrastructure. They become
visible to end users only when a consuming screen (e.g. the Administration app PBI) is
deployed and its own feature flag is activated.

Rollout of the grid contract itself is therefore equivalent to the rollout of the
Platform module deployment:
1. Deploy the Platform module changes (settings endpoint, DTOs, frontend component).
2. Verify settings endpoint health via `/health/ready`.
3. No per-tenant activation required for this PBI.

The first consuming screen's design carries the feature flag and activation order
(health tenant → friendly tenants → all).

---

## Cost & SLO impact

**GCP impact:** negligible.

| Resource | Impact |
|---|---|
| Cloud SQL (platform schema) | No new table. ABP `AbpSettings` table receives one row per user per grid-id per tenant on first settings save. At 50 users × 10 grid-ids = 500 rows per tenant; at 100 tenants = 50 000 rows total — trivial. |
| Cloud SQL (consumer grids) | Not owned by this design. Each consumer's block-fetch query is designed and budget-checked in that consumer's own design doc. |
| Cloud Run | No new min instances. Settings GET/PUT/DELETE are simple single-row operations; sub-millisecond DB time expected. |
| Egress | `GridSettingsDto` payloads are < 5 KB per response. Negligible. |
| External API calls | None. |
| Per-tenant margin | Unaffected — no paid external service involved. |

**Performance budgets touched:**

- **Interactive reads**: `GET /api/platform/grid-settings/{gridId}` — single-row ABP
  settings lookup; expected p95 well within 300 ms. No index needed beyond the ABP
  primary key.
- **Mutations**: `PUT /api/platform/grid-settings/{gridId}` — single-row upsert via
  ABP; expected p95 well within 500 ms. Debounced client-side so server call rate is
  capped at ≈ 1/s per user.
- **Web frontend bundle**: `ag-grid-enterprise` + `ag-grid-react` adds significant
  bundle weight (AG Grid Enterprise is ~800 KB minified pre-compression). The component
  must be **lazy-loaded** via `React.lazy` / dynamic `import()` on routes that use it;
  CI bundle-size budget check will fail on any route that eagerly imports `OpenTmsGrid`.
  The implementer must verify the per-route bundle budget is not exceeded after adding
  the lazy boundary.
- **Availability SLO (99.9 %)**: settings endpoint is non-critical to core flows
  (order creation, invoicing, planning) — a settings GET failure falls back to defaults
  and does not degrade core availability.

---

## Assumptions

1. The AG Grid Enterprise license key is available as the environment variable
   `VITE_AG_GRID_LICENSE_KEY` in the Vite build (frontend). The exact variable name is
   assumed; the implementer must confirm it matches the convention used for other secrets
   in this repo and adjust the design accordingly if different.

2. The license key is provisioned in CI (GitHub Actions) and E2E environments as a
   secret named `AG_GRID_LICENSE_KEY` (injected as `VITE_AG_GRID_LICENSE_KEY` at build
   time). CI asserts key presence; a production build without a valid key fails.

3. ABP's `ISettingManager.GetOrNullForCurrentUserAsync` / `SetForCurrentUserAsync`
   automatically scopes settings to the current tenant through ABP's `ICurrentTenant`
   — no manual tenant filtering is needed. This is relied upon without additional
   wrapping. If this assumption is wrong, a custom tenant-scoped key must be used.

4. An `ag-grid-enterprise` + `ag-grid-react` version exists on npm that is compatible
   with TypeScript 5.9. If the latest AG Grid version's type definitions require TS ≥ 6,
   an older compatible version must be pinned; the implementer must verify this before
   installation and document the pinned version.

5. The AG Grid Enterprise license held by iopentms covers the AG Grid major version
   selected by the implementer. If the license covers only a specific major version, the
   implementer must install that version.

6. Base UI's `Popup` component (or an equivalent from `@base-ui/react`) is suitable for
   the failure toast notification. If no toast-capable component exists in the current
   library, the implementer adds the smallest viable Base UI component — not a new
   dependency — and it becomes part of `src/components/ui/`.

7. ABP's `AbpSettings` table exists in every tenant database and in the host database as
   part of the initial ABP scaffold migration (confirmed: the Initial migration for each
   module schema exists; ABP core tables including `AbpSettings` are created by the ABP
   framework migrations in the host DB and replicated to each tenant DB by the
   DbMigrator).

8. Columns removed from the API after a user persisted them are silently ignored: the
   `OpenTmsGrid` component filters out saved `ColumnStateDto` entries whose `colId`
   does not appear in the current `columnDefs` prop before applying the persisted state.

9. Saved column widths are restored in pixels without responsive re-scaling. A user on a
   different screen resolution sees the same saved pixel widths — this is acceptable for
   the initial implementation.

10. The active sort included in `GridSettingsDto.sortModels` is treated as the default
    sort on load. If the sorted column no longer exists in the current `columnDefs`, the
    sort is silently discarded (no error, grid defaults to unsorted).

11. `GridRequest.endRow − GridRequest.startRow` is capped at 200 rows per block at the
    Platform contract level (enforced by `[Range]` validation on the controller). Consuming
    grids may set a lower AG Grid `cacheBlockSize`; they cannot exceed 200 rows per
    block without a deliberate contract change.

12. `filteredCount` in `GridResponse<T>` must be computed by the consuming grid's
    block-fetch query. Platform provides the field in the contract; it does not compute it.
    A consuming grid that cannot compute `filteredCount` cheaply may return `totalCount`
    as a documented deviation in its own design, with an explanation of why exact counts
    are not feasible within the 300 ms budget.

---

## Security quickscan

**New ABP permission definitions:** None. `GridSettingsAppService` requires only an
authenticated session (`[Authorize]` with no policy argument). Users can only read and
write their own settings; there is no admin read path for other users' grid settings.

**Input validation boundaries:**

| Input | Validation | Location |
|---|---|---|
| `gridId` (path param) | `^[a-zA-Z0-9_.-]{1,100}$` regex; rejected 400 if invalid | `GridSettingsController` model binding attribute |
| `GridRequest.startRow` | `[Range(0, int.MaxValue)]` | DTO attribute |
| `GridRequest.endRow` | `endRow > startRow && endRow − startRow ≤ 200` | Custom `IValidatableObject` on `GridRequest` |
| `GridRequest.wildcardSearch` | `[MaxLength(200)]` | DTO attribute |
| `ColumnFilterModel.filter` / `filterTo` | `[MaxLength(500)]` | DTO attribute |
| `GridSettingsDto` (PUT body) | Max 50 column states; `colId` matches `^[a-zA-Z0-9_.-]{1,64}$` | Custom validation in `GridSettingsAppService` |

**SQL injection**: Not applicable at the Platform level. `GridRequest` is a shape
definition; Platform does not execute the filter against a database. SQL safety is the
responsibility of each consuming grid's EF Core query builder (parameterized queries by
default).

**Attack surface changes:**

- `GET /api/platform/grid-settings/{gridId}`: reads own settings only; no cross-user
  access path. ABP `ISettingManager` with `CurrentUser` scope enforces this.
- `PUT /api/platform/grid-settings/{gridId}`: writes own settings only. No cross-user
  write path.
- `DELETE /api/platform/grid-settings/{gridId}`: deletes own settings only.
- AG Grid SSRM block-fetch: implemented by each consuming module with that module's own
  authorization; Platform does not open a generic data-fetch endpoint.

**GDPR (art. 15 / art. 17):** Not applicable. Grid settings contain only column layout
preferences (column ids, widths, sort direction) — no personal data. A future export PBI
that includes columns with personal data must include the GDPR art. 15/17 paragraph in
that design.
