# Buttons and actions (UI-ACT)

**Load for:** any button, command, menu item or footer.

Commands are `AppCommandBarCommand` objects (`components/ui/app-command-bar.tsx`). Their labels are localization keys.

---

### UI-ACT-001 · MUST · A list page's command bar: New first, then selection actions, then the rest
`AppCommandBar` commands on a list page, in order:
1. **New** (`isPrimary`, `Plus` icon, label `t("New<Entity>")`), if the user may create
2. selection actions (`requiresSelection`, with `selectionLabelKeys` for bulk)
3. module actions and wizards, which overflow into "More"

There is exactly one `isPrimary` command.
- **Check:** at most one `isPrimary`; New is first when present.
- **Evidence:** inventory §2.2 (all 5 module toolbars start with New, then wizards); §13 (`AppCommandBar` order); D-08.

### UI-ACT-002 · MUST · An editor's command bar: Save, Close, then entity and document actions
Editor `commands`, in order:
1. **Save** (`isPrimary`, `Save` icon): saves and closes
2. **Save only** (`SaveAll` icon): stays open. Only for `size="full"` editors.
3. **Close** (`X` icon): dismisses, with the dirty guard
4. entity state actions: Approve, Archive / Unarchive, Cancel order …
5. document actions: Print / Download, E-mail, which go to overflow first
6. **Delete** (destructive), last

The dismiss command is always **Close**, never "Cancel". A business cancellation names its object: `t("CancelOrder")`.
- **Check:** order as listed; no command with label key `Cancel` in an editor.
- **Evidence:** inventory §6.2 (ribbon order: Save, Cancel / Close, Archive / Approve, documents), §4.7 (Save = save and close; TO Save & Close + Save only), §6.5 (the "Cancel" clash); D-13, D-20.

```ts
// ✅
const commands: AppCommandBarCommand[] = [
  { id: "save", labelKey: "Save", icon: Save, isPrimary: true, disabled: save.isPending, onClick: submit },
  { id: "close", labelKey: "Close", icon: X, onClick: requestClose },
  ...(canArchive ? [{ id: "archive", labelKey: "Archive", icon: Archive, onClick: archive }] : []),
  { id: "print", labelKey: "PrintOrDownload", icon: Printer, onClick: print },
];
// ❌
[{ id: "cancel", labelKey: "Cancel", … }, { id: "save", labelKey: "Ok", … }]
```

### UI-ACT-003 · MUST · Dialog footer: primary rightmost, Cancel directly to its left
Action-dialog footers are right-aligned (`flex justify-end gap-2`) and contain:
- `[Cancel (variant="outline")]`, then `[Primary (variant="default", or "destructive" for destructive actions)]`
- optional extra secondary buttons to the **left** of Cancel

All footer buttons are default size.
- **Check:** the last child of the footer is the primary; the one before it is Cancel.
- **Evidence:** inventory §6.3 (Cancel left / primary right in about 53 dialogs incl. the shared footer, vs 13 primary-first); §13; D-14.

```tsx
// ✅
<div className="flex justify-end gap-2">
  <Button variant="outline" onClick={onCancel}>{t("Cancel")}</Button>
  <Button onClick={merge} disabled={busy}>{busy ? t("Merging") : t("Merge")}</Button>
</div>
// ❌
<div className="flex justify-end gap-2">
  <Button onClick={merge}>{t("Ok")}</Button>
  <Button variant="outline">{t("Cancel")}</Button>
</div>
```

### UI-ACT-004 · MUST · Primary buttons are labelled with the verb
The primary button says what happens: Save, Merge, Publish, Send, Delete, Generate. Never "Ok", "Submit", "Finish" or "Yes". This includes the last step of a wizard (UI-WIZ-002).
- **Check:** no `t("Ok")`, `t("Submit")` or `t("Yes")` on a primary.
- **Evidence:** inventory §6.5 (`submitButtonName` already overrides "Ok" with Complete / Merge / Migrate where it was used deliberately); D-21.

### UI-ACT-005 · MUST · Destructive actions look destructive
- **Delete, remove, cancel-order and other irreversible actions:**
  - command bar: `variant: "destructive"`
  - menus: `destructive: true` on the item
  - footers: `Button variant="destructive"`
  - always behind a `ConfirmDialog` (UI-DLG-011)
- **Archive** is reversible: not destructive styling, but still confirmed.
- **No double confirmation:** an action dialog whose body already states the consequence (an `Alert` with UI-FB-008), and whose primary is the destructive verb, **is** the confirmation. Don't add a `ConfirmDialog` after it.
- **Check:** every delete has the destructive variant and exactly one confirmation step.
- **Evidence:** inventory §6.4 (3 of 34 delete buttons styled = gap), §7.2 (confirm on delete / archive / copy / reset); D-21.

### UI-ACT-006 · MUST · Icons: lucide, from the action→icon table, never alone without a tooltip
Use these icons for these actions, everywhere:

| Action | Icon |
|---|---|
| New / add | `Plus` |
| Edit | `Pencil` |
| Delete | `Trash2` |
| Save | `Save` |
| Save only | `SaveAll` |
| Close | `X` |
| Refresh | `RefreshCw` |
| Export | `Download` |
| Print / download document | `Printer` |
| E-mail | `Mail` |
| Archive | `Archive` |
| Unarchive | `ArchiveRestore` |
| Approve | `CheckCheck` |
| Merge | `Merge` |
| Copy | `Copy` |
| Flag | `Flag` |
| Upload | `Upload` |
| Search | `Search` |
| Filter | `Filter` |
| Columns | `Columns3` |
| Reset layout | `RotateCcw` |
| Settings | `Settings` |
| Attachments | `Paperclip` |
| Remarks | `MessageSquareText` |
| Record identification | `Fingerprint` |

An icon-only button has a `Tooltip` with its label and an `aria-label`.
- **Check:** icon imports for these actions match the table; every `size="icon*"` button has a tooltip and an `aria-label`.
- **Evidence:** inventory §1.9 (Material action→icon mapping), §6.4 (144 icon-only buttons); D-06.

### UI-ACT-007 · SHOULD · Button text and icon: commands have both; footers have text only
| Surface | Style |
|---|---|
| command bars, menus | icon + text |
| dialog footers | text only |
| inline row and field actions (edit pencil in a lookup, remove a line) | icon only, `variant="ghost"` `size="icon-sm"` |

- **Check:** footer buttons have no icon; command-bar commands have `icon`.

### UI-ACT-008 · MUST · Document actions in an editor save first
If an editor command produces output from the record (print, download, e-mail, export, send to a portal) and the form is dirty:
1. it runs Save first, without closing;
2. it runs the action only when the save succeeded.

If the save fails, the action does not run and the error shows as UI-FB-004. There is no "please save first" message.
- **Check:** document commands in editors call `saveIfDirty()` before the action.
- **Evidence:** inventory §4.7 (invoice `saveBeforeEmail`: saves before e-mail and print); verification §2.
- **Evidence:** inventory §6.4 (icon + text in toolbars and ribbon, text in footers, icon-only inline: 144 / 327 / 144).
