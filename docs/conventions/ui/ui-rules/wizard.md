# Wizards (UI-WIZ)

**Load for:** multi-step batch operations, such as generating invoices, approving or archiving orders, print / e-mail, sending to FMS, close period, redistribute.

The `Wizard` component is GAP-04 in the component map: a library choice is needed before the first wizard is built. The behaviour below is fixed regardless of that choice.

---

### UI-WIZ-001 · MUST · A batch operation over selected records is one wizard in a `full` dialog
A wizard is one `EntityDialog` (`variant="action"`, `size="full"`) containing the `Wizard`. It is not a chain of dialogs, and not a page. It opens from the list's command bar. Backdrop and Esc never close a wizard that has input; it asks first (UI-DLG-008).
- **Check:** one dialog per wizard; the opening command is in `AppCommandBar`.
- **Evidence:** inventory §12.1 (22 stepper wizards, all large dialogs with `disableClose`, opened from module toolbars).

### UI-WIZ-002 · MUST · Fixed footer: Cancel left; Previous and Next / run-verb right
The footer is fixed at the bottom of the dialog, outside the step content:
- **left:** `[Cancel (outline)]`
- **right:** `[Previous (outline)] [Next (default)]`

On the last step, Next is replaced by the operation verb as primary (`t("GenerateInvoices")`, `t("Archive")`, `t("Send")`). Previous is hidden on the first step. Next / verb is disabled until the current step is valid.
- **Check:** the footer is outside the scroll area; the last-step primary is a verb (UI-ACT-004).
- **Evidence:** inventory §12.1 (`div.controls` footer in 20 of 22: Previous / Submit / Next; newest wizards add Cancel); D-32.

### UI-WIZ-003 · MUST · Steps are linear and shown as a numbered header
The step header shows numbered steps with their titles:

| Step state | Look |
|---|---|
| current | `bg-brand text-brand-foreground` number |
| completed | `Check` icon |
| future | muted |

Users can go back to completed steps and cannot skip ahead.
- **Check:** clicking a future step does nothing.
- **Evidence:** inventory §12.1 (`mat-horizontal-stepper [linear]="true"` in all 22).

### UI-WIZ-004 · MUST · Running shows per-item progress, not toasts
After the user starts the operation:
- the last step shows a `TaskList`: one row per item, each with a status (waiting, running, done, failed + reason) and an overall progress bar;
- the footer changes to a single `[Close]`, enabled when the run finishes;
- per-item failures appear in the list.

A single summary toast after closing is allowed (UI-FB-002).
- **Check:** no `toast` per item.
- **Evidence:** inventory §12.1 (`task-list` with per-item progress in 16 wizards).

### UI-WIZ-005 · SHOULD · The first step explains; selection steps embed a grid
- The first step starts with a short explanation (2–4 sentences) of what the wizard will do and to which records.
- A step that selects records embeds an `OpenTmsGrid` with selection, pre-filled with the records selected on the list. Next is disabled until at least one row is selected.
- **Check:** selection steps use `OpenTmsGrid`, not a custom list.
- **Evidence:** inventory §12.1 (Welcome step in about 19 of 22; selection steps embed grids such as `transport-files` and `orders-selectable-table`).
