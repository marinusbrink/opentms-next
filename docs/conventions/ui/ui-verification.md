# UI rules: verification against three existing screens

Three representative Angular screens, each rebuilt on paper using **only** [ui-rules/](ui-rules/index.md). Everything that had to be guessed is listed under **Gaps**, together with the rule that now closes it. All gaps found are closed; the rule changes were made in the same pass (2026-10-09).

| # | Type | Angular source |
|---|---|---|
| 1 | List screen | Master data → Vehicles (`master-data/master-data-base/master-data-base.component.html` + `MasterDataBaseComponent`; 34 other master-data lists share it) |
| 2 | Master-detail edit screen | Invoice editor (`finance/dialogs/edit-invoices-dialog/`) |
| 3 | Dialog | Merge relations (`master-data/dialogs/merge-relations-dialog/`) |

---

## 1. List: Vehicles

**Loaded:** index → tokens, page, grid, actions, permissions, feedback.

### Page
- UI-PAGE-001: `AppCommandBar`, then a full-height `OpenTmsGrid`.
- UI-PAGE-002: no title; the nav pane shows "Vehicles".

### Command bar
- **New vehicle** (`isPrimary`, `Plus`), shown if `MasterData.Vehicles.Create` (UI-ACT-001, UI-PERM-002/003).
- **Delete**: destructive, `requiresSelection`, `selectionLabelKeys` (UI-ACT-005, UI-GRID-008), shown if `.Delete`.
- **Flag**: `requiresSelection`.
- No module wizards on this list.

### Grid
- `gridId="masterData.fleet.vehicles"`.
- **Toolbar (UI-GRID-005):**
  - search
  - `filters` slot: organisation unit (`TreeSelect`, GAP-02) and flags filter
  - Refresh, Export (backend, UI-GRID-013), Columns, Reset
  - no date range (master data, not transactional)
- **Columns:**

| Column | Cell |
|---|---|
| Name | text |
| Licence plate | text |
| Vehicle type | text |
| Transport company | text |
| Capacity (kg) | `NumberCell`, right-aligned, unit in the header (UI-FMT-008) |
| Loading metres (ldm) | `NumberCell` |
| Archived | `BoolCell` |
| Flags | `FlagCell` |

  Default-visible per the design; the remaining fields are `hide: true` (UI-GRID-017).
- **Selection:** dedicated checkbox column (UI-GRID-006).
- **Double click / Enter:** `openRecord("Vehicle", id)` (UI-GRID-007, UI-DLG-007).
- **Context menu:** Edit, Copy, Flag…, separator, Delete (UI-GRID-008).
- **Footer:** "Total 1.284" (UI-GRID-002).

### States and feedback
- **States** (UI-PAGE-005): skeleton, then the grid; empty overlay; block error with retry; `PermissionDenied` without `.Vehicles`.
- **Delete:** `ConfirmDialog` "Delete vehicles", "Delete 3 vehicles? This cannot be undone.", [Cancel][Delete]. Then `toast.success(t("VehiclesDeleted"))` and a refresh (UI-DLG-011, UI-FB-002).

### Gaps found
| Gap | What I had to guess | Closed by |
|---|---|---|
| L1 | Old lists let users show extra columns from metadata (`isDefault`). Which columns ship, and how are the others reachable? | **UI-GRID-017** (design names the defaults; the rest `hide: true`) |
| L2 | Old lists allow row grouping on every column. Allowed or not? | **UI-GRID-018** (only where the backend groups) |
| L3 | Is there a Refresh button? Where do filters sit relative to search and Columns? | **UI-GRID-005** extended (toolbar order, Refresh) |
| L4 | Do transactional lists get the default date range? | **UI-GRID-005** extended (last 6 months, persisted) |
| L5 | `TreeSelect` for organisation units has no library | not closable by a rule: **GAP-02** in component-map |

---

## 2. Master-detail edit: Invoice

**Loaded:** index → tokens, dialog, form, actions, feedback, lookup, format.

### Dialog
- **Host:** `EntityDialog variant="editor" size="lg"` (UI-DLG-002/003; old 1400px = `lg`).
- **Header** (UI-DLG-004):
  - brand bar, title "Invoice 2026-000123" (new: "New invoice")
  - badges (UI-DLG-012): `StatusBadge` for the invoice state; extra badges "Sent" / "Exported"
- **Command bar** (UI-ACT-002, UI-DLG-005):

| Order | Command | Notes |
|---|---|---|
| 1 | Save | primary |
| 2 | Close | |
| 3 | Change send status | |
| 4 | Change export status | |
| 5 | Print / download | `Printer`, overflow |
| 6 | E-mail | `Mail`, overflow |
| 7 | Delete | destructive, last, only if `.Delete` and the state allows; otherwise `disabled` + `disabledReasonKey "InvoiceAlreadySent"` (UI-PERM-005) |

  Print and e-mail save first if dirty (UI-ACT-008).

### Tabs and sections
- **Tab strip** (UI-FORM-008): end-aligned. General, Delivery, Ledger entries, then the icon tabs Attachments, Remarks, Record identification.
- **General** has four `FormSection`s in a 2×2 grid (UI-FORM-005):
  - **Invoice:** invoice number (read-only), invoice type (`Select`), report definition (`Select`), language (`Select`)
  - **Customer:** invoice-to (`EntityLookup` relation with address lines, Add new, edit pencil: UI-LOOK-001 to 005), e-mail address, reference
  - **Financial:** currency (`Combobox`, more than 15 items), payment term, VAT %
  - **Delivery:** delivery option, send freight documents (`Checkbox`, UI-FORM-010)
- **Fields:** labels above, required asterisks, errors under the field on blur / submit (UI-FORM-001/002/006).
- **Dates:** `DatePicker` in `DD-MM-YYYY` (UI-FMT-004; GAP-03).

### Lines and totals
- **Nested tabs** under General: "Invoice lines" / "Payments", start-aligned (UI-FORM-008 nested).
- **Lines:** `LinesGrid` (UI-GRID-014):

| Column | Cell |
|---|---|
| Description | text |
| Quantity | `NumberCell` |
| Unit price | `f.unitPrice`, 2–5 decimals |
| VAT % | |
| Amount | `AmountCell` |
| General ledger account | |
| Cost centre | |

  Toolbar: "Add line", "Add transport order". Remove via the row icon or the context menu.
- **Totals** (UI-GRID-015): Subtotal, VAT (21 %), Total incl. VAT, right-aligned with `f.amount`. The currency code appears once, in the totals block (UI-FMT-007).
- **"Add transport order"** opens an action dialog `size="lg"` with a picker grid (UI-GRID-016) and footer [Cancel][Add selected (3)].

### Save and close
- **Save:** close, `toast.success(t("InvoiceSaved"))`, refresh the invoice list (UI-FB-002).
- **Errors:** an `Alert` at the top of the body; the dialog stays open (UI-FB-004).
- **Dirty close:** asks "Discard changes?" (UI-DLG-008).

### Gaps found
| Gap | What I had to guess | Closed by |
|---|---|---|
| E1 | Editable invoice lines: `OpenTmsGrid` (server-side) doesn't fit lines in form state. What component, and when are lines persisted? | **UI-GRID-014** `LinesGrid` (client-side, persisted on Save) + component-map row |
| E2 | Where and how do totals render? | **UI-GRID-015** |
| E3 | Nested tab group (Invoice lines / Payments): which alignment, and how deep? | **UI-FORM-008** extended (nested: start-aligned, one level) |
| E4 | E-mail / print on unsaved changes: block, warn or save? | **UI-ACT-008** (save first) |
| E5 | Where is the invoice state shown? | **UI-DLG-012** (`badges` in the header) |
| E6 | "Add transport order" picker: does click select or open? | **UI-GRID-016** |
| E7 | Date picker component | not closable by a rule: **GAP-03** |

---

## 3. Dialog: Merge relations

**Loaded:** index → tokens, dialog, actions, keyboard (+ lookup for the target picker).

### Dialog
- **Host:** `EntityDialog variant="action" size="sm"`, opened from the relation editor's command bar ("Merge", overflow).
- **Title:** "Merge relations" (UI-DLG-013).
- **Header:** plain, no brand bar (UI-DLG-005, D-05).

### Body
- `FormField` "Merge into" with `EntityLookup` (relations; no "Add new", since merging into a new relation makes no sense).
  - The old screen used an unsearchable `mat-select` over all relations, which is legacy (UI-LOOK-001).
- Explanation text with the numbered consequences: localized keys with `{0}` / `{1}`, no em dashes (UI-FB-007).
- `Alert variant="destructive"`: "This cannot be undone." (UI-FB-008).

### Footer and keyboard
- **Footer:** [Cancel][Merge] with `Merge` as `variant="destructive"` (UI-ACT-003/004/005). No extra `ConfirmDialog`: the dialog is the confirmation (UI-ACT-005).
- **Keyboard:** focus starts on Cancel. Enter does not submit, because the primary is destructive (UI-KEY-001/003).

### Outcome
- **Busy:** "Merging…", the dialog can't close (UI-DLG-010).
- **Success:** both this dialog and the source relation editor close (UI-DLG-014), `toast.success(t("RelationsMerged"))`, the relation list refreshes.
- **Failure:** `Alert` in the body (UI-FB-004).

### Gaps found
| Gap | What I had to guess | Closed by |
|---|---|---|
| D1 | The dialog itself says "cannot be undone": is a second confirmation needed? | **UI-ACT-005** extended (the dialog is the confirmation) |
| D2 | Does Enter submit a destructive action dialog? | **UI-KEY-001** extended (never for a destructive primary) |
| D3 | How to show the irreversibility warning (old: a red span) | **UI-FB-008** (`Alert` above the footer) |
| D4 | After merging, the source relation no longer exists, but its editor is still open behind the dialog | **UI-DLG-014** |
| D5 | Title wording versus button wording | **UI-DLG-013** |

---

## Result

- **17 gaps found.** 15 are closed with new or extended rules: UI-GRID-005, -014, -015, -016, -017, -018; UI-FORM-008; UI-ACT-005, -008; UI-KEY-001; UI-FB-008; UI-DLG-012, -013, -014.
- **2 depend on library choices** (GAP-02 tree select, GAP-03 date picker) and stay open in [component-map.md](component-map.md) §4.
- **No rule contradicted another** during the walkthroughs.
- **One rule was corrected:** UI-ACT-004 had a wizard "Finish" exception that conflicted with "label = verb", so it now uses the verb (UI-WIZ-002).
