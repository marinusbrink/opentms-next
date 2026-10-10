# Tokens (UI-TOK)

**Load for:** every UI task.

Token names and values: [../tokens.md](../tokens.md). Proposed implementation: [../opentms-theme.css](../opentms-theme.css).

---

### UI-TOK-001 · MUST · No raw colours, sizes or shadows in feature code
Feature code uses only token utilities (`bg-brand`, `text-muted-foreground`, `border-border-section`, `max-w-dialog-md`, the Tailwind spacing scale). There are no hex, rgb or oklch values, and no arbitrary values in brackets (`p-[13px]`, `bg-[#F8F9FA]`, `w-[550px]`).
- **Check:** grep the diff for `#[0-9a-fA-F]{3,8}\b`, `rgb(`, `oklch(` and `-\[` inside `className`. Every hit outside `src/styles/` and `components/ui/` is a violation. Inside `components/ui/` a hit needs a comment naming the token it implements.
- **Evidence:** inventory §1.4 (177 distinct hex values, about 15 near-duplicate greys), §1.7 (62 distinct shadows), §5.3 (about 40 dialog widths).

```tsx
// ✅
<div className="bg-surface-section rounded-lg border border-border-section p-4" />
// ❌
<div className="rounded-[10px] border border-[rgba(0,0,0,.06)] bg-[#fafafb] p-[15px]" />
```

### UI-TOK-002 · MUST · The brand colour is identity, not the primary button
Use `brand` for:
- the entity-editor header bar
- the active tab indicator
- the active nav item
- grid row selection
- section captions
- command icons
- links

Never use it for `Button` fills. Buttons use the shadcn variants (`default` = `primary`).
- **Check:** no `bg-brand` on a `<Button>` or `buttonVariants`. No `--primary` redefined to the brand.
- **Evidence:** inventory §1.1 (brand roles, 485 uses); design #21; D-01.

```tsx
// ✅
<Button onClick={save}>{t("Save")}</Button>
<h3 className="text-label font-semibold text-brand">{t("Customer")}</h3>
// ❌
<Button className="bg-brand text-white">{t("Save")}</Button>
```

### UI-TOK-003 · MUST · The brand colour is tenant-specific: never hard-code it
Only `applyTenantTheme()` sets `--brand`. Code never assumes a particular brand hue: no "orange" or "blue" in names, no fixed brand-coloured images or SVG fills. Contrast-sensitive text on brand surfaces uses `text-brand-foreground`.
- **Check:** grep for `#0F6CBD`, `#fba91a`, `#FBA91A` outside `src/styles/` and `applyTenantTheme`. SVG icons use `currentColor`.
- **Evidence:** inventory §1.1 (5 themes + custom colour; the hard-coded orange broke tenant theming in 9 files); D-01.

### UI-TOK-004 · MUST · The AG Grid theme comes from tokens
Grids get their theme through `opentmsGridTheme()` (the AG Theming API with token-based params). Never import `ag-theme-*.css` and never set `rowHeight`, `headerHeight` or colours per grid.
- **Check:** no `ag-theme-` class or CSS import; no `rowHeight=` / `headerHeight=` props outside `OpenTmsGrid`.
- **Evidence:** inventory §1.10 (one theme, no per-grid overrides in 61 grids); tokens.md §11; D-04.

### UI-TOK-005 · MUST · Status, flag and tone colours come from their own token families
- Business states use `StatusBadge` (tenant colour pair with tone fallback).
- Row flags use `--flag-*`.
- Feedback uses `destructive` and the `tone-*` tokens.
- **Never** use Tailwind palette colours (`bg-green-100`, `text-red-800`).
- **Check:** grep the diff for `(bg|text|border)-(red|green|blue|amber|yellow|gray|slate|sky|emerald|rose|violet|indigo)-\d`.
- **Evidence:** inventory §1.2–1.3 (tenant `App.ColorSettings`, three conflicting semantic palettes); D-02.

```tsx
// ✅
<StatusBadge entity="TransportOrder" state={row.orderState} />
// ❌
<span className="rounded-full bg-green-100 px-2 text-green-800">{t("Invoiced")}</span>
```

### UI-TOK-006 · MUST · Use the type scale only
The type scale:

| Use | Classes |
|---|---|
| body | `text-sm` |
| labels, captions, hints, footers | `text-label` |
| dialog title | `text-title font-semibold` |
| rare headings | `text-base` / `text-lg` |

Weights: `font-medium` for labels and buttons, `font-semibold` for captions and titles. There is no `text-[11px]`, no `text-xs` for body text, and no `font-bold` on body text.
- **Check:** grep for `text-\[` and `font-bold` in the diff.
- **Evidence:** inventory §1.5 (de facto scale 11–20px, 12px labels, 15/600 title, 600 captions).

### UI-TOK-007 · SHOULD · Spacing follows a small set of steps
Use spacing steps 1, 2, 3, 4, 6 (4, 8, 12, 16, 24px):

| Use | Step |
|---|---|
| inside a field | `gap-1` |
| between fields | `gap-3` |
| between sections | `gap-4` |
| dialog body padding | `p-4` |
| page gutters | `px-6` |

- **Check:** spacing classes outside {0.5, 1, 1.5, 2, 3, 4, 6} need a reason.
- **Evidence:** inventory §1.6 (G3 code uses a 4px grid; legacy 5px grid dropped).

### UI-TOK-008 · MUST · One font family: `--font-sans` (Geist)
- **All UI text** uses the inherited `font-sans` (Geist Variable, from `@fontsource-variable/geist` in `index.css`).
- **Feature code** never imports fonts (`@fontsource/*`, Google Fonts links, `@font-face`), never sets `font-family` / `fontFamily`, and never uses `font-[…]`.
- **`font-mono`** is allowed only for raw technical content: JSON / XML payloads, API keys, log lines in technical screens.
- **Check:** grep the diff for `@font-face`, `fonts.googleapis`, `@fontsource`, `font-family`, `fontFamily` and `font-\[`; `font-mono` only around payload or log text.
- **Evidence:** inventory §1.5 (Poppins + the system stack + DM Sans + JetBrains Mono + a test font coexist, about 127 `font-family` declarations: the drift this prevents); D-03.

```tsx
// ✅
<pre className="font-mono text-label">{payloadJson}</pre>
// ❌
<span style={{ fontFamily: "'JetBrains Mono'" }}>{f.amount(total)}</span>
```

### UI-TOK-009 · MUST · Numbers that are compared use tabular figures
`tabular-nums` (fixed-width digits) applies to:
- numeric, amount, date and date-time grid cells
- totals blocks
- numeric inputs
- any column of figures in a summary

Digits then line up vertically without a monospace font. The grid cells (`NumberCell`, `AmountCell`, `DateCell`, `DateTimeCell`), the totals block and `NumberInput` apply it themselves; feature code adds it only for figures it renders by hand.
- **Check:** the library cells and the totals block have `tabular-nums`; hand-rendered columns of figures in the diff have it too.
- **Evidence:** inventory §1.5 (the price-list redesign used JetBrains Mono so prices align: the intent, without the second font); UI-GRID-004, UI-GRID-015.
