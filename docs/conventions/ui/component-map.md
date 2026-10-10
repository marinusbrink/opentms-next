# Component map: Angular → shadcn/ui

Each Angular component or library widget in use, with its equivalent in the new app.

**Status values:**

| Status | Meaning |
|---|---|
| **exists** | already in `opentms-next/frontend/src/components/ui` |
| **shadcn** | add with the shadcn CLI (`npx shadcn add <name>`; style `base-nova` on `@base-ui/react`) |
| **build** | an OpenTMS library component composed from shadcn parts, specified in the rules; build it into `components/ui` the first time a feature needs it, never inline in a page |
| **GAP** | shadcn has no equivalent and **a choice is needed**: listed in §4, not decided here |

Counts are template occurrences in the Angular app ([ui-inventory.md](ui-inventory.md)).

## 1. Layout, page and navigation

| Angular | Count | New | Status | Notes |
|---|---|---|---|---|
| Metronic header + app launcher + side menu | shell | `AppShell`, `WaffleLauncher`, `AppNavPane` | exists | out of scope (shell) |
| Module shell `mat-toolbar` (New, wizards) | 5 shells | `AppCommandBar` | exists | Office-365 strip (D-08) |
| Module shell `mat-sidenav` (entity types, filter presets) | 5 | `AppNavPane` views | exists | presets become nav entries or a filter control (UI-PAGE-004) |
| `app-toolbar` ribbon (planboard) | 2 | `AppCommandBar` | exists | bespoke screen; see `ui-rules/bespoke.md` |
| `m-subheader`, `m-portlet` (G1) | 20 / 28 | none | n/a | legacy; not ported |
| `p-splitter` | 4 | `Resizable` | shadcn | carrier portal, digital archive |
| `mat-card`, `p-card` | few | `Card` | shadcn | landing / portal pages only |
| `mat-expansion-panel` | 28 | `Collapsible` / `Accordion` | shadcn | |
| `smart-docking-layout` | 1 (planboard) | none | **GAP-07** | bespoke |

## 2. Data display

| Angular | Count | New | Status | Notes |
|---|---|---|---|---|
| `ag-grid-angular` + `FilogicGridBase` / `ServerSideGridBase` / `MasterDataBaseComponent` | 61 templates / about 98 screens | `OpenTmsGrid` (+ `OpenTmsGridToolbar`, `OpenTmsGridFooter`) | exists, **needs changes** | needed: AG Theming API from tokens (tokens.md §11); context menu + double-click (D-09); toolbar filter slot (D-10); pinned totals row; backend export button; localized overlays (already done) |
| `ag-preferences-panel` (search, refresh, columns, export, filters) | 40 | `OpenTmsGridToolbar` | exists, extend | add: refresh button, export button, `filters` slot |
| `totalCounter` directive ("Total: N") | 33 | `OpenTmsGridFooter` | exists | |
| Pinned-bottom totals rows | client grids | `OpenTmsGrid` `totals` prop → AG `pinnedBottomRowData` | build | UI-GRID-011 |
| Cell renderers: bool icon, status badge, flags, date, ETA date-time | about 30 field maps | `BoolCell`, `StatusCell`, `FlagCell`, `DateCell`, `DateTimeCell`, `NumberCell`, `AmountCell` in `components/ui/grid-cells.tsx` | build | one renderer per data type (UI-GRID-004) |
| `mat-table`, plain `<table>` in dialogs | 7 / 21 | `Table` | shadcn | small read-only tables only (UI-GRID-001) |
| `p-table` (G1 admin) | 22 | `OpenTmsGrid` | exists | legacy |
| Status renderers `ag-orderState`, `ag-taskStatus`, `ag-status` | about 28 files | `StatusBadge` | build | reads the tenant colour pair, falls back to tokens §4 |
| Flags `ag-flags`, `components/flags` | 6 | `FlagIndicator` | build | `--flag-*` tokens |
| chips / pills | few | `Badge` | shadcn | |
| `matTooltip` | many | `Tooltip` | exists | |
| `mat-progress-bar` | 4 | `Progress` | shadcn | |
| `mat-spinner` | 209 | `Spinner` / `Skeleton` | shadcn / exists | UI-FB-006 |
| `task-list` (wizard per-item progress) | 16 | `TaskList` | build | UI-WIZ-004 |
| chart.js charts | dashboard, sensors | none | **GAP-06** | bespoke |

## 3. Dialogs, forms and actions

| Angular | Count | New | Status | Notes |
|---|---|---|---|---|
| `base-dialog-template` (brand header + ribbon) | 41 | `EntityDialog` (`variant="editor"`) on shadcn `Dialog` | build | brand header bar (D-05), `AppCommandBar` below the header (D-13), size tiers (D-16), dirty guard (D-19) |
| `base-header` + `base-ok-cancel-footer` | 50 / 19 | `EntityDialog` (`variant="action"`) | build | plain header, footer `[Cancel][Primary]` (D-14) |
| bsModal transport order / file hosts (global) | 2 | `EntityDialog size="full"` opened through `useOpenRecord()` + the `?open=` deep link | build | UI-DLG-007 |
| `ConfirmDialogComponent` | 46 | `ConfirmDialog` on shadcn `AlertDialog` | shadcn + build | verb label, destructive styling (D-21) |
| `ConfirmDialogWithInputComponent` | 4 | `ConfirmDialog` with a `children` slot | build | |
| `entity-operations-bar` / `-bar-e` ribbon | 51 | `AppCommandBar` inside `EntityDialog` | exists | |
| `mat-form-field appearance="outline"` + `mat-label` | 979 | `FormField` (shadcn `Field` + `Label` + control + error) | shadcn + build | label above (D-17) |
| `matInput` | many | `Input` | exists | |
| `textarea matInput` | many | `Textarea` | shadcn | |
| `mat-select` (static lists) | 268 | `Select` | shadcn | UI-LOOK-001 |
| `mat-select multiple` | 47 | `MultiSelect` | build | generalise `RoleMultiSelect` (move it out of `ui/` as domain-free) |
| ng-select wrappers (`SelectDropdownBaseComponent`, relation / product / price-list / …) | about 75 | `EntityLookup` | build on shadcn `Combobox` | **GAP-01** for infinite server paging inside Combobox |
| `mat-autocomplete` (addresses, postal codes) | 19 | `EntityLookup` (`allowFreeText`) | build | |
| `organization-units-select` (tree) | 17 | `TreeSelect` | **GAP-02** | |
| `mat-checkbox` | many | `Checkbox` | shadcn | replace native checkboxes in the admin screens |
| `mat-slide-toggle` | some | `Switch` | shadcn | |
| `mat-radio-group` | some | `RadioGroup` | shadcn | |
| `owl-date-picker` / `owl-datetime-picker` / `owl-time-picker` | 42 + 39 + 12 | `DatePicker`, `DateTimePicker`, `TimeInput` | **GAP-03** | |
| `date-range-picker` (jQuery) | 15 | `DateRangePicker` | **GAP-03** | |
| `mat-tab-group` | 139 | `Tabs` | shadcn | right-aligned, General first, icon tabs (UI-FORM-008) |
| `mat-horizontal-stepper` wizards | 22 | `Wizard` | **GAP-04** | |
| `mat-menu` | 63 items | `DropdownMenu` | exists | |
| ag-grid context menu | 18 + 35 | AG `getContextMenuItems` (enterprise) | exists in AG | wired through `OpenTmsGrid` `rowActions` (UI-GRID-008) |
| `mat-icon-button` | 23 | `Button size="icon-sm" variant="ghost"` + `Tooltip` | exists | |
| `mat-raised-button` / `mat-flat-button color="primary"` | 88 | `Button` (default variant) | exists | |
| `mat-button`, `mat-stroked-button` | 113 | `Button variant="outline"` / `"ghost"` | exists | |
| `color="warn"` / `btn-danger` | 10 | `Button variant="destructive"` | exists | |
| `notify.*` (toastr) | 749 | `toast` from shadcn `Sonner` (`<Toaster position="bottom-right">`) | shadcn | replaces the custom `Toast` (UI-FB-001) |
| `abp.message.*` sweetalert, interceptor error modal | 32 | none (inline alert or toast) | n/a | D-22 |
| inline error / alert block, red warning spans (`CannotBeUndone`) | none | `Alert` (`destructive`, plus an added `warning` variant on `--tone-warning-*`) | shadcn + build | UI-FB-004, UI-FB-008 |
| invoice lines virtual table, editable ag-grids in dialogs (`singleClickEdit`) | 13 grids + invoice | `LinesGrid` (client-side AG Grid on form state) | build | UI-GRID-014, UI-GRID-015 |
| `documents` attachments tab, upload dialog, `ng2-file-upload`, dropzone | 44 | `AttachmentsTab`, `FileDropzone` | **GAP-05** | |
| `remarks`, `record-identifications` tabs | 43 / 43 | `RemarksTab`, `RecordIdentificationTab` | build | standard tabs (UI-FORM-008) |
| rich text (`@kolkov/angular-editor`, summernote) | e-mail templates | `RichTextEditor` | **GAP-08** | |
| signature pad / canvas | 2 | none | **GAP-09** | bespoke |
| `ngx-extended-pdf-viewer` | 2 | none | **GAP-10** | |
| colour picker (highlight colour) | 1 | `FlagColorPicker` (8 fixed `--flag-*` swatches) | build | |

## 4. Gaps: a choice is needed

**I have not picked anything for these.** Each one blocks the first feature that needs it, so decide before that feature's design.

| ID | Need | Old app | Options to evaluate |
|---|---|---|---|
| **GAP-01** | **Entity lookup**: server search (debounce, minimum characters), infinite scroll over pages of 25, "Add new…" row, edit pencil, rich option rows, clearable | ng-select + `SelectDropdownBaseComponent` | (a) shadcn `Combobox` (Base UI) with a TanStack `useInfiniteQuery` list and an IntersectionObserver sentinel; (b) shadcn `Command` in a `Popover` (cmdk), same data layer; (c) a third-party async select (react-select async) |
| **GAP-02** | **Tree select** for organisation units (multi-select, expand / collapse) | `organization-units-select` | (a) build on `Popover` + a recursive `Checkbox` list; (b) a headless tree library (react-arborist, @headless-tree) |
| **GAP-03** | **Date, date-time, time and date-range input**: typed entry in `DD-MM-YYYY HH:mm` + calendar popup, 24h, week starts Monday, culture-aware | owl date-time picker, bootstrap-daterangepicker | (a) shadcn `Calendar` (react-day-picker) + `Popover` + a masked `Input` for typing, plus a custom time field; (b) a full picker library (e.g. react-aria / @internationalized/date DateField); (c) native `<input type="date">` (rejected in the old app: legacy) |
| **GAP-04** | **Wizard** stepper: step header, linear validation, fixed footer, per-step completion | mat-stepper + custom footer | (a) build `Wizard` from `Tabs`-like step list + footer (small, recommended to evaluate first); (b) a stepper library |
| **GAP-05** | **File upload / attachments**: drag and drop, progress, multiple files | ng2-file-upload, dropzone | (a) react-dropzone + own list UI; (b) build on a native drop zone |
| **GAP-06** | **Charts** (dashboard KPIs, sensor time series) | chart.js 4 | (a) shadcn `Chart` (Recharts); (b) AG Charts (already licensed with AG Grid Enterprise) |
| **GAP-07** | **Planboard docking workspace** (dockable, persisted tab panels) | smart-webcomponents docking layout | (a) dockview; (b) flexlayout-react; (c) a fixed `Resizable` layout with tabs (simplification) |
| **GAP-08** | **Rich text editor** (e-mail templates) | @kolkov/angular-editor, summernote | (a) Tiptap; (b) Lexical |
| **GAP-09** | **Signature capture** | raw canvas | (a) signature_pad; (b) own canvas component |
| **GAP-10** | **PDF viewer** | ngx-extended-pdf-viewer | (a) react-pdf (pdf.js); (b) the browser's native viewer in an iframe |
| **GAP-11** | **Gantt / resource scheduler** (vehicle planboard, resource calendar) | gantt-schedule-timeline-calendar, smart scheduler | (a) bryntum / dhtmlx (commercial); (b) a custom build on a virtualised grid; (c) FullCalendar resource timeline |
| **GAP-12** | **Maps** | MapLibre (from CDN) + Google Maps | keep MapLibre GL via npm (`react-map-gl`); this is a decision for the planboard design |

## 5. Changes needed to existing new-app components

These are not decisions; they follow from the rules.

| Component | Change | Rule |
|---|---|---|
| `OpenTmsGrid` | AG Theming API from tokens instead of `ag-theme-quartz.css` | UI-TOK-004 |
| `OpenTmsGrid` | `rowActions` prop → context menu; `onOpen` → double click and Enter | UI-GRID-007, UI-GRID-008 |
| `OpenTmsGridToolbar` | refresh button, export button, `filters` slot | UI-GRID-005, UI-GRID-013 |
| admin `UsersView` / `RolesView` | the inline delete popover becomes `ConfirmDialog`; the action column becomes a context menu | D-09, D-21 |
| custom `Toast` | replaced by Sonner `toast` | UI-FB-001 |
| raw `@base-ui/react/dialog` copies (6) | replaced by `EntityDialog` / `ConfirmDialog` | UI-DLG-001 |
| `extractApiError` (7 copies), `useIsGranted` (2 copies) | move to `lib/api/errors.ts`, `lib/auth/permissions.ts` | UI-FB-004, UI-PERM-001 |
| `Intl.DateTimeFormat(undefined, …)` in the admin views | `formatDate` from `lib/format` | UI-FMT-001 |
| hard-coded `bg-[#F8F9FA]`, `border-b-[#E5E5E5]`, status pill palette | `bg-surface-page`, `border-border`, `StatusBadge` | UI-TOK-001 |
