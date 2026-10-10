# Lookups (UI-LOOK)

**Load for:** dropdowns, autocompletes, entity pickers.

## `EntityLookup` (`components/ui/entity-lookup.tsx`)

Built on the shadcn `Combobox` (the infinite-scroll mechanism is GAP-01 in the component map).

```ts
interface EntityLookupProps<T extends { id: string }> {
  id: string;
  value: T | null;                                        // multiple: T[]
  onChange: (value: T | null) => void;
  fetchPage: (q: { term: string; skip: number; take: number }) => Promise<{ items: T[]; totalCount: number }>;
  getLabel: (item: T) => string;                          // shown when selected: the name
  renderOption?: (item: T) => ReactNode;                  // optional richer row in the list
  onCreateNew?: (term: string) => void;                   // shows "Add new …"; omit when the user may not create
  onEdit?: (item: T) => void;                             // shows the edit pencil; omit when the user may not edit
  createLabel?: string;                                   // t("AddNewRelation")
  multiple?: boolean;
  disabled?: boolean;
  "aria-invalid"?: boolean;
}
```

---

### UI-LOOK-001 · MUST · Choose the control by the kind of list

| List | Control |
|---|---|
| a fixed enum or small reference list (up to 15 items, loaded once) | `Select` |
| a static list with more than 15 items (countries, currencies, languages) | `Combobox` with client-side search |
| **master-data or transactional records** (relations, vehicles, products, price lists, contact persons, transport units, …) | `EntityLookup` with server search |
| tree data (organisation units) | `TreeSelect` (GAP-02) |

- **Check:** no `Select` over an API list endpoint; no `Select` with more than 15 options.
- **Evidence:** inventory §9.1, §9.3 (`mat-select` for enums, 268; ng-select wrappers for entities, about 75; an unsearchable country list = legacy).

### UI-LOOK-002 · MUST · `EntityLookup` searches on the server with debounce and a minimum length
`fetchPage` runs with:
- **250ms debounce**
- a **minimum of 2 characters**
- pages of **25**
- the next page loads on scroll

An empty term loads the first page. Below the minimum the list shows `t("TypeToSearch")`. With no results it shows `t("NoItemsFound")`. While loading it shows a `Spinner` row.
- **Check:** `EntityLookup` owns these values; no feature passes its own debounce.
- **Evidence:** inventory §9.2 (base wrapper: 200ms, 3 characters, 25 per page, infinite scroll, Enter3Chars / NoItemsFound items; clones without debounce = legacy); D-30.

### UI-LOOK-003 · MUST · The selected value shows the name only; options may show more
- **Closed:** the control shows `getLabel(item)` (the name).
- **Open:** options may use `renderOption`:
  - line 1: the name in `font-medium`, optionally prefixed with the search name or code
  - up to 2 lines below in `text-label text-muted-foreground` (address, city, licence plate)

- **Check:** `getLabel` returns the name; `renderOption` has at most 3 lines.
- **Evidence:** inventory §9.2 (relation options: search name - name / street / postcode city; selected = name only); D-31.

### UI-LOOK-004 · MUST · "Add new …" creates in place
When the user may create the entity, the list ends with a row `createLabel` (`Plus` icon). It opens that entity's editor (`mode="create"`, the name prefilled with the typed term) on top of the current dialog. After saving, the new record is selected. When the user may not create, there is no row.
- **Check:** lookups for creatable entities pass `onCreateNew` guarded by the create permission.
- **Evidence:** inventory §9.2 (`addTag` "AddNew<Entity>" prefilled with the typed text, in the base wrappers and vehicle-select).

### UI-LOOK-005 · SHOULD · An edit pencil opens the selected record
When a value is selected and the user may edit that entity, a ghost `icon-sm` `Pencil` button with the tooltip `t("Edit")` sits inside the control's right edge, before the clear button. It opens the entity editor on top.
- **Check:** `onEdit` is passed where the old screen had it (relation, contact person, product).
- **Evidence:** inventory §9.2 (edit pencil in the base wrapper).

### UI-LOOK-006 · MUST · Optional lookups are clearable; required ones are not
- Optional lookups and selects show a clear (`X`) button when they have a value.
- Required ones don't; the user replaces the value instead.
- **Check:** clear button presence follows `required`.
- **Evidence:** inventory §9.2 (clearable by default in ng-select; override 3×).

### UI-LOOK-007 · MUST · Multi-select shows a comma list
A multi-value lookup or select shows the selected names comma-separated, truncated with `+N` when they don't fit. It does not show chips inside the control. The list shows a checkbox per option.
- **Check:** multi controls use `MultiSelect` / `EntityLookup multiple`.
- **Evidence:** inventory §9.1 (`mat-select multiple` 47); §13 (`RoleMultiSelect` comma list).
