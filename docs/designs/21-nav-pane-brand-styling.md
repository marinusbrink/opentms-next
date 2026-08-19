# Design: Nav pane fixed background and brand-colored selected item

**PBI:** https://github.com/marinusbrink/opentms-next/issues/21
**Status:** draft
**Date:** 2026-08-19

<!-- All nine sections below are mandatory (design §4.2). "Not applicable" is an
     acceptable answer only with a one-line reason; a missing section is not.
     Gate 1 approves this document by merging the PR — after that, implementers
     build exactly this, so vagueness here becomes iteration later. -->

## Domain impact

**Module:** Platform (frontend shell — horizontal layer)
No domain module is involved. No backend surface changes. No new entities, no events.

Changed files (frontend only):

| File | Change |
|---|---|
| `frontend/src/index.css` | Add `--brand: #0F6CBD` to `:root`; register `--color-brand: var(--brand)` in `@theme inline` |
| `frontend/src/app/shell/AppShell.tsx` | Replace hardcoded `bg-[#0f6cbd]` on `<header>` with `bg-brand` |
| `frontend/src/app/shell/AppNavPane.tsx` | Nav pane background, blue fill on active leaf, removal of accent-bar `<span>` |
| `frontend/src/app/shell/__tests__/AppNavPane.test.tsx` | Rewrite two assertions (lines 157 and 365) from `bg-primary` accent bar to `bg-brand` fill |

No cross-module events. No domain modules touched. No backend localization changes needed (no new user-facing strings).

## API contract

Not applicable — no backend surface is added or changed by this PBI.

## Migration strategy

Not applicable — there is no database schema or API contract change. The change is purely frontend CSS and component styling; rollback is a PR revert.

## UI design

### CSS token (`index.css`)

Add to `:root`:

```css
--brand: #0F6CBD;
```

Add to `@theme inline`:

```css
--color-brand: var(--brand);
```

This exposes Tailwind utility classes `bg-brand` and `text-brand`. The `.dark` block is **not** changed — the dark theme's own surface tokens take precedence in dark mode. The `--brand` token is the single source of truth for the brand blue; it must not be assigned to `--primary` (which drives all shadcn button styles).

### Top app bar (`AppShell.tsx`)

`<header>` className change: `bg-[#0f6cbd]` → `bg-brand`. Behaviorally identical; the header now consumes the `--brand` token rather than a hardcoded hex value.

### Navigation pane (`AppNavPane.tsx`)

**Pane container (`<nav>`):**
`bg-background` → `bg-[#F8F9FA] dark:bg-background`
The `border-r` separator and all other classes remain. `#F8F9FA` is light-mode only; the `dark:bg-background` variant restores the dark theme's own surface. No named CSS token is introduced for the pane background — it is used in one place only, so an arbitrary value is sufficient.

**Accent bar removed:**
The `<span aria-hidden className="... bg-primary" />` element rendered in the `isActive && !hasChildren` branch is deleted entirely. The blue row fill replaces it as the sole selected-state indicator.

**Active leaf entry — expanded mode (no children):**

| Before | After |
|---|---|
| `text-foreground font-medium` + accent-bar `<span>` | `bg-brand text-white font-medium`, no span |

`hover:bg-accent` and `hover:text-accent-foreground` are excluded from the active entry's `cn()` call so that the brand fill is not overridden on hover. Inactive entries keep their hover classes unchanged.

**Active leaf entry — collapsed mode (icon rail, 56 px):**

| Before | After |
|---|---|
| `text-foreground` | `bg-brand text-white` |

The fill applies to the rounded icon container. `hover:bg-accent hover:text-accent-foreground` are likewise excluded for the active state.

**Active parent entry (has children, expanded):**
Unchanged: `text-foreground font-medium`. The PO confirmed the parent stays subtle — bold label, no fill. Only the active leaf receives the brand fill.

**States:**

| State | Behaviour |
|---|---|
| Loading | Not applicable — shell renders after successful authentication; no async data path in these components |
| Empty | Not applicable — `app.views` is always non-empty for configured apps |
| Error | Not applicable — shell components are static; no network data |
| Permission-denied | Not applicable — auth guard fires before the shell renders |

**Optimistic updates:** None — this PBI introduces no mutation.

**Contrast (WCAG AA):** White icon and label on `#0F6CBD` yields ≈ 4.6:1, above the 4.5:1 threshold for WCAG AA normal text. Confirmed at intake; no further accessibility audit is required for this specific contrast pair.

## Test risk analysis

This change is in the horizontal platform shell. Per design §3.1, **any change touching the platform layer is risk class critical by default** — every domain app depends on the shell rendering correctly.

| Part | Risk class | Rationale |
|---|---|---|
| `AppNavPane` active-state fill + accent-bar removal | **Critical** | Platform shell; regression breaks selection visibility across all domain apps |
| `AppShell` header token swap | **Critical** | Platform shell; visual regression affects every user immediately |
| `index.css` `--brand` token addition | **Medium** | Additive-only CSS; primary risk is a scoping error that accidentally bleeds into dark mode |
| Test rewrites (`AppNavPane.test.tsx:157`, `:365`) | **High** | These assertions are the contract guard for the active state; an incorrect rewrite leaves the fill unguarded |

**Required test changes (owned by the test engineer):**

1. **Line 157** — rewrite the assertion from "accent bar `span[aria-hidden][class*="bg-primary"]` is present" to "active leaf link/button has a class containing `bg-brand`."
2. **Line 365** — rewrite the assertion from "no `span[aria-hidden][class*="bg-primary"]` for a parent-active entry" to "no `span[aria-hidden]` is present at all" (the span is removed entirely, so this applies to all entries, not only parent-active ones).
3. **New assertion** — collapsed icon rail active entry: the icon container has a class containing `bg-brand`.
4. **Dark-mode guard** — render the pane inside a `.dark` wrapper and assert the `<nav>` does not carry `bg-[#F8F9FA]` as an inline style effect (i.e., the dark theme's own surface applies instead).

## Flag & rollout plan

**No feature flag.** Constitution rule 4 requires a per-tenant flag for new user-visible behavior. This PBI adds no new capability, data, or backend surface — it corrects the visual weight of an existing shell element that is identical for all tenants. A flag would force `AppNavPane` to carry both the old and the new look until cleanup, and the only rollback path needed is a PR revert.

**Gate 1 must formally confirm this exemption.** The PO stated this at intake and requested confirmation at gate 1 rather than treating it as settled.

**Rollout:** all tenants in the same release — no staged activation, no migration of existing tenant data required.

## Cost & SLO impact

**GCP impact:** None. This is a client-side CSS and component change — no Cloud SQL, no egress, no Cloud Run min-instance change, no external API calls.

**Performance budgets touched:**

| Budget | Impact |
|---|---|
| Web frontend — bundle size | `--brand` token adds < 10 bytes to the compiled CSS; negligible; CI threshold not approached |
| Web frontend — LCP / INP | Replacing one className with another adds no paint layers or layout work |
| Interactive reads — API p95 < 300 ms | Not applicable |
| Availability 99.9% SLO | No runtime code path that can fail is changed |

**Per-tenant margin:** None — purely client-side.

## Assumptions

1. **Tailwind v4 `@theme inline` registration** — defining `--color-brand: var(--brand)` in `@theme inline` produces `bg-brand` and `text-brand` utility classes, matching the existing pattern for every other color token in `index.css`. If this does not hold for the pinned TypeScript/Tailwind combination, the fallback is the arbitrary-value form `bg-[--brand]`.

2. **Nav pane background as an arbitrary value** — `#F8F9FA` is used in one place, so `bg-[#F8F9FA]` is sufficient; no named CSS custom property is introduced for it. Only `--brand` (consumed in two places: header and nav active state) warrants a named token.

3. **Hover suppression for active entries** — the implementer omits `hover:bg-accent hover:text-accent-foreground` from the active branch of `cn()`, ensuring the brand fill takes visual priority over hover. Inactive entries are not changed.

4. **`border-r` stays** — the separator on the `<nav>` element is not changed.

5. **Dark-mode nav pane** — `dark:bg-background` is the correct override token. If a dedicated dark surface token is introduced later, this assumption must be revisited.

6. **Parent entry with `hasChildren && isActive`** — the parent keeps `text-foreground font-medium` (bold text, no fill) as confirmed by the PO (intake reply item 4: "only the active leaf gets the fill; the parent stays subtle").

7. **Collapsed icon rail active fill** — the same `bg-brand text-white` applies to the icon container in collapsed (56 px) mode, consistent with the expanded active-leaf behaviour.

8. **`Landing.tsx` and `FullScreenSpinner.tsx` are out of scope** — both hardcode `#0f6cbd` but are explicitly excluded from this PBI; they are addressed in a follow-up PBI (intake reply item 1).

9. **WCAG AA contrast confirmed** — white text on `#0F6CBD` ≈ 4.6:1, above the 4.5:1 WCAG AA threshold for normal text; confirmed at intake by the PO.

## Security quickscan

- **New ABP permissions:** None.
- **Input validation boundaries:** None — no new user-input surfaces.
- **Attack surface changes:** None — the change is CSS custom property registration and `className` updates; no new event handlers, fetch calls, or dynamic code paths are introduced.
- **Personal data:** None — no personal data is introduced, processed, or logged.
- **GDPR art. 15/17:** Not applicable.
