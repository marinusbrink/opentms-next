# Keyboard (UI-KEY)

**Load for:** dialogs, forms, lookups.

---

### UI-KEY-001 · MUST · Enter submits small forms, never large editors or destructive confirms
| Context | Enter does |
|---|---|
| **action dialogs and `sm` / `md` editors** | submits through the form's `onSubmit` (the primary action). Not in a `Textarea`, and not while a lookup list is open (there Enter selects the option). |
| **`lg` / `full` editors** | does **not** submit; they contain grids, line editors and multi-line content |
| **destructive `ConfirmDialog`**, and **any dialog whose primary is destructive** (e.g. Merge) | never confirms or submits; focus starts on Cancel (UI-KEY-003) |

There are no document-level key listeners.
- **Check:** small dialogs wrap their body in `<form onSubmit>` with a `type="submit"` primary; no `document.addEventListener("keydown")` in features.
- **Evidence:** inventory §10 (Enter confirms 11 small dialogs via document listeners, including a destructive reset = risky; editors don't save on Enter); D-26.

### UI-KEY-002 · MUST · Esc closes the innermost layer only
Esc closes, in order of precedence:
1. an open list or menu
2. a nested dialog
3. the dialog

Editors apply the dirty guard (UI-DLG-008).
- **Check:** this is provided by Base UI layering; don't add custom Esc handlers.
- **Evidence:** inventory §10 (inconsistent Esc: 31 `disableClose`, 4 custom handlers).

### UI-KEY-003 · MUST · Initial focus
| Dialog | Focus on open |
|---|---|
| editor (create) | the first editable field |
| editor (edit) | the first editable field of the first tab |
| action dialog | the first input, or the primary button if there are no inputs |
| **destructive** `ConfirmDialog` | **Cancel** |
| non-destructive `ConfirmDialog` | the confirm button |

- **Check:** `EntityDialog` / `ConfirmDialog` set `initialFocus`; component tests assert the focused element.
- **Evidence:** inventory §10 (`cdkFocusInitial` on the primary in 10 dialogs; no first-field focus, often landing on the X = gap); D-27.

### UI-KEY-004 · MUST · Tab commits the highlighted lookup option
In `EntityLookup` and `Combobox`, pressing Tab while the list is open selects the highlighted option and moves focus to the next field.
- **Check:** a component test covers Tab-to-commit.
- **Evidence:** inventory §10 (addresses, SSP address and waypoints commit on Tab: deliberate fast entry).

### UI-KEY-005 · MUST · No custom global shortcuts
Features don't add application shortcuts (Ctrl+S, F-keys, single letters) unless the design specifies them. Grids keep the AG Grid keyboard defaults, plus Enter = open (UI-GRID-007).
- **Check:** no `keydown` listeners on `window` / `document` in features.
- **Evidence:** inventory §10 (no hotkeys in the app).
