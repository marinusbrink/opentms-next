# Page anatomy (UI-PAGE)

**Load for:** new list / overview screens, settings pages.

The application shell (header, app launcher, nav pane) is out of scope and already built. These rules cover what goes inside the content area.

---

### UI-PAGE-001 · MUST · A list page is a command bar on top of a full-height grid
A list page renders, in order:
1. `AppCommandBar`
2. one grid that fills the remaining height (`min-h-0 flex-1`)
3. the page's dialogs

The page itself never scrolls; the grid scrolls.
- **Check:** the root element is `flex h-full flex-col`; the grid container has `min-h-0 flex-1`; no `overflow-auto` on the page root.
- **Evidence:** inventory §2.2–2.3 (5 module shells, about 46 lists: toolbar + full-bleed grid), §13 (`UsersView`).

```tsx
// ✅
export function VehiclesView() {
  return (
    <div className="flex h-full flex-col">
      <AppCommandBar commands={commands} selectionCount={selectionCount} />
      <div className="min-h-0 flex-1 px-6 pb-6">
        <Suspense fallback={<Skeleton className="h-full w-full" />}>
          <VehiclesGrid onSelectionChange={setSelection} />
        </Suspense>
      </div>
      <VehicleDialog … />
    </div>
  );
}
```

### UI-PAGE-002 · MUST · No page title or breadcrumb on list pages
The nav pane's active item is the title. Don't add an `<h1>`, a heading bar or a breadcrumb above the command bar.
- **Check:** no heading element and no `Breadcrumb` before `AppCommandBar` in a view.
- **Evidence:** inventory §2.1 (0 breadcrumbs, no titles on business screens; only legacy G1 admin has them); D-07.

```tsx
// ❌
<div className="flex h-full flex-col">
  <h1 className="text-xl font-semibold">{t("Vehicles")}</h1>
  <AppCommandBar … />
```

### UI-PAGE-003 · MUST · Opening a record never navigates away from the list
Create, edit and view open as a dialog over the current page ([dialog.md](dialog.md), UI-DLG-001). The list keeps its scroll position, selection and filters. There are no `/entity/:id` page routes for records.
- **Check:** no route with an `$id` / `:id` param that renders an edit form; row open calls `useOpenRecord()` or sets dialog state.
- **Evidence:** inventory §5.1–5.2 (edit is never a route in 66 editors; TO / TF open from anywhere); D-15.

### UI-PAGE-004 · SHOULD · Saved list presets are nav entries with a query parameter
Predefined subsets of the same list are child views in `apps.config.ts` that route to the same view with a `?filter=` search param. Examples: "Today", "Ready for invoicing", "Pending acceptance". They are not separate pages and not separate grids.
- **Check:** one view component per entity list; presets differ only in the `filter` search param passed to `fetchRows`.
- **Evidence:** inventory §2.2 (transport sidenav presets `transportorder?filter=today|readyfor|…`).

### UI-PAGE-005 · MUST · Every page has all four states, rendered the standard way

| State | How it renders |
|---|---|
| loading | `Skeleton` in the grid slot (the Suspense fallback); the grid's own block loading |
| empty | the grid's localized no-rows overlay |
| error | the grid block error + retry; see [feedback.md](feedback.md) for errors outside the grid |
| permission denied | the shared `PermissionDenied` panel |

The permission-denied panel replaces the whole content area: `flex h-64 items-center justify-center rounded-lg border bg-background p-8 text-sm text-muted-foreground`, with the text `t("PermissionDenied")`.
- **Check:** the view returns `<PermissionDenied />` when the view permission is missing; no custom empty or error markup next to a grid.
- **Evidence:** coding-conventions (four states); inventory §13 (admin views); §3.7.

### UI-PAGE-006 · MUST · Settings (singleton) screens are full pages with one Save
Tenant, host or user settings that are one record are full pages: `AppCommandBar` with a single primary **Save** command, then a scrollable body of `FormSection`s (optionally grouped in `Tabs`). There is no per-section save button.
- **Check:** settings views have exactly one save command; the body is `min-h-0 flex-1 overflow-auto`.
- **Evidence:** inventory §5.1 (about 6 settings pages are the only full-page editors; `tms-settings` "SaveAll" at the top), §4.7.
