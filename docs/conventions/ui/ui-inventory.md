# UI inventory: existing OpenTMS Angular app

Phase 1 evidence for the OpenTMS Next UI conventions. This file records what the existing app **does**, not what it should do. Rules are derived from it in phase 2; decisions a human must make are in [open-decisions.md](open-decisions.md).

- **Source:** `OpenTMS/angular` (Angular 17.3). Paths below are relative to `angular/src/app/` unless they start with `src/` or `aspnet-core/`.
- **Size:** 425 component templates, 386 app style files, 853 TS files.
- **Method:** a read-only sweep with grep and small scripts over `*.html`, `*.ts`, `*.scss`, `*.less` and `*.css` (generated `service-proxies` excluded), plus reading of the shared building blocks. Counts are reproducible but approximate (±a few): multi-line templates and shared templates make exact counting impossible. "N files / M occ" means distinct files and total occurrences.
- **Verdicts:** every deviation is labelled **legacy** (noise to drop), **deliberate** (an exception to keep) or **gap** (no convention exists; something is missing).
- **Date of sweep:** 2026-10-09.

---

## 0. The big picture: three UI generations

Most inconsistency comes from three layers that coexist and follow the folder structure. Within each layer the app is fairly consistent.

| Generation | Where | Recognisable by | Status |
|---|---|---|---|
| **G1. ABP / ASP.NET Zero / Metronic** | `admin/**`, `shared/layout/**`, `main/dashboard`, `shared/utils` | `m-subheader` page title, `m-portlet`, Bootstrap `form-group` + `form-control`, `bsModal`, ngx `tabset`, PrimeNG `p-table`, `<validation-messages>`, `[buttonBusy]`, `[busyIf]`, `this.message.confirm` (sweetalert), Font Awesome | Inherited template code. Legacy for everything except host administration. |
| **G2. Filogic TMS** | transport, master-data, finance, planboard, portals, wizards | MatDialog, `base-dialog-template` + `entity-operations-bar` ribbon, `base-header`, `base-ok-cancel-footer`, `mat-form-field appearance="outline"`, `mat-tab-group`, ag-grid via `FilogicGridBase`, `ag-preferences-panel`, `this.notify.*` toasts, `ConfirmDialogComponent`, Material Icons | **The dominant generation; the main source for rules.** |
| **G3. Modernised islands (2025-2026)** | `finance/dialogs/edit-invoices-dialog`, `finance/invoice-delivery`, `finance/dialogs/add-transport-order-dialog`, `transport/transport_order/financial`, `master-data/discounts-surcharges-wizard`, `master-data/dialogs/edit-surcharges-and-discounts-dialog`, price-list redesign, vehicle outline, `wizards/pdf-*`, `wizards/chainfill-*` | white / `#fafafb` section cards with 10px radius, `rgba(0,0,0,.06)` borders and brand-colour captions, CSS grid layouts, `color-mix()`, a 4px spacing grid, local token sets `--plt-*` / `--vop-*` | Deliberate new direction ("invoice-detail convention"), but only about 6 to 10 files. |

**The new app already exists.** `opentms-next/frontend` has a shell, a grid wrapper, a command bar and two admin screens. Its precedents are listed in §13, because several decisions have to choose between "what the old app does" and "what the new app already does".

---

## 1. Design tokens

### 1.1 Colour: brand

- **`--filogic-base-color` is the only real cross-app token.** It has **485 uses in 264 files**.
  - Defined per Metronic theme bundle in `src/assets/metronic/src/sass/demo/<theme>/base/_page.scss:59-63`, together with `--filogic-base-transparent-color` (16 uses), `--filogic-base-hover-color` (12 uses) and `--ag-alpine-active-color`.
  - Newer code derives shades with `color-mix(… var(--filogic-base-color) …)` (21 uses).

| Theme | base | transparent | hover |
|---|---|---|---|
| default | `#fba91a` (orange) | `#fba81a44` | `#cc8a16` |
| red | `#e20613` | `#e2061545` | `#ba2731` |
| green | `#006847` | `#14c4554c` (different hue: bug) | `#169545` |
| blue | `#38a8c6` | (none) | `rgb(110,220,250)` |
| custom | the tenant setting `App.UiManagement.CustomColor`, applied at runtime in `src/root.component.ts:27-32` | base + `45` | (none) |

- **How the theme is chosen:** the theme is a **tenant setting**, `App.UiManagement.Theme` (default `"default"`, `aspnet-core/src/FiLogic.Core/Configuration/AppSettingProvider.cs:76`), injected as `themes.<theme>.min.css` by `src/shared/helpers/LocalizedResourcesHelper.ts:45`.
- **Material** gets `material-<theme>-theme` (`app.component.ts:139`, palettes in `src/assets/material/defaultMaterialTheme.scss`). For `custom` the Material primary is **grey 500**, not the tenant colour (gap).
- **Where the brand is applied:**
  - dialog header bars
  - tab ink
  - ribbon icons
  - context-menu icons
  - grid row selection and range (`defaultMaterialTheme.scss:15-23`)
  - `.btn-primary` forced with `!important`
  - focus rings `0 0 2px 1px var(--filogic-base-color)`
  - section captions in G3 dialogs
- **Deviation: `#fba91a` hard-coded** 22 times in 9 style files and 4 times in TS (`los`, `map`, `wizards/pdf-*`, `planboard-map.service.ts:122`). The `--plt-brand` and `--vop-brand` tokens also hard-code it. These screens stay orange for red, green, blue and custom tenants. **Legacy / bug.**

### 1.2 Colour: semantic

- **Three conflicting palettes are loaded**, and none of them is mapped to the brand:
  - **Bootstrap 4** `:root`: primary `#007bff`, success `#28a745`, info `#17a2b8`, warning `#ffc107`, danger `#dc3545`.
  - **Metronic** state map (`framework/_config.scss:76-120`): brand `#716aca`, success `#34bfa3`, danger `#f4516c`, warning `#ffb822`, info `#36a3f7`.
  - **Material**: warn is deep orange.
- **Template usage is small:** `m--font-success` 12, `m--font-danger` 9, `text-danger` 8, `text-muted` 9. CSS-variable use: `--danger` 29, `--secondary` 18.
- **Error / required red** is hard-coded `#ff0000` 24 times in 23 files, plus `red` in the Material field-error override (`src/assets/material/overrides/mat-form-field-overrides.scss:55-63`).
- **Success green** `#28a745` appears 11 times in TS (dashboard, load-factor renderer).
- **Verdict:** there is no semantic colour system. Danger and success are "some red" and "some green". **Gap.**

### 1.3 Colour: status (business data)

- **Status colours are tenant-configurable**, not designed. Each state has a `Background` + `FontColor` pair of settings named `App.ColorSettings.*`, edited in `admin/tms-settings/tms-settings.component.ts:160-215`. All defaults are empty (`AppSettingProvider.cs:359+`).
- **States covered:**
  - Transport order `OrderState`: PendingAcceptance, Open, Canceled, ReadyForInvoicing, Invoiced, Archived.
  - `TaskStatus`: Unknown, Requested, Accepted, Planned, InProgress, Completed.
  - Transport file and order `Status`: InProgress, Expected, Completed.
  - Also Warnings, LoadFactor and vehicle-planboard items.
- **Consumers:**
  - grid renderers `global/ag-orderState-cell-renderer`, `ag-taskStatus-cell-renderer`, `ag-status`, `ag-loadFactor-cell-renderer` (the last hard-codes `#dc3545` / `#28a745`)
  - the planboard Gantt (`planboard/vehicle-planboard-gantt/vehicle-planboard-gantt-color-settings.service.ts:56+`, which maps them to `--status-<state>-background`)
- **Flag / highlight colours** (user-chosen markers on rows) are red, green, blue, purple, brown, grey, `#ff8800` and `#ff00f0`. The set is duplicated in 6 files (`global/filogic-grid-base/filogic-grid-base.scss`, `set-flags-dialog`, `components/flags`, `components/ag-flags`, `list-controls-bar`, `set-highlight-color-dialog`).
- **Map route palette** (TS): `#1971c2`, `#2f9e44`, `#9c36b5`, `#e8590c`, `#0c8599`, `#c2255c` (`services/planboard-map.service.ts:126`).

### 1.4 Colour: neutrals

- **982 hex occurrences, 177 distinct.** The neutrals have no scale: about 15 near-duplicate greys.

| Value | Occ / files | Used for |
|---|---|---|
| `#ffffff` (+ `#ffff` copy-paste 24) | 302 / 113 | surfaces, text on brand |
| `#f4f4f4` | 35 / 28 | detail-screen panels (TO / TF details) |
| `#d6d5d5` | 32 / 19 | dialog borders and dividers |
| `#f3f2f1` | 17 / 16 | grid footer rows, toolbar |
| `#fafafb` | 16 / 6 | **G3 section background** |
| `rgba(0,0,0,.12)` = `#0000001f` | 67 + 53 borders | Material divider; the field outline `$mdc-form-border-color` |
| `rgba(0,0,0,.06)` | 21 | G3 section border |
| `#5f6368`, `#6c757d`, `#999` | 15 / 15 / 12 | secondary / muted text |

- Other greys: `#f5f5f5`, `#fafafa`, `#f9f9f9`, `#f0f0f0`, `#eee`, `#e0e0e0`, `#ddd`, `#d9d9d9`, `#d4d4d4`, `#ccc`, `#a2a1a1`.
- **Verdict:** legacy noise. Only `#fafafb` / `rgba(0,0,0,.06)` / 10px radius (G3) and `rgba(0,0,0,.12)` (field outline) are used with intent.

### 1.5 Typography

**Fonts are split.**
- Metronic sets `html, body { font-family: Poppins; font-size: 14px }`, or 13px on tablet and mobile (`framework/components/base/normalize/_normalize.scss:17-56`).
- **Material fields, dialogs and ag-grid** use `var(--font-family-default)`, the Bootstrap system stack (`-apple-system, "Segoe UI", Roboto…`; `defaultMaterialTheme.scss:18,38`; 61 declarations in 41 files).
- Most of the G2 UI the user sees is therefore in the **system font**, and Metronic chrome is in Poppins. Long-standing, probably unintended.
- G3 islands add DM Sans (vehicle outline) and JetBrains Mono (price-list redesign, numbers).

**Sizes:** 664 declarations, 70 distinct values. The de facto scale is **11 / 12 / 13 / 14 / 16 / 18 / 20 px**.

| px | 12 | 13 | 14 | 20 | 18 | 11 | 16 |
|---|---|---|---|---|---|---|---|
| occ | 97 | 68 | 68 | 53 | 47 | 47 | 38 |

Fixed points:

| Element | Size / weight |
|---|---|
| base text | 14px |
| Material field label | 12px (`mat-form-field-overrides.scss:12,81,155`) |
| dialog header title | 15px / 600 (`global/base-dialog-template/base-dialog-template.component.scss:4-17`) |
| section caption (h6) | 12–13px / 600 in brand colour (G3) |

**Weights:** `600` 118, `500` 91, `700` 55, `400` 42.

**Headings:** `h6` is the de facto section caption (150 uses). `h3` 56 is mostly the admin subheader. There are no Bootstrap `.h*` classes. Line-height has 38 distinct values and no scale.

### 1.6 Spacing

**Two grids coexist.**
- **Legacy 5px grid.** Margin `10px` 281, `5px` 199, `15px` 130, `20px` 117, `25px` 88. It also comes as home-grown utilities in `src/styles.css:135-215`: `margin-left-10` 189 uses, `margin-top-10` 53, the set `margin-*-{0,5,10,15,20,30,40}` and `padding-*-{0,5,10,20}`.
- **G3 4px grid.** `gap` 8px 36, 10px 31, 6px 28, 12px 24, 4px 16.

Other observations:
- Bootstrap spacing utilities are almost unused (62 in total).
- There is no shared spacing variable.
- The 28 master-data dialog SCSS files each copy-paste `.entity { … }` / `.entity__row` with small differences. This is the root cause of padding drift between dialogs (e.g. `master-data/dialogs/edit-countries-dialog/edit-countries-dialog.component.scss`). **Legacy.**

### 1.7 Radii, shadows, borders

**Radius:** `5px` 59, `4px` 39, `50%` 28, `8px` 28, `10px` 12, `999px` 14 (pills), `6px` 5.

| Element | Radius |
|---|---|
| Material field | 4px (`mat-form-field-overrides.scss:95`) |
| dialog header | 6px top |
| G3 section | 10px |

**Shadow:** 95 declarations, 62 distinct, so there is no scale. Recurring values:
- the Metronic portlet `0 1px 15px 1px rgba(69,65,78,.1)`
- the brand focus ring
- a copy of Material elevation-24 for dialogs

**Border:** `1px solid rgba(0,0,0,.12)` (53) is the de facto hairline. `2px solid var(--filogic-base-color)` (23) marks the active or selected state.

### 1.8 Breakpoints

- **Desktop-first; not responsive.** Only 40 `@media` queries, 30 of them distinct.
- Only 992px and 768px line up with Bootstrap (sm 576, md 768, lg 992, xl 1200; Metronic adds 1400 and 1600).
- **Deliberate in practice:** TMS back-office users work on desktop. The portals (self-service, carrier, distribution) are the only mobile-relevant screens.

### 1.9 Icons

| Set | Uses in templates | Status |
|---|---|---|
| **Material Icons / Symbols** (`<mat-icon>` 418 in 135 files, plus `<i class="material-icons">` 64) | dominant, 114 distinct | **standard (G2)** |
| Font Awesome 4 `fa fa-*` | 274 in 109 files | legacy. About 135 of these are the three detail-tab icons `fa-paperclip`, `fa-id-card-o` and `fa-file-text-o` |
| Line Awesome | 34 | legacy |
| Flaticon | 12, plus 19 menu icons in `shared/layout/nav/app-navigation.service.ts` | legacy (shell) |
| simple-line, PrimeIcons | 7 / 0 | dead |
| Custom SVG masks (`toolbar/toolbar-icons.ts`, 8 icons) and the `filogic-custom-icons` font | planboard toolbar, integration logos | deliberate |

**Action → icon (Material, the dominant mapping):**

| Action | Icon | Deviations |
|---|---|---|
| add | `add` (44) | `fa-plus` 10 |
| edit | `edit` (17) | `fa-edit` 9 |
| delete | `delete` (42) | `fa-trash` 5 |
| save | `save` (ribbon) / `save_as` (save only) | `fa-save` 18, legacy |
| cancel (dialog) | `cancel` (ribbon) | |
| close | `close` (63) | |
| refresh | `refresh` | `fa-refresh` 10 |
| export to Excel | `download` | `fa-file-excel-o` 10 |
| print / download document | `class` (ribbon; odd) | `print` 6 |
| e-mail | `mail` | |
| archive | `archive` | |
| approve | `spellcheck` (odd) | |
| merge | `call_merge` | |
| flag | `flag` (35) | |
| attachments / remarks / identification tabs | `fa-paperclip` / `fa-file-text-o` / `fa-id-card-o` (about 45 each) | |

### 1.10 Density

- **ag-grid:** `ag-theme-alpine` on 60 of 61 grids, with **no `rowHeight` / `headerHeight` overrides**. That gives Alpine defaults: rows about 42px, header about 48px, grid-size 6px. The pinned totals row is forced to 30px (`global/filogic-grid-base/filogic-grid-base.scss:26-50`). Cell borders are removed.
- **Material:** `density: -3` (`defaultMaterialTheme.scss:47`). The outline text-field is capped at **36px** with padding `0 12px` and a 12px label (`mat-form-field-overrides.scss:90,113-114`). Buttons are **36px** (`mat-button-overrides.scss:11-14`).
- **Other control heights** are scattered: 34px 74, 30px 44, 40px 41, 35px 29, 32px 24.
- **Verdict:** the dense-but-not-cramped feel comes from **36px controls, 42px grid rows and 12–14px text**.

---

## 2. Page anatomy

### 2.1 Shell influence (out of scope, recorded for context)

- The Metronic header shows only the **current app name** (`shared/layout/header.component.html`). An app launcher grid (`app_menu/`, items in `services/AppMenuService.ts`) switches apps.
- **There are no page titles and no breadcrumbs on business screens** (`breadcrumb`: 0 hits). The app name plus the selected sidenav item act as the title.
- The header also hosts the **singleton transport-order and transport-file detail modals** (`shared/layout/header.component.ts:148-168, 335-352`). Any screen opens an order or file by emitting on `TransportOrderDetailsService.openModalEvent` / `TransportFileService.openModalEvent`.

### 2.2 Pattern P1: module shell (the dominant business page frame)

- **Structure:** a `mat-toolbar` action strip on top, then a `mat-sidenav-container` with a left sub-navigation (expansion panels, collapsible to an icon rail), then a `<router-outlet>`. Full width and full height, with no container and no padding.
- **5 shells**, covering about 46 routed list screens:

| Shell | File | Toolbar actions |
|---|---|---|
| Transport | `transport/transport.component.html` | New (menu), Accept, PrintEmailWizard, TransportOrderApproveWizard, AiWizard, OtherWizards menu |
| Master data | `master-data/master-data.component.html` | New, MigrateRelations, DiscountsOrSurcharges, AdjustingPriceLists |
| Finance | `finance/finance.component.html` | New, GenerateInvoice, ExportInvoiceWizard, PrintEmailWizard |
| Transport unit transactions | `transport-unit-transaction/transport-unit-transaction.component.html` | ClosePeriod, SendTransactionsToTrackOnline |
| CBS | `cbs/cbs.component.html` | Upload, Download, CBSSettings, TellMeHowThisWorks |

- **Toolbar content:** module-wide actions ("New" first, then wizards), not selection actions.
- **Sidenav content:** entity types (master data: about 36, built from the permission tree, `master-data.component.ts:140`) or **saved filter presets** (transport: `transportorder?filter=today|readyfor|…`, query params, not separate routes).
- **Variant:** the digital archive puts a `p-splitter` in place of the sidenav. **Deliberate.**

### 2.3 Pattern P2: grid list page (inside the shell)

- **Canonical template:** `master-data/master-data-base/master-data-base.component.html`. It is a `.main-control-container` holding `.preferences-container`, which holds `<ag-preferences-panel>`, followed by a `.grid-container` holding `<ag-grid-angular>` at 100% / 100%.
- **The preferences panel** (`global/ag-preferences-panel/`, **40 templates**) is the filter and command row directly above the grid. It holds:
  - free-text search
  - refresh
  - column chooser
  - export
  - optional filters: date range, organisation unit, category, discounts, flags
  - grid-profile "apply to all users / tenants"
  - sometimes create-new
- **Followers:**
  - all 35 master-data lists
  - transport order and file wrappers
  - finance invoices, purchase orders and incoming invoices
  - TUT lists, data warehouse
  - portal lists, kisjes, airhunters, CBS questionnaires
  - planboard grid tabs
- **Deviations:**
  - Admin lists use G1 (`m-subheader` + `m-portlet` + `p-table`, 20 / 28 / 22 files). **Legacy.**

### 2.4 Pattern P3: G1 admin page

- **Structure:** `div.m-subheader`, with an `h3` title, a sub-text and buttons on the right, over `div.m-content > div.m-portlet > m-portlet__body`, containing a search form and a `p-table`. Settings pages use `<tabset>` in portlets. Example: `admin/users/users.component.html`.
- **Counts:** 20 files (17 admin, plus dashboard, notifications and demo-ui).
- **Verdict:** this is the **only place with a real page title**. It is G1 legacy and should not be a source for rules.

### 2.5 Pattern P4: ribbon toolbar (planboard)

- **`toolbar/` (`app-toolbar`):** a strip of icon-over-label buttons with scroll arrows. Its actions:
  - New, PrintEmailWizard, SendToVehicle, TrafficLayers
  - OtherWizards
  - map overlays, open-tabs chooser
  - save layout
- **Used by 2 screens:** `planboard/planboard.component.html:2` and `arrowline_support/arrowline-support.component.html:2`.
- **Verdict:** **deliberate.** The planboard has no module shell, and the ribbon is its command surface. The ribbon look was kept on purpose.

### 2.6 Width and density

- Business screens are 100% wide. The grid fills the remaining height, and the page itself does not scroll: the grid does.
- Admin pages use the Metronic fluid container padding.

---

## 3. Data grids

### 3.1 Technology

| Tech | Templates | Where | Verdict |
|---|---|---|---|
| **ag-grid 32 enterprise** | 61 templates ≈ **98 screens** (`master-data-base.component.html` serves 34) | every TMS business list | **standard** |
| PrimeNG `p-table` | 22 | `admin/**` (17), `shared/layout`, `common-lookup-modal`, bigmile, pdf wizard | G1 legacy |
| `mat-table` | 7 | small embedded tables in dialogs (ledger entries, currencies, task list, price panel) | deliberate (lightweight) |
| plain `<table>` | 21 | key/value and schema layouts, not data grids | deliberate |

### 3.2 Shared building blocks

**`FilogicGridBase<T,S>`** (`global/filogic-grid-base/filogic-grid-base.ts`, 1,240 lines) provides:
- grid options and the sidebar (Columns tool panel only)
- column-state persistence
- quick search
- backend export
- pinned totals rows, flags
- drag/drop
- `getRowId = data.id`

**`ServerSideGridBase`** (same file, line 1159) adds:
- SSRM `getData`
- server sort
- server grouping
- select-all with exceptions

**The column factory `toAgGridColumns()`** (`common/columnExtensions.ts`) turns server `EntityAttributeDto[]` (caption, name, format, isDefault, isEditable, dataType, displayTotalsInFooter, alignRight, isBold) into `ColDef[]`. **Columns are metadata-driven from the server.**

Usage:

| Usage | Count |
|---|---|
| FilogicGridBase directly | 29 templates |
| ServerSideGridBase directly | 16 |
| PrintEmailSelectionGridBase | 3 |
| `MasterDataBaseComponent` | 35 screens |
| raw ag-grid | 13 |

The 13 raw grids are embedded or editable grids in dialogs and wizards (**deliberate**). The exception is `vehicle-registered-activities`, which is **legacy**: its own module registration, pagination, English headers and a default sort.

**Duplicate implementations:** `transport/wrappers/transport-order-wrapper` vs `planboard/transport-orders`, and the same pair for transport files. These are historical forks. **Legacy.**

### 3.3 Columns

- **Headers** come from server metadata (`caption`, localized), or from `this.l('…')` in hand-written colDefs (116). Hard-coded English appears in 5 files. **Legacy.**
- **Alignment: none.** `EntityAttributeDto.alignRight` and `isBold` exist but **`toAgGridColumns` ignores both** (verified: 0 hits in `columnExtensions.ts`). Numbers and amounts are **left-aligned everywhere**. The only `type: 'numericColumn'` is in `transport-unit-transaction/transaction-overview`. **Gap.**
- **Dates** are detected by **field name** (`date`, `departurefrom`, `arrivalat`; with time: `time`, `heartbeat`, `seen`). They are formatted with moment and the server `format`, default `'ll'` for dates ("9 okt. 2026") and `'LLLL'` for date-times. ETA, CTA, ATA and ATD cells use `AgFormattedDateTimeComponent` (`dd-MM-yyyy HH:mm`).
- **Numbers and currency: no formatter.** Server-side grids show raw DTO numbers. Client-side footer totals use `numberFormatter("nl-NL")` (hard-coded, with a TODO, `filogic-grid-base.ts:811`). Pinned totals use `numberWithDot()` (always `1.234,56`). **Gap.**
- **Booleans:** about 30 known field names map to `AgBoolIconCellRendererComponent`, which shows a centred Material `check` / `remove` icon. Unknown booleans show raw `true` / `false`. **Gap.**
- **Status columns** render as coloured badges in the tenant `App.ColorSettings` colours (§1.3). The same field→renderer mapping is re-implemented by hand in 26 `setCellRenderersAndAggregation` overrides. **Legacy duplication.**
- **Width:** `defaultColDef { flex: 1, minWidth: 60–65 }` (about 25 grids), then `autoSizeAllColumns()` when no saved state exists. All columns are resizable.
- **Pinning:** only utility columns are pinned (drag handle, portal action columns, `lockPinned`). Users can pin other columns, and that choice is persisted.
- **Bug** (not a convention): `planboard/transport-orders/transport-orders.component.ts:544` has a `findIndex` truthiness bug that applies `sum` to almost every column.

### 3.4 Sorting, filtering, paging, counts, persistence

- **Sorting:** single-column, server-side for SSRM. Some fields are blocked via `AgColumnRestrictions`. **There is no default sort anywhere** (the server order applies), except in `vehicle-registered-activities`.
- **Filtering:**
  - **No column-filter UI and no floating filters.** `columnExtensions.ts:169` sets `floatingFilter = false`, and server columns use `suppressHeaderFilterButton`.
  - Filtering is a **single free-text quick search** in the preferences panel. Client grids use `quickFilterText` (46). Server grids push the term into the filter model, with a 350ms debounce and a minimum of 3 characters.
  - On top of that come the panel's dropdown filters (date range defaulting to the last 6 months, persisted per user; organisation unit; category; flags) and the sidenav presets.
  - The set filter, advanced filter and filter tool panel are registered (`app.component.ts:143-164`) but unused.
- **Paging: no pager UI.** 24 SSRM grids use infinite block scrolling (`cacheBlockSize = 40`, 4 concurrent requests, 700ms block debounce). Client-side grids load everything. The only pager is `vehicle-registered-activities` (**legacy**).
- **Row count:** the custom `totalCounter` directive (33 templates, `global/filogic-grid-base/rowCounter/totalCounter.directive.ts`) is an absolutely positioned "Total: N" at the bottom right, in hard-coded English. The ag-grid status bar is unused.
- **Footer totals:** pinned-bottom rows sum the columns flagged `displayTotalsInFooter`. A second row shows the sum of the selection.
- **Column-state persistence:** **per user, server-side.**
  - On resize, sort, move, pin or visibility change, the state is saved to the user setting `Filogic.User.{Area}.{Entity}.AgGrid.State` (43 templates).
  - Group-expansion state is persisted too.
  - "Apply to all users" and "apply to all tenants" exist for admins.
  - 5 planboard and transport grids keep state **per organisation unit** (**deliberate**).
  - Wizards do not persist (**deliberate**).

### 3.5 Selection and row interaction

- `rowSelection`: single about 15, multiple about 14, dynamic 9.
- **The checkbox sits on the first displayed column** (`checkboxSelection: isFirstColumn`, plus a header checkbox for multi-select), not in a dedicated column.
- **Single click selects. Double click opens the detail / edit dialog** (13 `rowDoubleClicked` + 4 `cellDoubleClicked`; master-data `triggerEditing`, transport `openModalEvent`, invoices). Group and pinned rows are ignored.
- Picker dialogs use `rowMultiSelectWithClick` (4).

### 3.6 Row actions

- **The primary pattern is the ag-grid context menu** (`getContextMenuItems`, 18 templates + the 35 master-data screens).
  - Standard items: **Edit, Delete, Flag (Copy)**, each with a brand-tinted Material icon and a localized label.
  - With a multi-selection, only bulk-capable items remain (Delete).
- **Action columns** appear only in **portals** (self-service, distribution, carrier). They are pinned, have an empty header and are **deliberate** for occasional external users.
- About 37 grids still show the **default enterprise context menu** (Copy / Paste / Export, unlocalized). **Legacy / accidental.**
- **Delete is reachable only through the context menu** on list pages. The module toolbar holds "New" and wizards.

### 3.7 States

- **Empty:** ag-grid's built-in English "No Rows To Show". `overlayNoRowsTemplate` / `localeText` are never set. **Gap.**
- **Loading:** the ag-grid loading overlay (`setGridOption('loading')`), or an external `mat-spinner`.
- **Error:** a toast `notify.error(error.message)`. There is no in-grid error state. **Gap.**

### 3.8 Export, grouping, master-detail

- **Export** is **backend-generated Excel** through the preferences panel → a `DownloadPreferences` dialog (all fields or visible fields; all records or filtered records). Master data runs it as a background job with polling. `[canExport]="true"` in 23 templates.
- **Row grouping** is enabled on every metadata column by dragging in the Columns panel. Server-side grouping exists, and group footers show totals. **Deliberate, a power-user feature.**
- **Master-detail:** 3 grids (transport files with stops, freight details, price list). **Deliberate.**
- **Row drag and drop** appears in the planboard grids (8). **Bespoke.**

---

## 4. Forms

### 4.1 Field style and labels

| Pattern | Evidence | Verdict |
|---|---|---|
| **Outline `mat-form-field` with a floating `mat-label`** | 979 `appearance="outline"` in 152 files; 1,940 `mat-label` | **dominant (G2)** |
| Label above the field (Bootstrap `<label>` + `form-control`) | 311 `<label>` in 46 files, about 40 of them G1 | legacy |
| Label to the left | 1 file | legacy |
| `floatLabel="always"` | 3 | deliberate (hint visible) |

**The placeholder is the label.** Lookup wrappers render the field noun ("Relation", "PriceList") as the floating label. There is never "Select…" or "Search…" (§9).

### 4.2 Required marker

- **G2:** an **asterisk after the label**, added automatically by Material for `Validators.required` (284 in 71 files) or `[required]` (61). Never restyled.
- **G1:** a literal ` *` in the label text (21 in 10 files).
- **Both generations show an asterisk after the label.**

### 4.3 Layout and widths

- **G2 dialogs** use flex rows: `.entity` > `.entity__row`, with `mat-form-field { width: 100% }` and a 10px gap (35 files). Fixed variants: 163px "short" fields and a 335px name field. The SCSS is copy-pasted, not shared.
- **The transport-order details** screen uses a Bootstrap `row` / `col-sm-6` two-column layout with `col-sm-3` cells. **Legacy layout.**
- **G3** uses a CSS grid with named areas (invoice: `invoice / customer / financial / delivery`, in `finance/dialogs/edit-invoices-dialog/edit-invoices-dialog.component.scss`). **Deliberate target.**
- Inline `style="width…"` on fields: 57, scattered. **Legacy.**

### 4.4 Grouping

| Mechanism | Count | Verdict |
|---|---|---|
| `mat-tab-group` | 139 groups in 80 files | **standard (G2)** |
| `.entity__section` cards (G3 look) | 6 SCSS files | deliberate new direction |
| `m-portlet` | 28 files | G1 legacy |
| `mat-expansion-panel` | 6 files | sparse |
| fieldset / card | 2 / 2 files | noise |

**The standard master-data dialog tab skeleton** is very consistent (49 files):
- `mat-tab-group mat-stretch-tabs="false" mat-align-tabs="end"`, with **tabs right-aligned**.
- A text tab **General** comes first.
- Then **icon-only tabs with a tooltip**:
  - Attachments (`<documents asAttachmentTab>`, 44 files)
  - Remarks (`<remarks>`, 43)
  - Record identification (`<record-identifications>`, 43)
- Large entities (relation, transport order) add more text tabs and nested tab groups.
- **Tab-level error hint:** in `edit-relations-dialog` only, the General tab title turns red with the tooltip `RelationRequiredFieldsErrorMessage` when required fields are missing. It is a good pattern that has not been reused anywhere else.

### 4.5 Validation

- **When it fires:** the Material default. A field shows a red outline once touched or after submit. `updateOn` is never set. Only 15 files call `markAllAsTouched`.
- **Where the message appears: practically nowhere inline.** `<mat-error>` appears in 1 file. The convention is:
  - a red outline on the field,
  - plus **Save disabled while the form is invalid** (`base-dialog-template.component.ts`: `isSaving = status === "INVALID"`).
  - Caveat: `statusChanges` only emits on change, so a fresh, untouched new record starts with Save enabled.
- **Server and business errors go to an error toast** (§7).
- **G1** shows `<validation-messages>` text under the field (52 uses in 11 files).

### 4.6 Dirty state

- **None.** There is no `CanDeactivate`, no `beforeunload`, no confirm-on-close and no "Save enabled only when dirty". Code-wide, `.dirty` / `.pristine` appear only in 3 G1 templates.
- **Close behaviour:** most master-data dialogs close on backdrop click or Esc, silently discarding edits. `master-data/dialogs/modal-dialog.service.ts` opens 40 dialogs, and only 11 pass `disableClose: true`. App-wide: `disableClose: true` 31, otherwise the default. G1 bsModals use `backdrop: 'static'` (22 of 24).
- **Transport order:** the ribbon has a `hasChanges` toggle, but it is always true once sub-forms exist (`transport.order.details.component.ts:285-295`).
- **Verdict: gap.**

### 4.7 Save behaviour

- **Ribbon dialogs (41):** **Save = save and close.** The sequence is `notify.success` → close dialog → refresh the grid. There is no "save only".
- **The transport order** is the only screen with **Save & Close + Save only + Close** (`entity_operations_bar_enhanced/operation-sets/transport-order-operations.ts`). Close is shown only when there are no changes.
- **Invoice:** saves automatically before e-mail or print (`saveBeforeEmail`, `base-dialog-template.component.ts`; `edit-invoices-dialog.component.ts:469-500`).
- **Settings pages:** one explicit **Save all** at the top (`admin/tms-settings/tms-settings.component.html:13`).
- **No auto-save** of entity data anywhere.

---

## 5. Create/edit flow

### 5.1 Where edit screens open

**Edit screens are never routes.** There are no `:id` detail routes in business modules.

| Host | Entities | Count |
|---|---|---|
| **MatDialog + `base-dialog-template`** (header + ribbon) | all master data (35 route keys in `master-data/dialogs/modal-dialog.service.ts`), invoice, purchase order, incoming invoice, SSP order entry | 41 |
| **Near-full-screen bsModal**, hosted globally in the header | **transport order, transport file** (98vw × 96vh) | 2 |
| G1 bsModal | admin users, roles, tenants, editions, languages, organisation units, profile | 22 |
| **Full page (route)** | settings-type singletons only: tms-settings, tenant / host settings, e-mail configuration, custom properties, maintenance | about 6 |
| Inline quick-create from a lookup | `create_new/relation`, `create_new/inline_base` | 2 |
| Inline grid editing (`editable: true`) | detail lines: invoice lines, price-list tables, TF legs, a few small master data | 18 TS files |
| Docking layout | **never** for editing (planboard panels only) | 0 |

**Create uses the same dialog as edit** (`ModalDialogService.dialogs['new'+entity]`). It is opened from the shell toolbar "New", or from a lookup's "Add new…" with the typed text prefilled.

### 5.2 What decides the host

- **Size and reach decide**, not master data versus transactional:
  - small master data: small dialog (500–550px)
  - large master data: near-full-screen dialog (relation 96vw × 98vh, price list 98vw × 96vh, vehicle 1200px)
  - finance documents: 1400px
  - TO / TF: a global near-full-screen modal so they can be opened from anywhere (planboard, finance, grids, other dialogs)
- The bsModal hosting of TO / TF is legacy technology. The "open from anywhere over the current context" behaviour is **deliberate**.
- **Users never lose their list context when editing.** This is the strongest cross-app convention in the create/edit flow.

### 5.3 Dialog sizes

- **MatDialog widths:** about 40 distinct literals. The most frequent: 500px 33, 550px 18 (+9 minWidth), 400px 16, 900px 12, 98vw 12, 100% 9, 850px 7, 700px 7, 95vw 7, 1250px 6. Heights: `175px` 28 (confirm), 95vh 11.
- **Implicit tiers** (not codified anywhere):

| Tier | Size |
|---|---|
| confirm | about 400–500 × 175 |
| small form | 500–550 |
| medium | 700–900 |
| document | 1200–1400 |
| full | 95–98vw × 95–98vh |
| wizard | 70–98vw × 80–95vh |

### 5.4 Dialog anatomy

**Families:**

| Family | Count | Anatomy |
|---|---|---|
| **Entity editor**: `base-dialog-template` (`global/base-dialog-template/`) | 41 | **38px header bar in brand colour, white 15px / 600 title, close X right (45px hit area), 6px top radius**, then the `entity-operations-bar` **ribbon**, then the content (usually right-aligned tabs). **No footer.** |
| Entity editor with the ribbon used directly | 11 | `base-header` or a copied header, then `entity-operations-bar`: TF details, stop details, edit order freight, TUT details, arrowline, admin API details |
| Enhanced ribbon `entity-operations-bar-e` | 1 | transport order only: config-driven `RibbonOperation[]`, Approve split-button, state chips. **Deliberate exception**, an unfinished "v2" |
| **Action dialog**: `base-header` + body + `base-ok-cancel-footer` | 18 (`base-header` 50 total, footer 19) | 34px brand header with optional icon and close X; footer **Cancel (text button) left of OK (raised, primary, label configurable via `submitButtonName`)** |
| Bespoke `mat-dialog-*` / copied headers | about 38–50 | confirms, exports, print-CMR, push-to-vehicle, calendar. Mostly drift that predates the shared components (**legacy**) |
| G1 bsModal | 23 | `modal-header` / `modal-body` / `modal-footer` (**legacy**) |

**Header rule observed across G2 and G3:** a coloured title bar with the close X at the right. It is present in `base-dialog-template`, `base-header`, the confirm dialog and the copied headers.

**Edit dialog title:** the entity type and, for existing records, the name or number (e.g. `EditRelation`).

---

## 6. Buttons and actions

### 6.1 Placement

| Surface | Placement | Count |
|---|---|---|
| **Entity editor dialogs** | **ribbon at the top, below the header**; no footer | about 51 |
| Action / confirm dialogs | footer, right-aligned | about 55 |
| Module shell | `mat-toolbar` at the top of the module: "New" first, then module wizards | 5 |
| Planboard | `app-toolbar` ribbon | 2 |
| Wizards | custom footer `div.controls`: Previous / Submit / Next | 20 of 22 |

### 6.2 Ribbon content and order

The ribbon is `entity_operations_bar/entity.operations.bar.component.ts:79-170`:
- **Buttons have an icon over a label.**
- **Order:**
  1. the "Edit" group: Save, Cancel / Close
  2. Archive / Unarchive, Approve / Rectify
  3. entity-specific groups (Invoice: ChangeSendStatus / ChangeExportStatus; PriceList: Modify)
  4. an "other" group: PrintOrDownload, Download, Send e-mail, Merge
- **All ribbon buttons are styled the same (`btn btn-default`). There is no visual primary.** The only highlighted button is Approve on the transport order. **Deliberate** (the Office-style ribbon was kept on purpose).

### 6.3 Footer order

**There are two orders, roughly evenly split:**

| Order | Where | Count |
|---|---|---|
| **Cancel left, primary right** | `global/base-ok-cancel-footer` (19 dialogs); ABP modals (about 20); `transport-file-publish`, `generate-transport-files`, `push-to-vehicle`, `approve-transport-order`, `accept-transport-order`, `import-transport-orders`, `finance/dialogs/add-transport-order-dialog` (G3), 3 export-to-Excel dialogs, `manage-legs`, `create_new/relation` | **about 14 bespoke + 19 via the shared footer + about 20 G1 ≈ 53 dialogs** |
| **Primary left, Cancel right** (still right-aligned) | **`dialogs/confirm-dialog`** (the shared confirm, **46 call sites**; verified), column-visibility, convert-vehicle, split-freight-item, generate-return-*, select-transport-file-type, add-cmr-profile, select-orders, transport-order-request, reset-account, subscription modify ×2 | **13 dialogs, but the most-seen dialog in the app** |

### 6.4 Hierarchy and styling

**Material footers** have exactly one `color="primary"` button (raised or flat). Cancel is a text (`mat-button`) or flat button.

**Destructive actions are not styled as destructive.**
- Of 34 delete buttons, 3 have `warn` / `btn-danger`.
- Delete is an uncoloured icon button followed by the confirm dialog, whose confirm button is a **blue primary "Ok"**.
- `warn` appears in 10 files ad hoc.
- **Verdict: gap.**

**Icon use:**
- **Over 615 buttons:** text only 327, icon + text 144, icon only 144.
- **Ribbon:** icon over label.
- **Shell toolbars:** `mat-stroked-button` with icon + text.
- **Row-level and inline:** icon-only `mat-icon-button` (add, delete, close).
- **Icon-only buttons** carry a `matTooltip` in the tab headers. Elsewhere this is inconsistent.

### 6.5 Wording

| Key | Uses |
|---|---|
| `Cancel` | 52 |
| `Close` | 29 |
| `Save` | 22 |
| `Ok` | 22 |
| `Submit` | 27 |
| `Next` | 24 |
| `Previous` | 22 |
| `Refresh` | 14 |
| `Search` | 11 |
| `Delete` | 6 |
| `SaveAll` | 4 |
| `Confirm` | 4 |

- The confirm button says **"Ok"**, not the action verb.
- In the transport-order ribbon, **"Cancel" means "cancel the order"** (a business state change), and **"Close"** dismisses. Elsewhere "Cancel" dismisses. This is a naming clash.
- About 119 toast and button strings are English literals instead of localization keys. **Legacy.**

### 6.6 Busy while saving

- **G1:** `[buttonBusy]` (20).
- **G2:** `[disabled]` bound to a saving flag (48 buttons), or a spinner overlay over the dialog content (`showSpinner`).
- **The ribbon Save is not disabled while a request is in flight** (only while invalid), so double submits are possible. 3 screens guard with `PendingRequestService` + `message.info('SaveWhilePendingRequest…')`.
- **Verdict: gap.**

---

## 7. Feedback

### 7.1 Toasts

- **`this.notify.*` (ABP NotifyService → toastr): 749 calls in 221 files.** This is the standard.
  - `error` 514, `success` 193, `info` 46, `warn` 15.
  - Toastr default position: **top right**.
  - Alternatives are dead or legacy: PrimeNG MessageService (no `<p-toast>` anywhere), MatSnackBar (0), direct toastr (chat only).
- **Success wording:**
  - `l('SavedSuccessfully')` 38
  - entity-specific keys `Edit…Success` / `CreateNew…Success` (32, master-data dialogs)
  - `SuccessfullyDeleted` 7
  - `Copied` 3
- **Error wording:** mostly the raw server message (`error.message` 232, `err.message` 49), sometimes prefixed ("Unexpected error occurred: …", about 18).
- **Standard success flow** (consistent in master data): `save → close dialog → notify.success(l(key)) → refresh grid`. On error the dialog stays open, an error toast appears and the spinner hides.

### 7.2 Confirmations

- **`ConfirmDialogComponent` (`dialogs/confirm-dialog`): 46 call sites.** This is the standard. Its layout:
  - **brand title bar** "Confirm"
  - the question text from a key: `DeleteConfirmation` 8, `AreYouSure*` 21
  - buttons **Ok (primary, initial focus) then Cancel**
  - a close X
  - size about 400–500 × 175
- Variants: `ConfirmDialogWithInputComponent` 4, `DeleteEventDialogComponent` 1.
- G1 `this.message.confirm` (sweetalert): 9, admin only. **Legacy.**
- **Confirmations are asked for:** delete, archive, copy, reset, bulk actions. **Not asked for:** closing a dirty form (§4.6).

### 7.3 Busy indicators

| Mechanism | Count | Use |
|---|---|---|
| **`<mat-spinner>` overlay** over a dialog or section | 209 tags in 135 files | **standard (G2)**; diameters inconsistent (16–70) |
| ag-grid loading overlay | most grids | grid fetches |
| `abp.ui.setBusy` (block a region) | 41 / 27 files | mixed G1 / G2 |
| `[busyIf]`, `[buttonBusy]` | 27 / 20 | G1 legacy |
| ng-select `[loading]` | 16 | lookups |
| `mat-progress-bar` | 4 | rare |
| Wizard `task-list` | 16 wizards | **per-item progress** for batch operations (deliberate) |

### 7.4 Errors

- **Global:** `interceptors/filogic.inerceptor.ts` → `services/configuation.service.ts showError` → **an ABP / sweetalert modal**. It is suppressed for 2 endpoints. A 401 redirects to login.
- **Per call:** about 514 additional `notify.error` toasts. **The same failure very likely shows twice** (modal + toast). **Gap / bug.**
- **Inline errors:** almost none (§4.5).

---

## 8. Formatting

### 8.1 Locale and timezone

- **UI languages:** en, de, es, fr, it, nl, pt, ru (`aspnet-core/src/FiLogic.Core/Localization/FiLogic/FiLogic-*.xml`). The main user base works in **nl** and **en**.
- **Locale wiring:**
  - `LOCALE_ID` and moment both use `abp.localization.currentLanguage.name` (`app.module.ts:107`, `app.component.ts:129`).
  - **Bug:** `convertAbpLocaleToAngularLocale` (`src/root.module.ts:116-121`) never matches `AppConsts.localeMappings`. The intended mapping is **en → en-GB** ("European conventions", `src/shared/AppConsts.ts:19`), but "en" resolves to en-US.
- **Timezone:** user, tenant and host settings exist (ABP timing). Display mostly uses `moment().local()`, i.e. the **browser** timezone. The calendar uses the user setting. `owlDisableTimeZone` strips the timezone for date-only values (7 files).

### 8.2 Dates and times

| Context | Format | Evidence | Verdict |
|---|---|---|---|
| **Grid date columns** | moment `'ll'`, e.g. "9 okt. 2026" / "Oct 9, 2026" | `columnExtensions.ts` `applyFormat` | **dominant, locale-aware** |
| Grid date-time columns | `'LLLL'` (long, with weekday) or the server `format` (`LT` ×8, `lll` ×4 in C# definitions) | | |
| **Grid ETA / ATA / CTA / ATD cells** | `dd-MM-yyyy HH:mm` | `global/ag-formatted-date-time` | deliberate (compact, operational), but hard-coded NL style |
| Stops (`transport/file-stops/*` pipes) | `'lll'` ("9 okt. 2026 14:30") | | |
| **Date / date-time inputs** (owl pickers: 42 + 39 + 12, 22–23 files) | `DD-MM-YYYY HH:mm`; for "en": `DD/MM/YYYY hh:mm A` | `src/shared/owl-date-time`, `owl-date-picker.component.ts:94` | **standard picker; 24h, day-first** (en uses 12h: deviation) |
| Date-range filter | `date-range-picker` (15 files), `'L'` | `shared/common/timing/date-range-picker.component.ts` | standard |
| Technical logs (audit, API messages, webhooks) | `YYYY-MM-DD HH:mm:ss` | 8 + 3 | **deliberate** |
| Admin lists | `momentFormat:'L'` | 13 | G1 |
| Planboard headers | `D MMM`, `D dd`, `LTS` | Gantt / outline | bespoke |
| Bugs | `'MM/dd/yyyy'` in 2 dialogs; moment `'dd MMMM YYYY'` (renders a weekday) in 4 arrowline places; `'MM/DD/YYYY'` in the discounts wizard text; `'MMM D, YYYY'` in group totals | | legacy / bug |

- **Time** is **24-hour `HH:mm`** everywhere except the en owl picker.
- **Time-only** values (time slots, time windows) use `HH:mm` (31 in TS).

### 8.3 Numbers

- **There are 4 competing approaches:**
  - the Angular `number` pipe with `LOCALE_ID` (`'1.2-2'` ×14, mostly invoices)
  - hard-coded `nl-NL` (grid totals, map, LOS)
  - the browser locale (dashboard)
  - invariant `toFixed` (24)
- **The deliberate modern helpers:**
  - `src/shared/helpers/mathUtils.ts`: BigNumber rounding, `PRICE_MIN/MAX_DECIMALS = 2/5`
  - `price-format.ts formatPrice`: user locale, 2–5 decimals, grouping on
  - the `input[maxDecimals]` directive (`directives/max-decimals.directive.ts`, default 5, 60 uses)
- **Number inputs** are `type="number"` (208 occurrences, `step="any"`).
- **Decimals by convention:** **amounts 2, unit prices 2–5**, weights and volumes free.

### 8.4 Currency

- **No currency symbol in most of the UI.** Amounts show as numbers. The rationale is documented in `price-format.ts`: a price list or invoice is in one currency, shown once at document level.
  - `currency:' '` ×6 (financial) explicitly suppresses the symbol.
  - Hard-coded `€` appears only in LOS and the map (bespoke).
  - Currency day rates show ISO codes.
- `ng2-currency-mask`: 0 uses (dead). The `$` `currencyInput` directive appears once, in G1. **Legacy.**

### 8.5 Units, percentages, durations

- **Units are mostly absent.** The freight weight, volume and loading-meters inputs have no unit in the label, the suffix or the key (`transport/dialogs/edit-order-freight-dialog/*.html:257-273`). **Gap.**
- Units appear inline only in map, LOS and planboard-map views ("km", "kg CO₂").
- **Percentages:** `{{x}}%` concatenated (7 files). There is no percent pipe. "(%)" appears in the label in one case.
- **Durations:** `formatRouteDuration` gives "1d 2u 5m" with localized units (`planboard-map.service.ts:597`, deliberate). One place hard-codes English hours / minutes. **Legacy.**

---

## 9. Lookups

### 9.1 Components

| Component | Count | Used for | Verdict |
|---|---|---|---|
| **`mat-select`** | 268 in 90 files (47 multiple) | enums and small or static lists (VAT, waypoint types, languages, states) | **standard for small lists** |
| **ng-select wrappers on `SelectDropdownBaseComponent`** (`global/select-dropdown-base/`) | relation 47, price list 13, contact person 5, transport unit 3, product 3, capabilities 2, product-relation code 2 | **server-searched master-data entities** | **standard for entity lookups** |
| ng-select clones not on the base (vehicle-select 16, transport-file-select, SSP relation and address, consignment note, CMR profile, do-not-combine) | about 25 | entities | legacy (no debounce or minimum length) |
| `mat-autocomplete` | 19 in 11 files | addresses, postal codes, waypoints, inline relation creation | deliberate for address-like free entry |
| `organization-units-select` (tree) | 17 in 14 files | organisation units | deliberate |
| p-autoComplete, bootstrap-select, native select, `common-lookup-modal` | G1 only | | legacy |
| Grid-based picker dialog | 1 (`driver-selection-grid`) | drivers in a wizard | exception |

### 9.2 Entity-lookup behaviour (`SelectDropdownBaseComponent`)

- **Server search:** **debounce 200ms, minimum 3 characters.** Below 3 characters the list shows an "Enter3Chars" item; an empty term reloads the first page. No results show a "NoItemsFound" item.
- **Paging:** 25 items per page with infinite scroll; virtual scroll above 3 items.
- **Create-new from the lookup:** an "Add new X" item (`addTag`, `l('AddNew'+entity)`) opens the entity's create dialog **prefilled with the typed text**. The new record is selected on save.
- **Edit from the lookup:** a pencil next to the selected value opens the entity's edit dialog.
- **Clearable** (ng-select default; overridden in 3 places). The dropdown arrow is hidden.
- **Display:**
  - The option list shows **rich rows**. Relation: bold "searchName - name", then street, then "CC-postcode city".
  - **The selected value shows the name only.** There is no code + name convention.
  - Vehicles can show "name + licence plate" (setting-driven).
- **Debounce elsewhere** is inconsistent: 200 ×7, 400 ×4, 500 ×2, 300 ×2, 250, 600.

### 9.3 Choosing between select and lookup

- There is no explicit item-count threshold. **De facto: anything that is master data with server paging gets the searched lookup; enums and small reference lists get `mat-select`.**
- Deviations: countries (about 250 items) use an unsearchable `mat-select` in 2 dialogs; products, vehicles and price lists use `mat-select` in some settings screens. **Legacy.**

---

## 10. Keyboard

- **No application shortcuts.** No hotkey service, no Ctrl+S, no F-keys. `ctrlKey` is used only for multi-select (arrowline, planboard legs).
- **Enter:**
  - **Small confirm and action dialogs** confirm on Enter through a document-level `@HostListener('document:keydown')` (11 dialogs, including the shared confirm and `reset-account-dialog`).
  - **Entity editor dialogs do not save on Enter.** There is no form submit in Material dialogs; `ngSubmit` appears only in G1.
  - 4 forms deliberately `preventDefault` Enter in fields.
  - **Enter adds a new freight line** in the transport order (`transport.order.details.component.ts:1244`, deliberate fast entry).
  - Search fields refresh on Enter (3).
- **Esc:**
  - **Small dialogs:** MatDialog default (Esc closes).
  - `disableClose: true` (31) disables it. 4 big dialogs (TO details, approve TO, TF details, edit order freight) re-implement Esc with guards.
  - G1 bsModals use `backdrop: 'static'`.
  - **Verdict:** inconsistent.
- **Tab:** address and waypoint autocompletes commit the highlighted option on Tab (`global/addresses`, `ssp-address`, waypoints). **Deliberate** for fast data entry.
- **Focus:**
  - `cdkFocusInitial` is placed on the **primary button** in 10 action dialogs (the confirm: Ok).
  - There is no first-field autofocus anywhere (`autofocus` 0). MatDialog focuses the first tabbable element, often the close X or a ribbon button. **Gap.**
- **Grid:** ag-grid default keyboard navigation. Editable grids use `singleClickEdit` (15) and `stopEditingWhenCellsLoseFocus` (6). Custom cell editors handle their own keys.

---

## 11. Permissions

- **Hiding is dominant, overwhelmingly so.**
  - In templates, `*ngIf` with `isGranted` appears 32 times and `[hidden]="!isGranted"` 8 times (G1 table columns).
  - `[disabled]` with a permission appears **0 times** in templates, and once via TS: `toolbar/toolbar.component.html:199-201`. In that case the Arrowline action is disabled without permission, which looks like deliberate discoverability for an integration.
- **Granularity:**
  - **Route level:** `auth-route-guard.ts:39`, `route.data['permission']` on almost every route.
  - **Menu, sidenav and tab level:** the permission-filtered trees in `master-data.component.ts:147`, `transport.component.ts:156` and `finance.component.ts:90`; the planboard tabs, which are removed from the saved layout when not granted (`planboard.component.ts:146-149`); the SSP tabs.
  - **Action level only in G1 admin.** Business ribbons, context menus and toolbar "New" buttons have **no client-side permission checks** (`RibbonOperation` has no permission field). **Gap**: the new backend has fine-grained policies.
- **Feature flags** are hide-only as well (3 template uses).
- Disabled states in G2 are business state (saving, invalid, order state), never permission.
- Legacy bug: `admin/tenants/tenants.component.html:13` uses AngularJS `ng-if`, so its permission check does nothing.

---

## 12. Bespoke screens (not covered by the general rules)

| Screen | Path | What it is | Libraries |
|---|---|---|---|
| **Planboard workspace** | `planboard/planboard.component.*` | Dockable, persisted tab workspace (transport files, orders, stops, legs, legs to schedule, map, vehicle planboard, Gantt, resources, Route42) with a ribbon on top and live updates | smart-webcomponents dockinglayout, ag-grid, SignalR |
| **Vehicle planboard Gantt** | `planboard/vehicle-planboard-gantt/` | Vehicle × time Gantt with move, resize and selection (about 2,200 lines) | gantt-schedule-timeline-calendar 3.35 |
| **Vehicle outline** | `planboard/vehicle-outline/` | Vehicle-row board with drag and drop; own `--vop-*` tokens | CDK drag-drop |
| **Planboard map** + route forecast | `map/`, `services/planboard-map.service.ts` | Map with routes, ETA forecast, tolls | MapLibre GL (from CDN, `src/index.html:35`), Google Maps for geocoding |
| Track & Trace | `track-and-trace/` | Full-screen map with a settings panel | Google Maps |
| LOS (road-pricing / CO₂ simulation) | `los/` | Analytics page with its own header and hard-coded Dutch text | MapLibre |
| Tenant dashboard | `main/dashboard/` | KPI widgets and charts | chart.js |
| Sensor events chart | `features/event-charts/` | Temperature time series inside vehicle / TF dialogs | chart.js |
| Resource calendar | `global/calendar/` | Availability scheduler inside vehicle / relation dialogs | smart scheduler |
| PDF viewer, PDF import | `transport/print-or-download/`, `wizards/pdf-transport-order-import-wizard/` | Document preview; PDF beside extracted order | ngx-extended-pdf-viewer |
| Signature capture | `distributionPortal/.../distribution-signature/`, `global/ag-signature/` | Proof-of-delivery signature | canvas |
| Portals | `self-service-portal/`, `carrier-portal/`, `distributionPortal/`, `kisjes/`, `arrowline_support/`, `airhunters/` | External-user screens; top-level tabs, splitters, action columns | ag-grid, PrimeNG splitter |
| Integration landing pages | `chainfill/`, `bigmile/`, `route42/`, `trackonline/` | Logo, cards, API-key form | PrimeNG card |
| Onboarding | `admin/onboarding_wizard/` (route `/onboard`) | Full-page pre-shell wizard | mat-stepper |

### 12.1 Wizards (a general pattern, listed separately because they are frequent)

22 `mat-horizontal-stepper [linear]` wizards. Canonical example: `transport/archive-transport-files-wizard/archive-transport-files-wizard.component.html`.

**Shell:**
- Opened as a large dialog (`98vw × 95vh` or `80vw`, `disableClose: true`).
- `base-header` with a title and close X.
- A stepper with step labels.

**Steps:**
1. **Welcome**: explanatory text (about 19 of 22).
2. **Selection**: usually an embedded grid with `[completed]` bound.
3. **Confirm / summary**: after Submit, a **`task-list` with per-item progress** (16 of 22).

**Footer:** a fixed `div.controls` outside the stepper (20 of 22), right-aligned, containing **Previous, Submit (last step only), Next (disabled until the step is valid)**.

**No shared wizard base.** The newest wizards (`wizards/pdf-*`, `wizards/chainfill-*`) drive the footer from a `buttonsMap` config and add a **Cancel** button. **Deliberate evolution.**

**Deviations:**
- in-step `matStepperNext` buttons (`generate-transport-order-wizard`, `importing-transport-orders-wizard`): legacy
- no Welcome step (`discounts-surcharges-wizard`): deliberate, recently modernised

---

## 13. Precedents already set in the new app (`opentms-next/frontend`)

These are not evidence of what users are used to, but they exist and new screens will copy them. Sources: `src/index.css`, `src/components/ui/*`, `src/apps/admin/**`, `docs/designs/{6,21,27}-*.md`.

### 13.1 Stack and tokens

- **Stack:** shadcn `base-nova` on `@base-ui/react` (triggers compose with `render=`, not `asChild`), lucide icons, Tailwind v4, AG Grid 33 enterprise.
- **Tokens:**
  - stock shadcn neutral (oklch) plus one custom token, `--brand: #0F6CBD`
  - **design #21: `--brand` must not be assigned to `--primary`**, so primary stays near-black
  - `--radius: 0.625rem`
  - font Geist Variable
  - dark mode defined but never switchable
- **Hard-coded values:**
  - `bg-[#F8F9FA]` page / nav / command-bar surface
  - `border-b-[#E5E5E5]`
  - raw Tailwind status pill colours (`bg-green-100 text-green-800` …)

### 13.2 Page anatomy

- **`AppShell`:** a brand header bar (h-12) with the app launcher and app name, plus `AppNavPane` (250px, or a 56px rail). **No page title.**
- **List page:** `AppCommandBar`, then a grid filling the height (`px-6 pb-6`).
- **`AppCommandBar`:** commands are localization keys with an optional `isPrimary`, `variant: destructive`, `requiresSelection` and `disabledReasonKey`. Two styles exist behind the ABP feature `UI.CommonToolbar`:
  - **off:** a boxed bar with buttons; selection commands right, plus the "N selected" count
  - **on:** an **Office-365 style text strip**: primary first in semibold, then selection commands, then a divider, then secondary actions with overflow into "More", and the count on the far right

### 13.3 Grid (`OpenTmsGrid`, design #6)

- **Loading and paging:** lazy-loaded; SSRM with 100-row blocks and infinite scroll; no pager.
- **Toolbar** (`OpenTmsGridToolbar`): search (300ms debounce, `wildcardSearch`), a Columns dropdown and Reset.
- **Filters and selection:** AG text column filters; a pinned-left 40px checkbox column with select-all = "filter based".
- **Persistence:** per-user server-side column state (`/api/platform/grid-settings/{gridId}`).
- **Footer:** "Total N" / "N of M" (`Intl.NumberFormat`).
- **States:** localized empty and empty-filtered overlays, block retry, and a toast after repeated failures.
- **Theme:** the `ag-theme-quartz` legacy CSS theme, not tied to the tokens.

### 13.4 Row actions, dialogs and forms

- **Row actions:** a pinned-right `__actions__` column with ghost icon buttons (pencil, key) and an **inline popover confirm for delete** (`[Cancel outline] [Delete destructive]`).
- **Dialogs:**
  - Raw `@base-ui/react/dialog` is copied into 6 files, with no shared Dialog.
  - Centred, `max-w-sm` or `max-w-md`, `p-6`, title `text-base font-semibold`, **no coloured header bar**.
  - **Footer `flex justify-end gap-2`: Cancel (outline) left, primary or destructive right.** During a pending mutation the label swaps to "Saving…" and both buttons are disabled.
- **Forms:**
  - **The label sits above the field** (`Label` + `Input`, h-8 = 32px).
  - A literal ` *` marks required fields, with the HTML `required` attribute and no field-level errors.
  - **Server errors render inline** as `text-sm text-destructive` above the footer, never as a toast.
- **Toast:** a bare custom `Toast` (bottom right), with no variants and no queue. Used twice.
- **Permissions:** commands and actions are **hidden** when not granted. A missing view permission shows a "PermissionDenied" panel.
- **Dates:** `Intl.DateTimeFormat(undefined, {dateStyle: "short"})`. This ignores the app culture (gap).

---

## 14. Summary: candidate rules per topic

These are the dominant patterns that phase 2 will turn into rules, subject to the decisions in [open-decisions.md](open-decisions.md).

| Topic | Dominant pattern (candidate rule) | Followers | Main deviations |
|---|---|---|---|
| Brand | one tenant-themed brand colour for headers, selection, tabs, icons, captions | 485 uses | hard-coded orange (legacy) |
| Status colours | tenant-configured background / font pairs per state | all status renderers | load factor (legacy) |
| Page | module command strip on top + left sub-nav + full-bleed grid; no page title | 5 shells / about 46 lists | G1 admin |
| Grid | ag-grid, SSRM infinite scroll, no pager, one free-text search above the grid, per-user persisted columns, double click = open, context menu = row actions, total count bottom right | about 98 screens | p-table admin, portals (action columns) |
| Forms | outline fields with floating labels, asterisk on required, Save disabled while invalid, right-aligned tabs starting with General + icon tabs for Attachments / Remarks / Identification | 152 / 49 files | G1, TO layout |
| Create/edit | always a dialog over the list; size by entity size; same dialog for create and edit | about 66 | settings singletons (page) |
| Actions | entity editors: ribbon on top (Save, Cancel / Close, entity actions, documents); action dialogs: footer | 51 / 55 | footer order split |
| Feedback | success toast + close + refresh; error toast and the dialog stays open; confirm dialog for destructive actions; spinner overlay while busy | 749 / 46 / 209 | double error display |
| Formatting | locale-aware dates (`ll`) in grids, day-first 24h `DD-MM-YYYY HH:mm` in inputs and operational cells, 2 decimals for amounts, no currency symbol per line | see §8 | hard-coded nl-NL, US formats |
| Lookups | `mat-select` for small lists; server-searched lookup (200ms, 3 chars, 25 per page, Add new…, edit pencil, clearable, name shown) for entities | 268 / about 75 | clones without debounce |
| Keyboard | Enter confirms small dialogs; Esc closes small dialogs; Tab commits autocomplete; no shortcuts | 11 / default / 3 | big dialogs |
| Permissions | hide, never disable | 40 of 41 | Arrowline teaser |
