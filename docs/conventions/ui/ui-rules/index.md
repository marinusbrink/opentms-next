# UI rules: index

These are binding UI rules for the OpenTMS Next front-end (Vite + React + TypeScript, Tailwind v4, shadcn/ui `base-nova` on `@base-ui/react`, AG Grid Enterprise). They exist so that two agents building two different screens produce the same result, and so that users of the old Angular app feel at home.

## How to use this folder

1. **Always** read [tokens.md](tokens.md). It is short, and every screen uses it.
2. Read the files for your task from the table below. Read nothing else; each file stands alone.
3. While building, cite rule IDs in code review notes when you make a non-obvious choice ("per UI-DLG-004").
4. A rule you cannot follow is a design question. Report it on the issue, or mark it with `DEVIATION(UI-XXX-nnn): <reason>, approved in <design doc>`, exactly like constitution deviations. An unmarked deviation is a reviewer blocker.

| Task | Load |
|---|---|
| A new list / overview screen | tokens, [page](page.md), [grid](grid.md), [actions](actions.md), [permissions](permissions.md), [feedback](feedback.md) |
| A create / edit screen for an entity | tokens, [dialog](dialog.md) (incl. the fixed height across tabs, UI-DLG-015), [form](form.md), [actions](actions.md), [feedback](feedback.md), [lookup](lookup.md), [format](format.md) |
| A small action or confirm dialog | tokens, [dialog](dialog.md), [actions](actions.md), [keyboard](keyboard.md) |
| Adding a column, a cell renderer or a filter | tokens, [grid](grid.md), [format](format.md) |
| A dropdown, autocomplete or entity picker | tokens, [lookup](lookup.md), [form](form.md) |
| A multi-step batch operation (wizard) | tokens, [wizard](wizard.md), [dialog](dialog.md), [feedback](feedback.md) |
| Showing dates, numbers, amounts, units | [format](format.md) |
| Any button, menu item or command | [actions](actions.md), [permissions](permissions.md) |
| Planboard, maps, Gantt, dashboards, portals | [bespoke](bespoke.md) first; it says which general files still apply |
| Reviewing a UI PR | all files whose topic the diff touches; the **Check** lines are your checklist |

## Rule format

Each rule has:
- **An ID** (`UI-<AREA>-nnn`), stable once published.
- **A level:** **MUST** blocks review; **SHOULD** needs a one-line reason when not followed.
- **One checkable statement.**
- **Check:** how a reviewer verifies it.
- **Evidence:** a section of [../ui-inventory.md](../ui-inventory.md) and, if a human chose between options, the decision number in [../open-decisions.md](../open-decisions.md).
- **Examples:** a correct example and, where the mistake is likely, a wrong one.

## Library components these rules refer to

Several rules name OpenTMS library components that **do not exist yet** (marked *build* in [../component-map.md](../component-map.md)). The first feature that needs one builds it in `src/components/ui/` with the API given in the rule, plus a component test. It never inlines a one-off version in a page.

| Component | Specified in | Purpose |
|---|---|---|
| `EntityDialog` | dialog.md | every dialog except confirmations |
| `ConfirmDialog` | dialog.md | confirmations |
| `FormField`, `FormSection` | form.md | every form field / field group |
| `EntityLookup` | lookup.md | searched entity picker |
| `StatusBadge`, grid cells | grid.md | status and typed cells |
| `LinesGrid` | grid.md (UI-GRID-014) | editable document lines inside an editor |
| `Alert` `warning` variant | feedback.md (UI-FB-008) | warnings in dialogs |
| `useFormat()` (`lib/format`) | format.md | all date / number / amount output |
| `useIsGranted()` (`lib/auth/permissions`) | permissions.md | all permission checks |
| `useOpenRecord()` | dialog.md | open a record dialog from anywhere |
| `Wizard`, `TaskList` | wizard.md | batch operations |

Components flagged **GAP** in the component map need a human library choice first. If your task needs one, report it; don't pick a library.
