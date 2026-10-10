# OpenTMS Next: UI style guide

**For:** designers, product owners, developers and reviewers. It explains **why** the UI looks and behaves the way it does.

**Related documents:**
- The binding, checkable rules for the front-end agent are in [ui-rules/](ui-rules/index.md).
- The evidence is in [ui-inventory.md](ui-inventory.md).
- The choices behind the rules are in [open-decisions.md](open-decisions.md).

## The goal in one sentence

A planner who has used OpenTMS for ten years opens the new app and knows where everything is. Information density, grids, dialogs and feedback work the way they always did, minus the inconsistencies and minus the ways the old app lost their work.

**"Equivalent" means the same conventions and behaviour,** not a pixel copy and not a port of Angular code. Where the new app deviates on purpose, it is listed in [proposals.md](proposals.md).

---

## Principles

### 1. The list is home; records come to you
**What users do:** in OpenTMS they live in lists (orders, files, invoices, relations) and the planboard. A record **never takes you away** from where you were.
- A record opens as a dialog over the list, the planboard or even another dialog.
- When it closes, the list is exactly as you left it: scroll position, selection, filters.

**Where this comes from:**
- The old app has 66 editors, and not one is a page route.
- The transport order and file open from anywhere through one global host.

**What we keep, and what we add:**
- We keep this (UI-PAGE-003, UI-DLG-007).
- We add what a route would have given: a deep link (`?open=TransportOrder:<id>`), so a record can be shared.

### 2. Dense, not cramped
**What users want:** planners want to see a lot of rows. The old app ran:
- 36px controls
- 42px grid rows
- 12px labels and 14px text

**What the new app does:** it is slightly denser:
- 32px buttons and toolbar controls (the shadcn default); 40px form fields, because the label floats inside them (D-17 A)
- 36px grid rows

The extra space comes from removing chrome, not from shrinking text: there are no page titles, no breadcrumbs and no padded cards around grids.

**Validation:** density is the one change to check with real planners before building many screens (proposals A17).

### 3. One way to do each thing
**What the old app had:** three UI generations:
- an inherited ABP / Metronic admin
- the Filogic TMS layer
- the recent "invoice-detail" redesign

Each was internally consistent, but together they produced, for example:
- two footer orders
- three red palettes
- four ways to format a number
- 40 dialog widths

**What the rules do:** they pick one way, mostly the TMS layer's, polished with the redesign's look.

**For reviewers:** when you see a second way to do something, it is a defect, not a style.

### 4. The tenant's colour is identity, not decoration
**What the old app did:** each tenant chose a theme colour (orange, red, green, blue or custom). It colours exactly the things that say "this is my OpenTMS":
- the record header bar
- the active tab
- selected rows
- section captions
- command icons

**What we keep:** we keep this mechanism with a single variable (`--brand`).

**What we change:** we stop using the brand colour for primary buttons. A near-black primary button reads as "action" in every tenant colour, and an orange or light-green button with white text does not pass contrast.

### 5. Say what happens, once, where the user is looking
- **Buttons are verbs.** "Delete", "Merge", "Generate invoices", never "Ok".
- **Success:** the dialog closes and a short toast names the subject ("Transport order saved").
- **A failure inside a dialog** shows inside the dialog, and the dialog keeps your input.
- **No double messages.** The old app often showed the same error as both a modal and a toast.

### 6. Don't lose people's work
Things the old app did not do, and the new app does:
- An editor never closes on a stray click outside it.
- Leaving a changed form asks first.
- Print and e-mail save first.
- A button that is busy says so and can't be clicked twice.

These are the deliberate improvements users will thank us for.

### 7. Hide what you can't do; explain what you can't do *yet*
- **Missing permission:** the action is not there. The old app did this 40 times out of 41.
- **Record state blocks an action** (an invoice already sent): the action is visible but disabled, with a tooltip saying why.

---

## Annotated anatomy

### A list screen

```
┌─ Shell header (brand) ──────────────────────────────────────────────────────────────┐
├─ Nav pane ─┬─ Command bar (h-command-bar, surface-page) ───────────────────────────────┤
│ Master     │ [+ New vehicle]  [Delete (3)] [Flag]  │  More ▾          3 selected       │ ← UI-ACT-001: New first, one primary
│ data       ├──────────────────────────────────────────────────────────────────────────┤
│  Vehicles◀ │ [🔍 Search…] [Org unit ▾] [Flags ▾]         [⟳] [⬇ Export] [Columns ▾] [↺] │ ← UI-GRID-005: search, filters, tools
│  Relations │ ☐ │ Name        │ Licence plate │ Type   │ Capacity (kg) │ Archived │ ⚑ │ ← headers carry units (UI-FMT-008)
│  …         │ ☑ │ DAF 12      │ 12-ABC-3      │ Truck  │        24.000 │    ✓     │ ● │ ← numbers right-aligned (UI-GRID-004)
│            │ ☐ │ …           │               │        │               │          │   │   double click opens (UI-GRID-007)
│            │                                                                          │   right-click: Edit, Copy, Flag…, Delete
│            ├──────────────────────────────────────────────────────────────────────────┤
│            │ Total 1.284                                                              │ ← footer count, no pager (UI-GRID-002)
└────────────┴──────────────────────────────────────────────────────────────────────────┘
   no page title (UI-PAGE-002): the nav pane says where you are
```

**Why no page title?** Neither the old business screens nor the new shell have one. The nav pane already says "Vehicles", and a title costs a row of data.

**Why both a context menu and a command bar?** Long-time users right-click and double click; that is how the old app worked in about 50 lists. New users find the same actions in the command bar. Delete was hidden behind right-click before, so it is now in both.

### A record editor (the "record window")

```
┌──────────────────────────────────────────────────────────────────────────────────────┐
│ Invoice 2026-000123  (Open) (Sent)                                                ✕  │ ← brand bar, title = type + identifier,
├──────────────────────────────────────────────────────────────────────────────────────┤   state badges (UI-DLG-004, -012)
│ ┌─────┐ ┌─────┐ │ ┌──────────────┐           General · Delivery · Ledger · 📎 💬 🆔 │ ← ribbon: icon over label in captioned
│ │ 💾  │ │ ✕   │ │ │ Change send… │                                                  │   groups (D-13 A); tabs end-aligned at
│ │Save │ │Close│ │ └──────────────┘                                                  │   the right end of the ribbon row
│ └─────┘ └─────┘ │     INVOICE                                                       │
│    EDIT         │                                                                   │ ← no footer
├──────────────────────────────────────────────────────────────────────────────────────┤
│ ┌ Invoice ──────────────────────┐ ┌ Customer ───────────────────────┐                 │ ← FormSection cards: surface-section,
│ │ Invoice type *                │ │ Invoice to *                    │                 │   caption in brand colour
│ │ [Sales invoice          ▾]    │ │ [Acme Transport BV      ✎ ✕]    │                 │ ← label floats in the outline (D-17 A)
│ └───────────────────────────────┘ └─────────────────────────────────┘                 │   lookup with edit pencil
│ Invoice lines · Payments                                                             │ ← nested tabs start-aligned
│ [+ Add line] [+ Add transport order]                                                 │
│ Description            │ Qty │ Unit price │ VAT % │ Amount                           │ ← LinesGrid, edits saved on Save
│ Transport A → B        │   1 │   850,00   │   21  │ 850,00                           │
│                                                    Subtotal              850,00      │ ← totals right-aligned
│                                                    VAT (21 %)            178,50      │
│                                                    Total incl. VAT (EUR) 1.028,50    │ ← currency once
└──────────────────────────────────────────────────────────────────────────────────────┘
```

**Why actions on top, and why a ribbon?** It is where the old ribbon put them in about 50 editors, so it is where users look; the PO chose to keep the ribbon's icon-over-label buttons and group captions (D-13 A, 2026-10-10). Unlike the old ribbon, Save is visibly the primary.

**Why "Close", not "Cancel"?** In the transport order, "Cancel" meant *cancel the order*. Dismissing is always "Close"; cancelling a business object always names the object.

### A small action dialog

```
┌ Merge relations ─────────────────────────────── ✕ ┐   ← plain header (not a record): UI-DLG-005
│ Merge into *                                      │
│ [Search relation…                          ▾]     │   ← first field focused (UI-KEY-003)
│ 1. All orders of "Acme BV" move to …              │
│ ┌───────────────────────────────────────────────┐ │
│ │ ⚠ This cannot be undone.                      │ │   ← Alert, not red text (UI-FB-008)
│ └───────────────────────────────────────────────┘ │
│                               [Cancel] [ Merge ]  │   ← primary rightmost, verb, destructive
└───────────────────────────────────────────────────┘     (UI-ACT-003/004/005); Enter does not merge
```

### Confirmations

```
┌ Delete transport order ──────────────────────────┐
│ Delete transport order 2026-01234?               │   names the object
│ This cannot be undone.                           │
│                           [Cancel] [ Delete ]    │   focus on Cancel; red Delete
└──────────────────────────────────────────────────┘
```

The old app asked "Confirm / Are you sure? / Ok". Users clicked Ok without reading, because every confirmation looked the same.

---

## Formatting at a glance

| What | nl | en (en-GB) | Notes |
|---|---|---|---|
| Date | 09-10-2026 | 09/10/2026 | grids, forms |
| Date-time | 09-10-2026 14:30 | 09/10/2026 14:30 | always 24h |
| Technical timestamp | 2026-10-09 14:30:15 | same | logs, audit, API messages |
| Amount | 1.234,50 | 1,234.50 | 2 decimals, no currency symbol per line |
| Unit price | 0,12345 | 0.12345 | 2 to 5 decimals |
| Percentage | 12,5 % | 12.5% | |
| Duration | 1d 2u 5m | 1d 2h 5m | |
| Units | in the label: "Weight (kg)" | | never in the value |

**Why numeric dates in grids?** The old grids showed "9 okt. 2026" while every date input and every ETA showed `09-10-2026`. Numeric dates scan and compare faster in a column, and they match what users type.

## Colour at a glance

| Token | Used for | Never for |
|---|---|---|
| `brand` (the tenant's colour) | record header bar, active tab, selected row, section caption, active nav, command icons | buttons, text on white backgrounds for long passages |
| `primary` (near-black) | the one primary button | backgrounds |
| `destructive` | destructive buttons, errors, invalid fields | warnings |
| `tone-*` | badges, warnings, status fallbacks | buttons |
| tenant status colours | order / task / file state badges | anything else |
| `flag-*` | the 8 user flag colours | anything else |
| `surface-page` / `surface-section` | page and toolbar background / form section cards | |

## Typography

**One typeface: Geist** (Geist Variable, installed from npm). It is used for everything: labels, data, numbers, titles.

**Why not keep the old fonts?** Users of the old app never saw one consistent font:
- Metronic chrome rendered in **Poppins**;
- the screens they actually work in (Material form fields, dialogs and every ag-grid) rendered in the **operating system's font** (Segoe UI on Windows);
- the redesigned islands added **DM Sans** and **JetBrains Mono**.

Matching "the old font" would mean picking one of four. Geist is a neutral sans-serif in the same style as those system fonts, so text density stays close to what users see today. It was already the new app's choice (decision D-03).

**The scale:** the old app had 70 different font sizes in practice, which clustered on a handful of steps. Those steps are kept:

| Role | Size / weight | Token | Example |
|---|---|---|---|
| Body text, field values, buttons | 14px / 400 (buttons 500) | `text-sm` | Acme Transport BV |
| Field labels, captions, hints, grid footer | 12px / 500 | `text-label` | Licence plate * |
| Section caption in a dialog | 12px / 600, brand colour | `text-label font-semibold text-brand` | **Customer** |
| Grid cells and headers | 13px | `--grid-font-size` | 12-ABC-3 |
| Dialog title (in the brand bar) | 15px / 600 | `text-title font-semibold` | **Invoice 2026-000123** |
| Rare headings (KPI tiles, empty-state titles) | 16 to 20px / 600 | `text-base` to `text-xl` | 1.284 orders |

There is no 11px text. The old app's 11px badges become 12px, for legibility.

**Aligned figures without a second font.** The price-list redesign switched to a monospace font so that prices line up in a column. The new app gets the same effect with **tabular figures** (`tabular-nums`): every digit takes the same width, in Geist itself.

```
proportional   1.111,10        tabular   1.111,10
               9.999,90                  9.999,90   ← digits line up
```

Grid number, amount and date cells, totals blocks and numeric inputs do this automatically (rule UI-TOK-009).

**Monospace** is reserved for raw technical content: JSON or XML payloads in API-message screens, API keys, technical log lines (UI-TOK-008).

## What this guide does not cover

- **The shell:** header, app launcher, nav pane. It is built separately.
- **The bespoke screens:** planboard, Gantt, maps, dashboards, portals' special pages. Each needs its own design; [ui-rules/bespoke.md](ui-rules/bespoke.md) lists which general rules still apply to them.
- **Components without a library choice yet:** [component-map.md](component-map.md) §4. These are the date picker, the entity lookup's infinite list, tree select, wizard, upload, charts, rich text, PDF viewer, signature, docking and Gantt.

## Changing these conventions

Like every convention in this repository, these change only through a reviewed PR:
1. Change the decision in `open-decisions.md`.
2. Change the affected rules.
3. Keep rule IDs stable; retire a rule rather than re-using its ID.
