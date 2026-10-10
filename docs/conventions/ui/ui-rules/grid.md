# Data grids (UI-GRID)

**Load for:** list screens, grids inside dialogs, columns, cell renderers, filters.

All grids use `OpenTmsGrid` (lazy-loaded, server-side row model). Value formatting rules are in [format.md](format.md).

---

### UI-GRID-001 · MUST · Every data list is an `OpenTmsGrid`
Any list of records that can grow, sort or filter is an `OpenTmsGrid` with a stable `gridId` (`<domain>.<area>.<entity>`, e.g. `orders.transport.transportOrders`). shadcn `Table` is allowed only for small, fixed, read-only tables inside a dialog (at most about 20 rows, no sort or filter).
- **Check:** no `<table>` or `Table` rendering API list data; every `OpenTmsGrid` has a unique `gridId`.
- **Evidence:** inventory §3.1 (ag-grid on about 98 screens; `mat-table` / `<table>` only for small embedded tables).

### UI-GRID-002 · MUST · Server-side, infinite scroll, no pager
Lists use the server-side row model with block loading. There are no page-number controls. The footer shows the total, and "N of M" when filtered (`OpenTmsGridFooter`).
- **Check:** no `pagination` prop; no client-side `rowData` for API lists.
- **Evidence:** inventory §3.4 (24 SSRM grids with infinite blocks; the only pager is legacy); §13 (design #6).

### UI-GRID-003 · MUST · Column headers are localized, short nouns
`headerName: t("<Area>:<Field>")`. A unit, when there is one, goes in the header in brackets: `t("Weight")` + ` (kg)`; see UI-FMT-008. There are no code-like or English literal headers.
- **Check:** every `ColDef` has `headerName: t(...)`, or no header for the selection and actions columns.
- **Evidence:** inventory §3.3 (116 `l()` headers, 5 files with literals = legacy).

### UI-GRID-004 · MUST · One cell renderer per data type, by type, never by field name

| Data type | ColDef | Alignment |
|---|---|---|
| text | none (default) | left |
| number | `cellRenderer: NumberCell` + `type: "rightAligned"` | right |
| amount | `AmountCell` + `type: "rightAligned"` (2 decimals, no symbol) | right |
| date | `DateCell` | left |
| date-time | `DateTimeCell` | left |
| boolean | `BoolCell` (✓ icon or empty) | centre |
| state | `StatusCell` (wraps `StatusBadge`) | left |
| flags | `FlagCell` | centre |

- **Check:** numeric DTO fields have `NumberCell` / `AmountCell` and `rightAligned`; no inline `valueFormatter` doing its own formatting; no `true` / `false` text in cells.
- **Evidence:** inventory §3.3 (bool icon renderer, status badges, ETA date-time renderer; numbers left and unformatted = gap); D-11.

```tsx
// ✅
{ field: "revenue", headerName: t("Revenue"), cellRenderer: AmountCell, type: "rightAligned" },
{ field: "loadingDate", headerName: t("LoadingDate"), cellRenderer: DateCell },
{ field: "isArchived", headerName: t("Archived"), cellRenderer: BoolCell },
// ❌
{ field: "revenue", headerName: "Revenue", valueFormatter: p => p.value.toFixed(2) },
```

### UI-GRID-005 · MUST · Filtering: search plus explicit filter controls in the grid toolbar
**Toolbar layout:**
- **left:** search (300ms debounce, server-side `wildcardSearch`)
- **then:** the `filters` slot, holding the additional filters (date range, organisation unit, category, status, flags)
- **right:** Refresh (`RefreshCw`), Export (when designed), Columns, Reset

**Date range on transactional lists** (orders, files, invoices, transactions): the filter defaults to **the last 6 months** and the user's last choice is persisted per `gridId`.

Column-menu filters may stay enabled but are never the only way to filter a field that users filter routinely.
- **Check:** a design's "filter by X" requirement maps to a control in `filters`, not "use the column menu"; transactional grids have the date range.
- **Evidence:** inventory §3.4 (one quick search + panel filters + refresh in 40 templates; date range defaulting to 6 months, persisted per user; no column filters in 98 screens); D-10.

### UI-GRID-006 · MUST · Selection uses the dedicated checkbox column
Multi-select grids pass `onSelectionChange`, which shows the pinned-left checkbox column with select-all over the filter. The selection count is shown by `AppCommandBar` (`selectionCount`). Single click on a row selects it.
- **Check:** no `checkboxSelection` on data columns; `selectionCount` is passed when selection commands exist.
- **Evidence:** inventory §3.5; §13; D-12.

### UI-GRID-007 · MUST · Double click (and Enter) on a row opens the record
`onOpen(row)` opens the record's `EntityDialog` (edit, or view if the user can't edit). Group rows and pinned rows don't open.
- **Check:** every grid of records passes `onOpen`.
- **Evidence:** inventory §3.5 (17 grids: double click opens the detail); D-09.

### UI-GRID-008 · MUST · Row actions live in the context menu; bulk and create actions in the command bar
- **`rowActions`** returns the right-click menu items for the clicked or selected rows, in this order:
  1. **Edit**
  2. entity actions
  3. **Copy**
  4. **Flag…**
  5. a separator
  6. **Delete** (destructive)

  Items that support multiple rows stay when several rows are selected; others are hidden.
- **The same destructive and bulk actions** also appear in `AppCommandBar` as `requiresSelection` commands, so they are discoverable without right-click.
- **The default AG context menu** (copy / paste / export) is disabled.
- **Check:** `rowActions` is passed; no `getContextMenuItems` with AG built-ins (`"copy"`, `"export"`); Delete exists both in `rowActions` and as a selection command.
- **Evidence:** inventory §3.6 (context menu in 18 templates + 35 master-data screens; default menu in 37 grids = accidental); D-09.

```tsx
// ✅
rowActions={(rows) => [
  { id: "edit", labelKey: "Edit", icon: Pencil, onClick: () => open(rows[0].id), multi: false },
  { id: "flag", labelKey: "Flag", icon: Flag, onClick: () => setFlagRows(rows), multi: true },
  { id: "delete", labelKey: "Delete", icon: Trash2, destructive: true, onClick: () => confirmDelete(rows), multi: true },
]}
```

### UI-GRID-009 · SHOULD · Action columns only on portal screens
A pinned-right icon action column (`__actions__`) is used **only** on screens for external users (self-service, carrier, distribution portals). It holds at most 2 actions, ghost `icon-sm` buttons with a `Tooltip`. Back-office grids use UI-GRID-008.
- **Check:** `__actions__` columns appear only under portal apps.
- **Evidence:** inventory §3.6 (action columns only in 4 portal screens: deliberate); D-09.

### UI-GRID-010 · MUST · Column state is persisted per user; no default column widths in code
Column order, visibility, width, pinning and sort are saved per user by `OpenTmsGrid` (`gridId`). Code sets `flex: 1` + `minWidth` only and never fixed `width`, except utility columns (selection, actions, flags).
- **Check:** no `width:` on data columns.
- **Evidence:** inventory §3.3–3.4 (per-user server persistence in 43 templates; `flex: 1, minWidth` defaults).

### UI-GRID-011 · SHOULD · Totals go in a pinned bottom row
Totals use a pinned bottom row (`totals` prop). The row is `--grid-totals-row-height` high, has `font-semibold` text, and shows totals only for columns flagged in the design. A selection subtotal row (`(…)`) is optional.
- **Check:** no totals rendered outside the grid for grid columns.
- **Evidence:** inventory §3.4 (pinned-bottom totals + a selection sum in client grids).

### UI-GRID-012 · MUST · No default sort unless the design specifies one
Grids show the server's order until the user sorts. Sort is single-column.
- **Check:** no `sort:` in `ColDef`s unless the design names it.
- **Evidence:** inventory §3.4 (no default sort in any grid except one legacy grid).

### UI-GRID-013 · SHOULD · Export is a toolbar action that produces a server-side Excel file
When a design includes export, the grid toolbar gets an **Export** button (`Download` icon). It opens an `EntityDialog` (`variant="action"`, `size="sm"`) with the choice "visible columns or all columns" and "filtered rows or all rows", then calls the backend export. There is no client-side CSV.
- **Check:** no `exportDataAsCsv` / `exportDataAsExcel` calls.
- **Evidence:** inventory §3.8 (backend export via the `DownloadPreferences` dialog in 23 grids; client export used 0 times).

### UI-GRID-014 · MUST · Document lines in an editor use `LinesGrid`
Lines that belong to the record being edited (invoice lines, freight lines, price-list rows, payments) are edited in `LinesGrid` (`components/ui/lines-grid.tsx`). It is an AG Grid in the **client-side** row model on the editor's form state, with the same theme, and:
- editable cells (`singleClickEdit`, `stopEditingWhenCellsLoseFocus`), using the same cell renderers and formatters as UI-GRID-004;
- a small toolbar above with the add commands (`Button size="sm" variant="outline"`, `Plus` icon: "Add line", "Add transport order");
- remove via the context menu and a ghost `Trash2` icon at the row end, with no confirm, because the change is undone by Close without saving;
- changes that only become persistent on the editor's Save.

It does not use `OpenTmsGrid` (that one is server-side and for lists).
- **Check:** no hand-built `<table>` with inputs for lines; line edits don't call the API before Save unless the design says so.
- **Evidence:** inventory §3.2 (13 raw editable grids in dialogs: deliberate), §5.1 (inline grid editing in 18 TS files, `singleClickEdit` 15, `stopEditingWhenCellsLoseFocus` 6); verification `ui-verification.md` §2.

### UI-GRID-015 · MUST · Document totals are a right-aligned block under the lines
Below a `LinesGrid` (or a lines tab), totals render as a right-aligned label / value list:
- labels in `text-label text-muted-foreground`
- values in `f.amount`, tabular figures
- the last line (grand total) in `font-semibold`, with a top border

Example: Subtotal, VAT (21 %), Total incl. VAT.
- **Check:** totals use `f.amount`; values are right-aligned under the amount column.
- **Evidence:** inventory §5.1 / invoice dialog (`totals-footer`: Subtotal, VatAmount (x%), TotalInclVat; payments Total), §3.4 (pinned totals in list grids); verification §2.

### UI-GRID-016 · MUST · Picker grids select on click
A grid used to pick records inside an action dialog (add transport orders to an invoice, select orders for a wizard step) uses `OpenTmsGrid` with selection, and a **row click toggles the selection** (`rowMultiSelectWithClick`). Double click does nothing in a picker. The footer primary shows the count: `t("AddSelected")` + ` (3)`.
- **Check:** picker grids don't pass `onOpen`.
- **Evidence:** inventory §3.5 (`rowMultiSelectWithClick` in the 4 picker dialogs); verification §2.

### UI-GRID-017 · SHOULD · The design names the default columns; the others ship hidden
Every field users may want is a `ColDef`. Those not in the design's default set get `hide: true`, so users can show them through Columns. "Reset" returns to the default set.
- **Check:** the design lists default-visible columns; the remaining `ColDef`s have `hide: true`.
- **Evidence:** inventory §3.2 (`EntityAttributeDto.isDefault` → `hide = !isDefault`; users pick columns in the tool panel); verification §1.

### UI-GRID-018 · SHOULD · Row grouping only where the backend supports it
Grids enable row grouping (column menu "Group by") only when the list endpoint supports server-side grouping. Group rows show the group value and the child count; totals follow UI-GRID-011.
- **Check:** `enableRowGroup` only on columns the API groups by.
- **Evidence:** inventory §3.8 (grouping on every metadata column with server-side support; group footer totals); verification §1.
