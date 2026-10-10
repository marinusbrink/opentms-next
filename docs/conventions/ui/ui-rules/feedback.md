# Feedback (UI-FB)

**Load for:** anything that saves, deletes, loads or can fail.

**Toasts:** `toast` from the shadcn `Sonner` (`components/ui/sonner.tsx`). The `<Toaster position="bottom-right" />` is mounted once in the shell.

**API error text:** `getApiErrorMessage(error, t)` from `lib/api/errors.ts`. It reads the ABP `error.message`, then `title`, then `detail`, and falls back to `t("UnexpectedError")`.

---

### UI-FB-001 · MUST · Toasts only through Sonner `toast`
The custom `Toast` component, `alert()` and hand-made fixed-position messages are not used.
- **Check:** no import of `components/ui/toast`; no `alert(`; no `fixed bottom-` message markup in features.
- **Evidence:** inventory §7.1 (one toast API, `notify.*`, 749 uses, is the standard; the others are dead); D-33.

### UI-FB-002 · MUST · A successful save closes, confirms and refreshes
After a successful save in an editor or action dialog:
1. Close the dialog (unless "Save only").
2. Show `toast.success` with a short, localized message that names the subject: `t("TransportOrderSaved")`, `t("VehicleDeleted")`, `t("InvoicesGenerated")`.
3. Refresh the affected list (query invalidation or the grid's refresh key).

- **Check:** the mutation's `onSuccess` does all three; the message key is entity-specific; no generic `t("Success")`.
- **Evidence:** inventory §7.1, §7.5 (the standard flow: close → success toast → refresh, consistent in master data); copy convention (titles name the subject).

```ts
// ✅
const save = useSaveVehicle({
  onSuccess: () => {
    onOpenChange(false);
    toast.success(t("VehicleSaved"));
    queryClient.invalidateQueries({ queryKey: ["masterData", "vehicles"] });
  },
});
```

### UI-FB-003 · MUST · No toast for things the user can already see
There are no toasts for:
- opening a dialog
- selection changes
- filters applied
- a successful refresh
- a successful "Save only" (the editor shows the saved state)

Toasts are for outcomes of actions that close or happen out of view.
- **Check:** each `toast.*` follows a mutation or background job.
- **Evidence:** inventory §7.1 (`success` 193 vs `info` 46; info toasts were mostly noise such as "Copied").

### UI-FB-004 · MUST · Errors show where the user is, exactly once

| Where the failure happens | How it shows |
|---|---|
| a mutation started from a dialog | `Alert variant="destructive"` at the top of the dialog body with `getApiErrorMessage(…)`; the dialog stays open and keeps its input |
| a list-level, menu or background action | `toast.error(getApiErrorMessage(…))` |
| field-specific server validation | under the field (UI-FORM-006) |
| expired session | the auth flow (redirect to login); this is the only global handler |

Never show the same failure twice. There is no global error modal and no interceptor toast.
- **Check:** no `toast.error` inside a dialog's mutation handler; no global `onError` that shows UI.
- **Evidence:** inventory §7.4 (an interceptor modal + 514 per-call toasts = double display, unintended), §13 (inline dialog errors in the new app); D-22.

```tsx
// ✅ inside an EntityDialog body
{save.error && (
  <Alert variant="destructive" className="mb-3">
    <AlertDescription>{getApiErrorMessage(save.error, t)}</AlertDescription>
  </Alert>
)}
// ❌
onError: (e) => { toast.error(e.message); setError(e.message); }
```

### UI-FB-005 · MUST · No silent failures
Every mutation and every user-triggered fetch outside a grid has a visible failure path (UI-FB-004). A caught error must be shown or re-thrown.
- **Check:** no empty `catch {}`; no `onError: () => {}`; a test exercises the failure path.
- **Evidence:** coding-conventions ("Every mutation has a visible failure path").

### UI-FB-006 · MUST · Busy indicators are local to what is busy

| What is busy | Indicator |
|---|---|
| button action | label switches to the progress verb (`t("Saving")`) + disabled (UI-DLG-010) |
| dialog loading its record | `Skeleton` blocks in the form layout |
| a region recalculating (totals, a preview) | a `Spinner` centred over that region, with the region dimmed (`opacity-50`) |
| grid | its own block loading |

There are no full-screen blocking overlays except during sign-in.
- **Check:** no page-level overlay; no spinner in a disabled button without a text change.
- **Evidence:** inventory §7.3 (`mat-spinner` overlays per dialog or section, 209; grid overlays; `setBusy` region blocking).

### UI-FB-007 · MUST · User-facing text: localized, no em dashes, subjects named
- Every visible string comes from `t()`.
- Message and title keys name their subject ("Transport order saved", not "Saved successfully").
- Texts never contain an em dash (—).
- Placeholders in keys are `{0}`, `{1}`.
- **Check:** grep the diff and new localization values for `—`; no string literals in JSX text.
- **Evidence:** inventory §7.1 (about 119 English literals = legacy), §6.5; product-owner copy convention (no em dashes; titles name the subject).

### UI-FB-008 · MUST · Consequences and warnings inside a dialog use `Alert`
A dialog that must warn before an action ("This cannot be undone", "3 orders are already invoiced and will be skipped") shows the text in an `Alert` directly above the footer:

| Warning | Alert variant | Icon |
|---|---|---|
| irreversible | `destructive` | `TriangleAlert` |
| informational | `warning` (library variant on `--tone-warning-*`) | `TriangleAlert` |

Never use plain red text or a toast.
- **Check:** no `text-destructive` paragraphs or `text-red`-style spans for warnings in dialog bodies.
- **Evidence:** inventory verification of `merge-relations-dialog` ("CannotBeUndone" as a red span); §7.2; verification §3.
