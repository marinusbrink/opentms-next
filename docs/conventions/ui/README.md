# OpenTMS Next: UI conventions package

These are the UI conventions for the OpenTMS Next front-end (Vite + React + TypeScript, Tailwind v4, shadcn/ui `base-nova`, AG Grid Enterprise). They were derived from the existing OpenTMS Angular app.

- **Version:** 2026-10-10, from `dev-department` commit `120bb72` (branch `docs/ui-conventions`).
- **The canonical copy** lives in `dev-department/conventions/ui/`. Change the rules there, not in this copy.

## What's inside

| File | For | Purpose |
|---|---|---|
| `ui-rules/index.md` | **the front-end agent** | entry point: which rule file to load for which task |
| `ui-rules/*.md` | the agent and reviewers | 108 rules (`UI-<AREA>-nnn`), each with a check, evidence and examples |
| `ui-style-guide.md` | people | principles, reasoning, annotated layouts |
| `tokens.md` | everyone | old Angular values → new token names |
| `opentms-theme.css` | developers | proposed theme tokens (Tailwind v4 `@theme`) |
| `component-map.md` | developers / architect | Angular widget → shadcn equivalent; **12 library gaps still to decide** |
| `open-decisions.md` | product owner | the decisions behind the rules (D-24 timezone still open) |
| `proposals.md` | product owner | deliberate changes from the old app, and ideas that are not rules |
| `ui-inventory.md` | reference | evidence from the Angular app that the rules cite |
| `ui-verification.md` | reference | three old screens built using only the rules |

## How to install in `opentms-next`

1. **Copy the folder.** Copy this folder to `opentms-next/docs/conventions/ui/`, keeping the structure. The relative links between files depend on it.
2. **Add the theme.** Copy `opentms-theme.css` to `frontend/src/styles/opentms-theme.css` and import it in `frontend/src/index.css`, after `@import "shadcn/tailwind.css";`:
   ```css
   @import "./styles/opentms-theme.css";
   ```
   Then remove the `--brand` definition and the `--color-brand` mapping from `index.css`, because the theme file now owns them.
3. **Point the agents at the rules.** Add this to `opentms-next/CLAUDE.md`, in the front-end section:
   ```md
   - UI work follows `docs/conventions/ui/ui-rules/`. Before building or reviewing any screen, read `docs/conventions/ui/ui-rules/index.md` and the rule files it lists for the task. Deviations are marked `DEVIATION(UI-XXX-nnn): <reason>, approved in <design doc>`.
   ```
   If the dev-department agents run on this repo, also add the same "read `ui-rules/index.md`" step to the read-first lists in `agents/implementer-frontend.md` and `agents/reviewer.md`.

## Before the first screens are built

- **Build the library components** the rules refer to: `EntityDialog`, `ConfirmDialog`, `FormField`, `FormSection`, `EntityLookup`, `LinesGrid`, `StatusBadge`, grid cells, `useFormat()`, `useOpenRecord()`, `useIsGranted()`. The rules specify their APIs (see the table in `ui-rules/index.md`). Each one is built the first time a feature needs it, into `src/components/ui/`.
- **Decide the library gaps** in `component-map.md` §4. The date picker (GAP-03) and the entity lookup list (GAP-01) are needed by the first create / edit screen.
- **Apply the changes to existing components** listed in `component-map.md` §5:
  - the grid theme via the AG Theming API
  - context menu and double-click on `OpenTmsGrid`
  - Sonner toasts instead of the custom `Toast`
  - `ConfirmDialog` instead of the inline delete popover
- **Check density with users:** check the 32px controls and 36px grid rows with 2–3 planners (`proposals.md` A17).
