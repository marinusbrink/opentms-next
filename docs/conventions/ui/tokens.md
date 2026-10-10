# UI tokens

Every colour, size and spacing in the new UI comes from a token. This file maps the old Angular values to the new token names. The proposed implementation is [opentms-theme.css](opentms-theme.css); rule UI-TOK-001 forbids raw values.

**Columns:**
- **Old value** is what the Angular app uses ([ui-inventory.md](ui-inventory.md) §1).
- **New token** is the CSS variable, with the Tailwind utility in brackets.
- **Old use** is where the old value appeared.

**Token families:**
- **Stock shadcn tokens** (`background`, `foreground`, `primary`, `muted`, `border`, `input`, `ring`, `destructive`, `radius` …) keep their shadcn meaning and are not repeated here, except where an old value maps onto them.
- **The Tailwind spacing scale** (4px steps) is the spacing token set. Use `gap-2`, `p-4` and so on; never `p-[13px]`.

## 1. Brand (D-01)

**Runtime:** `applyTenantTheme(settings)` runs once after the application configuration loads.
- It sets `--brand` on `document.documentElement`:
  - from `App.UiManagement.CustomColor` when the theme is `custom`
  - otherwise from the theme map below
  - with `#0F6CBD` as the fallback
- All brand shades are derived with `color-mix`, so only `--brand` is ever set.

| Old value | New token | Old use |
|---|---|---|
| `--filogic-base-color` (theme `default` / `custom` `#fba91a`, `red` `#e20613`, `green` `#006847`, `blue` `#38a8c6`, custom = tenant hex) | `--brand` (`bg-brand`, `text-brand`, `border-brand`) | dialog header bars, active tab ink, ribbon / command icons, context-menu icons, section captions, focus ring, links (485 uses) |
| `#fff` text on brand | `--brand-foreground` (`text-brand-foreground`) | header titles, selected grid rows |
| `--filogic-base-hover-color` | `--brand-hover` (`hover:bg-brand-hover`) | hover on brand surfaces (12 uses) |
| `--filogic-base-transparent-color` (brand + `44`/`45` alpha) | `--brand-soft` (`bg-brand-soft`) | soft highlight, drop zones (16 uses) |
| `color-mix(…brand…)` light tints | `--brand-subtle` (`bg-brand-subtle`) | row hover, active nav leaf (21 uses) |
| `--ag-selected-row-background-color` = brand | `--grid-selected-row-bg` / `--grid-selected-row-fg` | selected grid rows (brand fill, white text) |
| `--plt-brand` / `--vop-brand` / hard-coded `#fba91a` (22 + 4) | **removed**: use `--brand` | price-list redesign, vehicle outline, LOS, map (legacy) |

**Brand is never `primary`.** Design #21 keeps `--primary` near-black for buttons. The brand colour is for identity and selection, not for the primary button.

**Theme map** (`App.UiManagement.Theme` → `--brand`):

| Theme | `--brand` |
|---|---|
| `default` | `#FBA91A` |
| `red` | `#E20613` |
| `green` | `#006847` |
| `blue` | `#38A8C6` |
| `custom` | tenant `CustomColor` |
| missing / empty | `#0F6CBD` |

## 2. Surfaces and borders

| Old value | New token | Old use |
|---|---|---|
| `#fff` | `--background` / `--card` (`bg-background`, `bg-card`) | dialog body, grid body |
| `#f8f9fa` (Bootstrap light), `#f4f4f4`, `#f3f2f1`, `#f5f5f5` | `--surface-page` (`bg-surface-page`) | page background, toolbars, detail panels, grid footer rows |
| `#fafafb` | `--surface-section` (`bg-surface-section`) | form section cards in dialogs (invoice-detail convention) |
| `rgba(0,0,0,.06)` | `--border-section` (`border-border-section`) | section card border |
| `rgba(0,0,0,.12)` / `#0000001f` (field outline, dividers), `#ddd`, `#d4d4d4`, `#d6d5d5`, `#e0e0e0` | `--border` / `--input` (`border-border`, `border-input`) | hairlines, field outlines, dialog dividers |
| `2px solid var(--filogic-base-color)` | `border-brand` + `border-b-2` | active tab, selected card |
| `#000`, `#333`, `#1a1a1a`, `#000000de` | `--foreground` (`text-foreground`) | body text |
| `#5f6368`, `#6c757d`, `#999`, `#9aa0a6`, `rgba(0,0,0,.54)` | `--muted-foreground` (`text-muted-foreground`) | secondary text, hints, empty states |

## 3. Semantic tones (feedback, badges, status fallbacks)

| Old value | New token | Old use |
|---|---|---|
| `--danger #dc3545`, Metronic `#f4516c`, `#ff0000`, `red`, `#d93025` | `--destructive` for actions and errors (`text-destructive`, `variant="destructive"`); `--tone-danger-*` for badges (`bg-tone-danger-bg text-tone-danger`) | error text, required-field marks, invalid outline, overdue / cancelled badges |
| `--success #28a745`, Metronic `#34bfa3`, `#1a8a42` | `--tone-success-*` | completed states, load factor OK |
| `--warning #ffc107`, Metronic `#ffb822`, `#f5a302` | `--tone-warning-*` | warnings, pending |
| `--info #17a2b8`, `#1a73e8`, `#36a3f7` | `--tone-info-*` | in progress, informational |
| greys | `--tone-neutral-*` | unknown, archived, draft |

## 4. Status colours (D-02)

Status colours are **tenant data**: a background + font colour pair per state, from `App.ColorSettings.<Entity>.<State>.Background` / `.FontColor`. The `StatusBadge` component (component-map) reads the tenant pair and falls back to the tone in this table when the setting is empty. The tones are the default; the tenant can override them.

| Entity / state | Fallback tone |
|---|---|
| Transport order: PendingAcceptance | warning |
| Transport order: Open | info |
| Transport order: Canceled | danger |
| Transport order: ReadyForInvoicing | success |
| Transport order: Invoiced | neutral |
| Transport order: Archived | neutral |
| Task: Unknown | neutral |
| Task: Requested | warning |
| Task: Accepted | info |
| Task: Planned | info |
| Task: InProgress | info |
| Task: Completed | success |
| Transport file / order status: Expected | neutral |
| Transport file / order status: InProgress | info |
| Transport file / order status: Completed | success |
| Load factor: under threshold | success |
| Load factor: over threshold | danger |

## 5. Flags

| Old value | New token | Old use |
|---|---|---|
| `red`, `#ff8800`, `green`, `blue`, `purple`, `#ff00f0`, `brown`, `grey` (duplicated in 6 files) | `--flag-red` … `--flag-grey` (`text-flag-red`, `bg-flag-red`) | user-chosen row flags and highlight colours |

## 6. Typography

| Old value | New token | Old use |
|---|---|---|
| Poppins (Metronic) / system stack (Material, ag-grid) | `--font-sans` = Geist Variable (D-03) | all text |
| 14px body | `text-sm` (14px) | body, form values, buttons |
| 12px | `text-label` (12px / 16px) | field labels, section captions, grid footer, hints |
| 13px grid text | `--grid-font-size` (13px) | grid cells and headers |
| 15px / 600 dialog title | `text-title font-semibold` | dialog header title |
| 11px | `text-xs` (12px); **11px is dropped** | badges, chips |
| 16px / 18px / 20px headings | `text-base` / `text-lg` / `text-xl` | rare headings, KPI tiles (bespoke) |
| weights 400 / 500 / 600 / 700 | `font-normal` / `font-medium` / `font-semibold` / `font-bold` | 600 = captions and titles; 500 = labels and buttons |
| JetBrains Mono for prices (price-list redesign) | `tabular-nums` on `font-sans` (UI-TOK-009) | aligned figures in grids and totals |
| DM Sans (vehicle outline), `'Segoe UI Menu Test'`, Open Sans, Arial, Verdana | **removed**: `--font-sans` only (UI-TOK-008) | local islands and leftovers |
| (none) | `font-mono` (Tailwind default mono stack) | raw JSON / XML payloads, API keys, technical log lines only |

## 7. Density and sizes (D-04)

| Old value | New token | Old use |
|---|---|---|
| 36px Material input / button | `h-10` (40px) for form fields with a floating label (D-17 A); `h-8` (32px) for buttons and toolbar controls | form fields; buttons |
| 34px / 30px compact controls | `h-7` (`Button size="sm"`) | grid toolbar, inline actions |
| ag-grid row about 42px | `--grid-row-height` 36px | grid rows |
| ag-grid header about 48px | `--grid-header-height` 40px | grid header |
| pinned totals row 30px | `--grid-totals-row-height` 30px | footer totals |
| 38px dialog header bar | `h-dialog-header` (44px) | entity editor header bar (D-05; taller to carry the ribbon look) |
| 40px command strip | `h-command-bar` (40px) | `AppCommandBar` (already 40px) |

## 8. Radius and shadow

| Old value | New token | Old use |
|---|---|---|
| 10px section radius | `rounded-lg` (`--radius` = 10px) | section cards, dialogs, popovers |
| 4px / 5px field and button radius | `rounded-md` (8px): the shadcn control default; old 4px is not kept | inputs, buttons |
| 6px header top corners | the dialog's `rounded-lg`, clipped by `overflow-hidden` | dialog header |
| `50%` / `999px` | `rounded-full` | avatars, pills, badges |
| 62 distinct box-shadows | `shadow-sm` (cards, menus), `shadow-lg` (popovers, toasts), `shadow-xl` (dialogs). **No other shadows.** | |

## 9. Dialog sizes (D-16)

| Old value | New token | Old use |
|---|---|---|
| 400–500 × 175 | `max-w-dialog-confirm` (420px) | confirm dialogs |
| 500 / 550px | `max-w-dialog-sm` (560px) | small master data, action dialogs |
| 700–900px | `max-w-dialog-md` (880px) | medium master data |
| 1200–1400px | `max-w-dialog-lg` (1360px) | invoice, purchase order, vehicle |
| 95–98vw × 95–98vh | `full`: `w-[98vw] h-[96vh]`, inside the `EntityDialog` `size="full"` variant only | transport order / file, relation, price list, wizards |

Viewport units appear in exactly one place, the `full` variant inside `EntityDialog`. Feature code passes `size`, never a width.

## 10. Breakpoints

| Old value | New token | Old use |
|---|---|---|
| Bootstrap sm 576 / md 768 / lg 992 / xl 1200 | Tailwind `sm` 640 / `md` 768 / `lg` 1024 / `xl` 1280 | the back office is desktop-first. Only portals and the nav-pane collapse respond to width. |

## 11. AG Grid theme

The grid must use the AG Theming API (not the legacy `ag-theme-*.css`), fed from these tokens. Proposed `src/components/ui/grid-theme.ts`:

```ts
import { themeQuartz } from "ag-grid-enterprise";

// Read once per render of the grid; tokens may change at runtime (tenant brand).
const css = (name: string) =>
  getComputedStyle(document.documentElement).getPropertyValue(name).trim();

export function opentmsGridTheme() {
  return themeQuartz.withParams({
    fontFamily: "inherit",
    fontSize: parseInt(css("--grid-font-size"), 10),
    rowHeight: parseInt(css("--grid-row-height"), 10),
    headerHeight: parseInt(css("--grid-header-height"), 10),
    accentColor: "var(--brand)",
    selectedRowBackgroundColor: "var(--grid-selected-row-bg)",
    rowHoverColor: "var(--brand-subtle)",
    borderColor: "var(--border)",
    headerBackgroundColor: "var(--surface-page)",
    foregroundColor: "var(--foreground)",
    backgroundColor: "var(--background)",
    wrapperBorderRadius: 0,
    cellHorizontalPadding: 12,
  });
}
```

Selected-row text colour (`--grid-selected-row-fg`) is applied with a `rowClassRules` / CSS rule on `.ag-row-selected`, because the Theming API has no selected-row foreground parameter.
