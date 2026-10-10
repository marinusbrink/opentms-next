# UI proposals: where the new app deliberately differs from the old

The rules are based on what the Angular app does. Where a rule **changes** user-visible behaviour, because the old behaviour was a gap or a bug, it is listed here so it can be communicated, tested with users and reverted if needed.

**Two parts:**
- **Part A:** changes that are already rules, because the product owner accepted the recommendation in [open-decisions.md](open-decisions.md).
- **Part B:** ideas that are **not** rules. Nothing in Part B binds the front-end agent.

## A. Accepted changes (already in the rules)

| # | Change | Old behaviour | Rule | Decision | Users will notice |
|---|---|---|---|---|---|
| A1 | Numbers right-aligned and locale-formatted in grids | left-aligned, raw (`alignRight` metadata ignored) | UI-GRID-004, UI-FMT-006 | D-11 | yes, positively |
| A2 | Inline validation messages under fields; Save stays enabled | red outline + disabled Save, no message | UI-FORM-006 | D-18 | yes |
| A3 | Unsaved-changes guard in editors; backdrop never closes an editor | 29 of 40 master-data dialogs lost edits on backdrop click | UI-DLG-008, UI-FORM-009 | D-19 | yes |
| A4 | Confirm button says the verb and is red for destructive actions | blue "Ok" for every confirmation | UI-DLG-011, UI-ACT-005 | D-21 | yes |
| A5 | Footer order Cancel, then Primary in the shared confirmation | Ok, then Cancel in the confirm dialog (46 places) | UI-ACT-003 | D-14 | yes (muscle memory); **mention in release notes** |
| A6 | Errors shown once, inline in dialogs | interceptor modal + toast for the same error | UI-FB-004 | D-22 | yes, positively |
| A7 | Numeric dates in grids (`09-10-2026`) instead of `9 okt. 2026` | `ll` in grid columns | UI-FMT-003 | D-23 | yes |
| A8 | "en" formats as en-GB, 24-hour clock | en-US by accident; 12-hour clock in the en picker | UI-FMT-002 | D-23 | en users only |
| A9 | Units in labels and headers | no units on freight inputs | UI-FMT-008 | D-25 | yes, positively |
| A10 | Action-level permissions enforced in the UI | business ribbons and menus had no checks | UI-PERM-003 | D-29 | only users with restricted roles |
| A11 | "Close" dismisses editors; business cancel is "Cancel order" | "Cancel" meant both | UI-ACT-002 | D-20 | yes |
| A12 | "Save only" on all large records | only on the transport order | UI-ACT-002 | D-20 | yes |
| A13 | First field focused when an editor opens | focus often on the X or a ribbon button | UI-KEY-003 | D-27 | yes, positively |
| A14 | Enter no longer confirms destructive dialogs | document-level Enter handler, including reset-account | UI-KEY-001 | D-26 | rarely |
| A15 | Lookup search starts at 2 characters with a 250ms debounce | 3 characters and 200ms (base); no limits in clones | UI-LOOK-002 | D-30 | slightly |
| A16 | Toasts bottom right | top right | UI-FB-001 | D-33 | yes |
| A17 | Denser grids (36px rows) and 32px controls | 42px rows, 36px controls | tokens.md §7 | D-04 | yes; **validate with 2–3 planners** |
| A18 | Brand colour no longer used for primary buttons | `.btn-primary` forced to the brand colour | UI-TOK-002 | D-01 / design #21 | slightly |
| A19 | A plain header on small action and confirm dialogs | brand bar on every dialog | UI-DLG-005 | D-05 | slightly |
| A20 | Context-menu row actions also available in the command bar | delete only via right-click | UI-GRID-008 | D-09 | yes, positively |

## B. Ideas, not rules

These came up during the inventory. Each needs a design decision before any agent may implement it.

| # | Idea | Why | Evidence |
|---|---|---|---|
| B1 | **Tinted selected rows** (`--brand-subtle`) instead of a brand fill with white text | White on a light tenant colour (orange `#FBA91A`) fails WCAG contrast: about 2:1 | tokens.md §1; D-01 |
| B2 | **A contrast check for tenant brand colours**: when an admin picks `CustomColor`, warn if white text on it is below 4.5:1, and derive `--brand-foreground` automatically (white or black) | The same contrast issue for header bars | design #21 (4.6:1 check for blue only) |
| B3 | **Ctrl/Cmd+S saves** the open editor | Power users in data entry; no evidence of demand in the old app | inventory §10 (no shortcuts) |
| B4 | **Use the user's timezone setting** for display (D-24 still open) | Planners in other timezones see browser-local times today | inventory §8.1 |
| B5 | **Dark mode**: tokens are defined for it, but no switch exists | Night-shift planners | §13 (dark variant defined, never enabled) |
| B6 | **Saved grid views** (named column + filter sets per user) instead of nav-pane presets only | Generalises the transport sidenav presets | inventory §2.2 |
| B7 | **Bulk edit** (set a field on all selected rows) | Several wizards exist only to set one field (discounts / surcharges, flags) | inventory §12.1 |
| B8 | **Keyboard-only line entry**: Enter on the last cell of the last line adds a new line in `LinesGrid` | The transport-order freight line has this (Enter adds a line); generalise it | inventory §10 |
| B9 | **Responsive editors** for tablet use in warehouses (portals already need `md`) | Desktop-first today | inventory §1.8 |

## Fixes for the Angular app (found during the inventory, out of scope here)

These are bugs in the current production app, worth a ticket regardless of the rebuild:

- `src/root.module.ts:116-121`: `convertAbpLocaleToAngularLocale` never matches, so "en" uses en-US instead of en-GB.
- moment `'dd MMMM YYYY'` renders a weekday instead of the day number in 4 arrowline files (`arrowline-transport-file.component.ts:262`, `filter-arrowline-transport-file.pipe.ts:14`, `filter-order-requests.pipe.ts:16`, `transport-order-request.component.ts:476`).
- `planboard/transport-orders/transport-orders.component.ts:544`: the `findIndex` truthiness bug applies `sum` to almost every column.
- Errors probably shown twice (interceptor modal + per-call toast): `interceptors/filogic.inerceptor.ts`.
- `admin/tenants/tenants.component.html:13`: AngularJS `ng-if`, so the permission check does nothing.
- The green theme's transparent brand colour has a different hue (`#14c4554c` vs `#006847`).
