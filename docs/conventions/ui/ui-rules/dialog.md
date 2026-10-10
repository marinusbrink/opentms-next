# Dialogs (UI-DLG)

**Load for:** create / edit screens, action dialogs, confirmations.

## The two library components

**`EntityDialog`** (`components/ui/entity-dialog.tsx`) is built on the shadcn `Dialog`. It covers every dialog except confirmations.

```ts
interface EntityDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  variant: "editor" | "action";
  size: "sm" | "md" | "lg" | "full";        // tiers, UI-DLG-003
  title: string;                             // already translated
  badges?: ReactNode;                        // editor: StatusBadge(s) after the title, UI-DLG-012
  commands?: AppCommandBarCommand[];         // editor only: rendered under the header
  footer?: ReactNode;                        // action only: rendered right-aligned
  isDirty?: boolean;                         // editor: guards closing, UI-DLG-008
  isBusy?: boolean;                          // blocks closing while a mutation runs
  children: ReactNode;
}
```

**`ConfirmDialog`** (`components/ui/confirm-dialog.tsx`) is built on the shadcn `AlertDialog`, size `max-w-dialog-confirm`.

```ts
interface ConfirmDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;                // e.g. t("DeleteTransportOrder")
  description: string;          // the question, with the object named
  confirmLabel: string;         // the verb: t("Delete"), t("Archive")
  destructive?: boolean;
  onConfirm: () => Promise<void> | void;
  children?: ReactNode;         // optional extra input (reason, checkbox)
}
```

---

### UI-DLG-001 · MUST · Dialogs only through `EntityDialog` or `ConfirmDialog`
Feature code never imports `@base-ui/react/dialog`, `Dialog` or `AlertDialog` directly.
- **Check:** grep feature folders (`src/apps`, `src/domains`) for `react/dialog` / `ui/dialog` / `ui/alert-dialog` imports.
- **Evidence:** inventory §5.4 (the shared `base-dialog-template` / `base-header` / footer / confirm cover the G2 dialogs; the 38–50 bespoke copies are drift); §13 (6 copy-pasted raw dialogs).

### UI-DLG-002 · MUST · `editor` for records, `action` for operations

| Variant | Use when | Examples |
|---|---|---|
| `editor` | creating, editing or viewing **one record** | vehicle, relation, invoice, transport order |
| `action` | **one operation** with parameters | merge, set flags, export, upload, publish, complete stop |

- **Check:** a dialog that saves a record via create / update uses `editor`.
- **Evidence:** inventory §5.4 (`base-dialog-template` 41 editors vs `base-header` + `base-ok-cancel-footer` action dialogs).

### UI-DLG-003 · MUST · Size by content tier, never by pixels

| Size | Content | Old examples |
|---|---|---|
| `sm` (560px) | up to about 8 fields in one column, or a short action | country, category, VAT, merge, set flags |
| `md` (880px) | two columns, or tabs with simple content | contact person, product, e-mail template |
| `lg` (1360px) | a document with a header block + a lines grid | invoice, purchase order, vehicle |
| `full` (98vw × 96vh) | the large records + wizards | transport order, transport file, relation, price list |

- **Check:** `size` is set; no `className` width override.
- **Evidence:** inventory §5.3 (tiers implicit in about 40 widths); D-16.

### UI-DLG-004 · MUST · An editor has a brand header bar
- **The bar:** `h-dialog-header`, `bg-brand`, `text-brand-foreground`, with the title `text-title font-semibold` on the left and the close X (`X` icon, `Button variant="ghost" size="icon-sm"`, `aria-label={t("Close")}`) on the right.
- **Title for a new record:** `t("New<Entity>")`.
- **Title for an existing record:** `t("<Entity>")` + the record's identifier (number or name), e.g. "Transport order 2026-01234".

`EntityDialog` renders this; features only pass `title`.
- **Check:** editor titles follow the pattern; no custom header markup.
- **Evidence:** inventory §5.4 (38px brand bar, 15px / 600 white title, X right, in 41 + 50 dialogs); D-05.

```tsx
// ✅
<EntityDialog
  variant="editor"
  size="sm"
  title={mode === "create" ? t("NewCountry") : `${t("Country")} ${country.code}`}
  commands={commands}
  isDirty={isDirty}
  isBusy={save.isPending}
  open={open}
  onOpenChange={onOpenChange}
>
  <CountryForm … />
</EntityDialog>
```

### UI-DLG-005 · MUST · An editor's actions are a command bar under the header; it has no footer
- The editor's `commands` render as an `AppCommandBar` directly under the header bar, in the order given by UI-ACT-002.
- Editors have no footer buttons.
- Action dialogs are the opposite: a plain header (title + X, no brand bar), a body, and a footer `[Cancel] [Primary]` (UI-ACT-003).
- **Check:** `variant="editor"` has `commands` and no `footer`; `variant="action"` has `footer` and no `commands`.
- **Evidence:** inventory §6.1–6.2 (ribbon at the top in about 51 editors, footer in about 55 action dialogs); D-13.

### UI-DLG-006 · MUST · The header and command bar stay fixed; the body scrolls
- The body scrolls (`min-h-0 flex-1 overflow-auto p-4`).
- In `full` dialogs, a lines grid fills the remaining height instead of scrolling the body.
- **Check:** no `overflow-auto` on the dialog popup itself.
- **Evidence:** inventory §5.4 (header + ribbon above scrolling content); §B3 invoice (lines grid + pinned totals).

### UI-DLG-007 · MUST · Records open through `useOpenRecord()` and are deep-linkable
- `const openRecord = useOpenRecord(); openRecord("TransportOrder", id)` sets the search param `?open=TransportOrder:<id>`.
- A record host mounted in the app shell renders the matching editor over **whatever page is current**: list, planboard, another dialog.
- Closing removes the param.
- **Check:** grids, links and other dialogs open records only through `useOpenRecord`; reloading a URL with `?open=` reopens the dialog.
- **Evidence:** inventory §2.1, §5.1 (TO / TF open from anywhere through a global host); D-15.

### UI-DLG-008 · MUST · Editors don't lose edits
In an editor:
- clicking the backdrop **never** closes it;
- Esc, the X and the **Close** command close immediately when the form is clean;
- on a dirty form they ask with `ConfirmDialog`: title `t("UnsavedChanges")`, description `t("DiscardChangesQuestion")`, confirm `t("Discard")`, destructive.

Action dialogs close on Esc / backdrop unless `isBusy`.
- **Check:** editors pass `isDirty`; the backdrop click handler is a no-op for `editor`.
- **Evidence:** inventory §4.6 (no dirty tracking; 29 of 40 master-data dialogs lose edits on backdrop click = gap); D-19.

### UI-DLG-009 · MUST · Create and edit use the same editor
One editor component per entity takes `mode: "create" | "edit"` (and optional `initialValues`, e.g. from a lookup's "Add new…"). There are no separate create dialogs.
- **Check:** one `<Entity>Dialog` per entity.
- **Evidence:** inventory §5.1 (`ModalDialogService.dialogs['new'+entity]` opens the same dialog).

### UI-DLG-010 · MUST · A busy dialog cannot be closed or re-submitted
While a mutation is pending:
- the dialog ignores close requests;
- every command or footer button is disabled;
- the primary label switches to the progress form (`t("Saving")`, `t("Deleting")`).
- **Check:** `isBusy` is wired to the mutation's `isPending`.
- **Evidence:** inventory §6.6 (the ribbon Save not disabled in flight = double-submit gap; 48 buttons use a saving flag); §13.

### UI-DLG-011 · MUST · Confirmations name the object and the verb
`ConfirmDialog`:
- **title:** the action and the object type: "Delete transport order"
- **description:** the question with the identifier: "Delete transport order 2026-01234? This cannot be undone."
- **confirm label:** the verb ("Delete", "Archive")
- **styling:** `destructive` for irreversible actions

Never "Ok" or "Yes".
- **Check:** `confirmLabel` is not `t("Ok")` / `t("Yes")`; destructive actions set `destructive`.
- **Evidence:** inventory §7.2 (shared confirm, 46 uses, "Confirm" / question / Ok), §6.4 (delete not styled = gap); D-21.

```tsx
// ✅
<ConfirmDialog
  open={!!toDelete}
  onOpenChange={() => setToDelete(null)}
  title={t("DeleteTransportOrder")}
  description={t("DeleteTransportOrderQuestion").replace("{0}", toDelete.number)}
  confirmLabel={t("Delete")}
  destructive
  onConfirm={() => remove.mutateAsync(toDelete.id)}
/>
// ❌
<ConfirmDialog title={t("Confirm")} description={t("AreYouSure")} confirmLabel={t("Ok")} … />
```

### UI-DLG-012 · SHOULD · An editor shows the record's state in the header
An editor for a record with a workflow state (transport order, transport file, invoice, purchase order) shows the state as a `StatusBadge` in the header bar, directly after the title. Secondary states (sent, exported) follow as extra badges. `EntityDialog` takes these in a `badges` prop.
- **Check:** stateful entities pass `badges`.
- **Evidence:** inventory §5.4 (state chips in the transport-order ribbon; invoice send / export status actions); verification §2.

### UI-DLG-013 · MUST · Titles: an action dialog names its action; that verb is the primary
- **Action dialog title:** the operation and the object ("Merge relations", "Publish transport file"). The primary button uses the same verb ("Merge", "Publish").
- **Editor title:** see UI-DLG-004.
- **Check:** the title verb equals the primary label verb.
- **Evidence:** inventory §5.4 (`base-header [title]` + `submitButtonName` pairs, e.g. MergeRelations / Merge); product-owner copy convention (titles name the subject); verification §3.

### UI-DLG-014 · MUST · An outcome that removes the parent record closes the parent too
- **Rule:** when an action dialog opened from an editor deletes, merges or replaces that editor's record (Merge, Delete, Convert), success closes both dialogs. The toast names the outcome, and the list refreshes.
- **Never:** the editor is never left open on a record that no longer exists.
- **Check:** the action dialog's `onSuccess` calls the parent editor's close.
- **Evidence:** inventory §5.4 (merge-relations / merge-vehicles opened from the editors); verification §3.
