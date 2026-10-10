# UI: open decisions

## Decision log

**Status 2026-10-09:** the product owner asked to proceed with phase 2 without overriding any item, so **every decision below takes its recommended option**. Rules cite decisions as `D-nn`. To revise a decision, change the line here and the rules that cite it.

**Revised 2026-10-10:** after seeing the first screens built on the recommendations, the product owner chose **the old look and feel, modernised**: **D-13 → A** (the ribbon on top of an editor: icon-over-label buttons in captioned groups, with Save visibly the primary) and **D-17 → A** (the label floats inside the field's outline), and the editor body sits on the `surface-page` colour. The rules UI-DLG-005, UI-ACT-007 and UI-FORM-001/003 and tokens.md §7 were changed in the same pass.

| Group | Decisions taken |
|---|---|
| A. Look and feel | D-01 A, D-02 A, D-03 A, D-04 B, D-05 C, D-06 A |
| B. Page and grid | D-07 A, D-08 A, D-09 C, D-10 B, D-11 A, D-12 A |
| C. Dialogs, forms, actions | **D-13 A** (revised 2026-10-10), D-14 A, D-15 A, D-16 A, **D-17 A** (revised 2026-10-10), D-18 B, D-19 B, D-20 A, D-21 A, D-22 A |
| D. Formatting | D-23 A, D-25 A |
| E. Smaller items | D-26 to D-35: recommendation |

**Still open:**
- **D-24 (timezone).** The recommendation depends on what the opentms-next backend exposes. Until that is known, rules use the browser timezone through one shared formatter (UI-FMT-005), so switching later is a one-file change.

These are the places where the Angular app is too inconsistent for the inventory to determine a rule, or where the old app and the new app's existing precedents (`opentms-next/frontend`) conflict. Each needs a human choice before phase 2. Evidence is in [ui-inventory.md](ui-inventory.md) (§ references).

**How to read each item:**
- **Options** list the count behind each one.
- **Recommendation** is my suggestion. It is not a decision.
- **"New app"** means what `opentms-next/frontend` already does today.

You can answer briefly, for example "D-01 A, D-02 A, D-05 B …". Items marked **(blocking)** shape many rules; the others can fall back to the recommendation if you don't object.

---

## A. Look and feel

### D-01 Brand colour and tenant theming (blocking)
**Old app:**
- One brand colour per tenant (§1.1): 5 themes (orange default, red, green, blue) plus a free custom colour, via the tenant setting `App.UiManagement.Theme` / `CustomColor`.
- It colours dialog header bars, tab ink, grid selection, ribbon icons and section captions (485 uses).

**New app:** a fixed `--brand: #0F6CBD`. Design #21 forbids mapping brand to `--primary`.

| Option | Description |
|---|---|
| **A** | Keep tenant theming. `--brand` is set at runtime from the tenant setting (fallback `#0F6CBD`). Rules use `brand` for the same roles as in the old app. `primary` stays neutral for buttons. |
| B | A fixed product brand (`#0F6CBD`) for everyone. Tenant colours are dropped. |
| C | A fixed brand, plus a tenant accent used only in the header bar. |

**Recommendation: A.** Tenants who chose red or green will notice immediately, and the mechanism is a single CSS variable. It requires the same contrast check per tenant colour that design #21 did for blue; flag it in proposals.

### D-02 Status colours
**Old app:** order, task and file status colours are **tenant-configured** background + font pairs (`App.ColorSettings.*`, empty defaults; §1.3).

**New app:** hard-coded Tailwind pill colours.

| Option | Description |
|---|---|
| **A** | Honour the tenant settings. Define default tokens (`--status-<state>-bg` / `-fg`) as a fallback when the setting is empty. |
| B | Fixed status tokens; drop the tenant settings. |

**Recommendation: A.** It is business configuration that customers set.

### D-03 Font
**Old app:** Material fields and ag-grid render in the **system font stack**; Metronic chrome renders in Poppins (§1.5). Users mostly see the system font.

**New app:** Geist Variable.

| Option | Description |
|---|---|
| **A** | Keep Geist (new app). |
| B | System font stack (closest to what users see today). |
| C | Poppins. |

**Recommendation: A.** The font is not what makes users feel at home; density is (D-04).

### D-04 Density (blocking)
**Old app (§1.10):**
- 36px inputs and buttons (Material density -3)
- ag-grid Alpine with about 42px rows and 48px header
- 12px labels, 14px body, 12–13px grid text

**New app:**
- shadcn base-nova: 32px inputs (`h-8`), 32px default button, 14px text
- `ag-theme-quartz` defaults (about 42px rows), not wired to tokens

| Option | Description |
|---|---|
| A | Match the old app: 36px controls, 42px grid rows. |
| **B** | Keep the shadcn 32px controls, and set grid row height 36px / header 40px via the AG Theming API tied to the tokens. Slightly denser than today. |
| C | shadcn and AG defaults as-is. |

**Recommendation: B.** Equally or more dense is what TMS planners want, and the grid gets wired to the tokens. Say A if you want a 1:1 feel.

### D-05 Dialog header style (blocking)
**Old app:** every G2 / G3 dialog has a **coloured title bar** (38px in the brand colour, white title, close X right; §5.4). That covers `base-dialog-template` 41, `base-header` 50, the confirm dialog and copied headers.

**New app:** a plain white dialog with a bold title and no bar.

| Option | Description |
|---|---|
| A | Brand-coloured title bar on all dialogs (old app). |
| B | Plain shadcn dialog header (title + close X) for all dialogs. |
| **C** | Brand-coloured bar only for **entity editor** dialogs (the "record window"); plain header for confirm and action dialogs. |

**Recommendation: C.** The bar is the strongest "this is a record" cue users have, while small confirms look calmer without it. A is the most faithful choice.

### D-06 Icon set
**Old app:** Material Icons / Symbols are dominant (482 uses); Font Awesome is legacy.

**New app:** lucide.

| Option | Description |
|---|---|
| **A** | lucide, with a mandatory action→icon mapping table that preserves the old meanings (§1.9). |
| B | Material Symbols. |

**Recommendation: A.**

---

## B. Page and grid

### D-07 Page title
**Old app:** **no page titles** on business screens. The shell shows the app name, the sidenav shows the selected item (§2.1). Only G1 admin pages have an `h3` subheader.

**New app:** no page title either.

| Option | Description |
|---|---|
| **A** | No page title on list pages; the nav pane selection is the title. |
| B | A small page title above the command bar. |

**Recommendation: A.** It is consistent in both apps and saves vertical space.

### D-08 Command bar style
**New app:** the command bar has two styles behind the ABP feature `UI.CommonToolbar`: a boxed button bar (off) and an Office-365 text strip (on, design #27).

**Old app:** module toolbars are stroked icon + text buttons (5 shells), and the planboard uses an icon-over-label ribbon.

| Option | Description |
|---|---|
| **A** | The Office-365 strip is the rule; the boxed bar is retired once the flag is fully rolled out. |
| B | The boxed bar is the rule. |
| C | Both remain, chosen by the flag (the rules must describe both). |

**Recommendation: A.** It is closest to the ribbon heritage users know.

### D-09 Grid row actions (blocking)
**Old app (§3.6):** row actions are in the **right-click context menu** (Edit / Delete / Flag (Copy); 18 templates + 35 master-data screens), and **double click opens** the record. Action columns exist only in portals (4 screens, external users).

**New app:** a pinned-right action column with icon buttons and an inline delete popover. There is no context menu and no double click.

| Option | Description |
|---|---|
| A | Context menu + double click only (old app). |
| B | Action column only (new app). |
| **C** | Both. Double click opens. The context menu carries the full action set (single and multi selection). The command bar carries create and selection actions. A narrow action column is optional, only for one or two very frequent actions, and **mandatory on portal screens**. |

**Recommendation: C.** Power users rely on right-click and double click. The command bar makes actions discoverable without a column per grid.

### D-10 Grid column filters
**Old app:** **no column filters.** Filtering is one free-text search above the grid plus dropdown filters (date range, organisation unit, category, flags) and sidenav presets (§3.4).

**New app:** free-text search **plus** AG text column filters in the column menu.

| Option | Description |
|---|---|
| A | Search + explicit filter controls only; disable column filters. |
| **B** | Search + filter controls in the grid toolbar as the primary path. Column filters are allowed as a secondary power feature. |
| C | Column filters as the primary filtering mechanism. |

**Recommendation: B.**

### D-11 Number alignment and number formatting in grids
**Old app:**
- numbers and amounts are **left-aligned and unformatted** in server grids
- the `alignRight` metadata is ignored (a gap, not a choice)
- totals are hard-coded nl-NL (§3.3, §8.3)

Matching the old behaviour exactly would mean copying a bug.

| Option | Description |
|---|---|
| **A** | Numbers and amounts right-aligned, locale-formatted (grouping + fixed decimals), tabular figures. The rule cites the `alignRight` metadata and the invoice / price-list screens as evidence of intent. |
| B | Keep left-aligned (exact old behaviour). |

**Recommendation: A.** I will record this as a correction in `proposals.md` as well, so it is visible that it is a change.

### D-12 Selection checkbox position
**Old app:** the checkbox is on the **first data column** (§3.5).

**New app:** a dedicated pinned-left 40px checkbox column.

| Option | Description |
|---|---|
| **A** | Dedicated column (new app). |
| B | First data column (old app). |

**Recommendation: A.** It is a cosmetic difference and the dedicated column survives column reordering.

---

## C. Dialogs, forms, actions

### D-13 Where entity editors put their actions (blocking)
**Old app:**
- entity editors (about 51) have a **ribbon at the top**: Save, Cancel / Close, entity actions, document actions; icon over label; no visual primary; no footer
- action and confirm dialogs (about 55) have a **footer**

**New app:** dialogs have a footer only.

| Option | Description |
|---|---|
| A | Ribbon at the top for entity editors; footer for action / confirm dialogs (old app split). |
| B | Footer everywhere. Entity actions (Archive, Approve, Print, E-mail) go in a "More" menu or the footer's left side. |
| **C** | Entity editors get a **command bar at the top** (the same `AppCommandBar` component and style as list pages: Save as primary, then entity actions, documents in overflow). Action and confirm dialogs get a footer. |

**Recommendation: C.** It keeps the ribbon position and order users know, reuses one component, and adds the visual primary that is missing today.

**Decided 2026-10-10: A, modernised.** The PO saw option C built and wants the ribbon: icon-over-label buttons (brand icon, 14px label, outlined, about 68px tall) in groups with an uppercase caption under them ("Edit"), a divider between groups, under the brand bar; Save keeps a visible primary treatment; the dismiss command stays **Close** (D-20). The list pages keep the Office-365 strip (D-08).

### D-14 Footer button order (blocking)
**Old app:** the split is about 53 vs 13 (§6.3):
- **Cancel left, primary right:** the shared `base-ok-cancel-footer` (19), most bespoke footers, all G1 modals, the newest G3 dialog.
- **Primary left, Cancel right:** the shared **confirm dialog** (46 call sites) and 12 older dialogs.

**New app:** Cancel left, primary right.

| Option | Description |
|---|---|
| **A** | Primary rightmost, Cancel directly to its left; right-aligned. |
| B | Primary first. |

**Recommendation: A.** It matches the majority, the new app and platform convention. The confirm dialog changes, so mention it in release notes.

### D-15 Edit host: dialog vs full page (blocking)
**Old app:** **edit is always a dialog over the list**, never a route (§5.1-5.2). Size follows the entity:
- small master data: 500–550px
- large master data: 96–98vw
- documents: 1400px
- transport order / file: a global near-full-screen modal that can open from anywhere, including over the planboard

| Option | Description |
|---|---|
| **A** | Always a dialog (old app). Size tiers (D-16). The large records (transport order, transport file, relation, invoice, price list) use the full tier and have a deep link (`?open=<type>:<id>`) that opens the dialog over the current page. |
| B | Small entities in a dialog; large entities as a routed full page. |
| C | Everything as a routed page or side sheet. |

**Recommendation: A.** "I never lose my list or planboard" is the strongest habit in the app. Deep links add the shareable URL a route would give.

### D-16 Dialog size tiers
**Old app:** about 40 distinct literal widths, clustering into tiers (§5.3).

**Proposed tokens:**

| Token | Size | Use |
|---|---|---|
| `confirm` | 420px | confirmations |
| `sm` | 560px | small master data, action dialogs |
| `md` | 880px | medium master data |
| `lg` | 1360px | documents: invoice, purchase order |
| `full` | 98vw × 96vh | transport order / file, relation, price list, wizards |

| Option | Description |
|---|---|
| **A** | Accept these tiers (only tiers; no other widths). |
| B | Adjust (tell me the values). |

**Recommendation: A.**

### D-17 Label position and placeholder-as-label (blocking)
**Old app:** **outline fields with a floating label inside the border** (979 fields; §4.1). The label doubles as the placeholder.

**New app:** **label above the field** (shadcn), with a literal ` *`.

| Option | Description |
|---|---|
| A | Floating outline label (a custom shadcn field component; shadcn has none). |
| **B** | Label above, compact: 12px label, 4px gap, 32px input. Vertical density stays close to the old floating label. |

**Recommendation: B.** A floating label is a sizeable custom component and fights shadcn. B with a 12px label keeps the look compact. The required asterisk stays in both options; both apps do it.

**Decided 2026-10-10: A, modernised.** The label floats inside the outline: it reads as the placeholder (14px, muted) while the field is empty and unfocused, and sits on the top border (12px, on the field's background) once focused or filled. Fields are 40px high. Controls that have no placeholder (pickers, selects) keep the label on the border. `FormField` renders this; feature code is unchanged.

### D-18 Validation message placement
**Old app (§4.5):**
- G2 shows **no inline messages**: a red outline plus a disabled Save, with server errors as toasts
- G1 shows messages under the field (52)
- relation's red tab title flags a tab with errors

**New app:** browser `required` plus an inline server-error paragraph.

| Option | Description |
|---|---|
| A | Red outline + disabled Save, no message text (G2). |
| **B** | Red outline + message under the field after blur or submit. Tabs with errors get a red title (relation pattern). Save stays enabled, and submitting focuses the first invalid field. |
| C | B, but Save stays disabled while invalid (G2 behaviour). |

**Recommendation: B.** A disabled Save without a reason is the most common "why can't I save?" complaint pattern, and B has evidence in G1 and in relation.

### D-19 Unsaved changes
**Old app:** **no dirty tracking at all** (§4.6). Most master-data dialogs close on backdrop or Esc and silently lose edits.

| Option | Description |
|---|---|
| A | Same as old: no guard. |
| **B** | Entity editors: backdrop click never closes; Esc / X / Cancel on a dirty form asks "Discard changes?". Action dialogs: unchanged. |

**Recommendation: B.** I'll put it in `proposals.md` too, since it is new behaviour.

### D-20 Save semantics
**Old app:**
- **Save = save and close** in all 41 ribbon editors
- only the transport order has **Save & Close + Save only + Close**, where "Cancel" means *cancel the order*

| Option | Description |
|---|---|
| **A** | Save = save and close everywhere. Large records (the D-15 full tier) additionally get "Save only" (stays open). The dismiss action is always labelled **Close** in editors. Business cancellation is labelled with the object ("Cancel order"). |
| B | Exact old behaviour (Save only for the transport order). |

**Recommendation: A.**

### D-21 Destructive confirmations
**Old app:**
- the shared confirm (46) shows "Confirm" + question + a **blue "Ok"**
- delete is not styled as destructive (3 of 34)

**New app:**
- single delete: an inline popover confirm
- bulk: a dialog
- confirm button labelled with the verb and styled destructive

| Option | Description |
|---|---|
| **A** | Always a confirm **dialog** (old habit). The confirm button is labelled with the verb ("Delete", "Archive") and styled destructive for destructive actions. |
| B | Inline popover for single-row deletes, dialog for bulk and for everything else (new app). |
| C | Old app exactly ("Ok"). |

**Recommendation: A.** The dialog is what users know, while the verb and colour fix a real gap. It means changing the new app's popover.

### D-22 Error presentation
**Old app (§7.4):** errors show as a **toast** (514 calls), and often *also* as a global modal from the interceptor (double display, unintended).

**New app:** errors render inline in the dialog, never as a toast.

| Option | Description |
|---|---|
| **A** | Error inside a dialog (save, delete): inline alert at the top of the dialog body, and the dialog stays open. Error from a list-level or background action: error toast. Never both. No global error modal except for session expiry. |
| B | Always a toast (old app). |

**Recommendation: A.**

---

## D. Formatting

### D-23 Date and time formats; the "en" locale
**Old app:**
- grids show `ll` ("9 okt. 2026")
- operational ETA cells and pickers use `DD-MM-YYYY HH:mm`
- logs use `YYYY-MM-DD HH:mm:ss`
- "en" was intended to be **en-GB** but falls back to en-US because of a bug; the en picker uses a 12h clock (§8.1-8.2)

| Option | Description |
|---|---|
| **A** | Three formats only, from the user culture via `Intl`. nl → nl-NL, en → **en-GB**, others → their default. Always **24h**. |
| B | Different (specify). |

**Option A formats:**

| Format | Example | Used for |
|---|---|---|
| **date** | `09-10-2026` / `09/10/2026` | |
| **date-time** | `09-10-2026 14:30` | |
| **technical timestamp** | `2026-10-09 14:30:15` | logs, audit, API messages |

`ll`-style medium dates ("9 okt. 2026") are allowed only in read-only summaries.

**Recommendation: A.** It drops `ll` from grid columns in favour of numeric dates, which sort and scan better and match the pickers. Say if you'd rather keep `ll` in grids.

### D-24 Timezone
**Old app:** display uses the **browser** timezone, despite user / tenant timezone settings existing (§8.1).

| Option | Description |
|---|---|
| A | Browser timezone (old behaviour). |
| **B** | The user's timezone setting, falling back to tenant, then browser. Stop times show the stop's local time where the backend provides it. |

**Recommendation: B**, but this depends on what the opentms-next backend exposes. If unknown, A for now.

### D-25 Units and currency display
**Old app:**
- units are absent from freight inputs and grids
- currency symbols are absent per line (one currency per document, shown once)

| Option | Description |
|---|---|
| **A** | Currency: no symbol per line in documents; the document shows its currency code once. Units: shown in the column header and field label ("Weight (kg)"), never repeated in cells. |
| B | Exact old behaviour (no units anywhere). |

**Recommendation: A.**

---

## E. Smaller items

You can accept these as a block.

| ID | Question | Old app | Recommendation |
|---|---|---|---|
| D-26 | Enter key | Small dialogs confirm on Enter via a document-level listener (11), including the destructive `reset-account`. Editors don't save on Enter. | Enter submits the focused form in action dialogs and small editors, but **never confirms a destructive action**. Enter does not save large editors (multi-line content, grids inside). |
| D-27 | Initial focus | No convention; focus often lands on the X or the ribbon. | First editable field in forms; **Cancel** in destructive confirms; the primary button in non-destructive confirms. |
| D-28 | Disabled for permissions | Hide is the rule (40 of 41); one disabled teaser (Arrowline). | Hide. Disable with a reason tooltip only for business state (`disabledReasonKey`), never for permissions. |
| D-29 | Action-level permissions | Business screens have none client-side. | Every command gets a policy and is hidden without it (the new backend has fine-grained policies). |
| D-30 | Lookup debounce / minimum | Base wrapper: 200ms / 3 characters; clones: none. | 250ms / 2 characters (the new app's grid search uses 300ms). Or keep 200 / 3: your choice. |
| D-31 | Lookup selected display | Name only, everywhere. | Name only. The option list may show secondary lines (address, licence plate). |
| D-32 | Wizard footer | Previous / Submit / Next; the newest wizards add Cancel. | Cancel (left), then Previous, Next / Submit (right, primary). |
| D-33 | Toast position | top right (toastr). New app: bottom right. | Bottom right (new app, shadcn Sonner default). |
| D-34 | Grid empty / loading texts | English ag-grid defaults (gap). New app: localized overlays + "Clear filters". | New app behaviour. |
| D-35 | Master-data tab skeleton | Right-aligned tabs; "General" first; icon-only Attachments / Remarks / Identification (49 files). | Keep exactly, as a rule. |
