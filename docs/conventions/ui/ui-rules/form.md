# Forms (UI-FORM)

**Load for:** create / edit dialogs, settings pages, any input.

## The library components

**`FormField`** (`components/ui/form-field.tsx`) is built on the shadcn `Field`.

```ts
interface FormFieldProps {
  label: string;          // translated noun, no colon
  htmlFor: string;        // id of the control
  required?: boolean;     // renders " *" after the label and sets aria-required on the control
  error?: string;         // translated message; renders under the control, sets aria-invalid
  hint?: string;          // optional muted help text under the control
  className?: string;     // only grid placement: col-span-*
  children: ReactNode;    // exactly one control
}
```

**`FormSection`** (`components/ui/form-section.tsx`).

```ts
interface FormSectionProps { title: string; children: ReactNode; className?: string }
// renders: <section class="rounded-lg border border-border-section bg-surface-section p-4">
//            <h3 class="mb-3 text-label font-semibold text-brand">{title}</h3> …
```

---

### UI-FORM-001 · MUST · Every input sits in a `FormField` with its label above
| Part | Classes |
|---|---|
| label | `text-label font-medium` |
| gap between label and control | `gap-1` |
| control height | `h-8` |

The label is the field's noun ("Licence plate"), never a sentence and never with a colon.
- **Check:** every `Input`, `Select`, `EntityLookup`, `Textarea` or `DatePicker` in a form is the child of a `FormField`; no bare `<Label>` + control pairs.
- **Evidence:** inventory §4.1 (1,940 labels on 979 fields; one label per field); §13 (labels above in the new app); D-17.

```tsx
// ✅
<FormField label={t("LicencePlate")} htmlFor="veh-plate" required error={errors.licencePlate}>
  <Input id="veh-plate" value={v.licencePlate} onChange={…} onBlur={…} />
</FormField>
// ❌
<div className="flex flex-col"><label>{t("LicencePlate")}:</label><Input placeholder={t("LicencePlate")} /></div>
```

### UI-FORM-002 · MUST · Required fields have an asterisk after the label
`FormField required` renders ` *` after the label text, in the label colour, and sets `aria-required`. There is no other required styling and no "(required)" text.
- **Check:** required fields pass `required`; no literal `*` in label strings or keys.
- **Evidence:** inventory §4.2 (Material auto-asterisk 345 uses; G1 literal ` *` 21; new app ` *`).

### UI-FORM-003 · MUST · No placeholder text, except an input-format example
Because labels are above the field, controls have no placeholder that repeats the label or says "Select…" / "Enter…". The only allowed placeholder is a format example (`DD-MM-YYYY`), and only when the control doesn't show the format itself.
- **Check:** grep `placeholder=` in forms; each one is a format example.
- **Evidence:** inventory §4.1, §9.2 (the placeholder doubled as the label; never "Select…"); D-17.

### UI-FORM-004 · MUST · Field layout is a CSS grid; widths come from columns
Forms use a grid, set by the dialog size:

| Dialog size | Grid |
|---|---|
| `sm` | `grid grid-cols-1 gap-3` |
| `md` / `lg` | `grid grid-cols-2 gap-x-4 gap-y-3` |
| `full` | `grid grid-cols-4 gap-x-4 gap-y-3` |

Short fields (postcode, code, number) take one column; long text spans with `col-span-*`. Never set a fixed width on a field.
- **Check:** no `w-[…]` / `style={{width}}` on a field or control.
- **Evidence:** inventory §4.3 (G2 flex rows at 100% width with fixed 163 / 335px exceptions copy-pasted in 28 files; G3 CSS grid with named areas = target).

### UI-FORM-005 · SHOULD · Group fields in `FormSection`s when a form has more than one topic
A form with more than one topic (e.g. invoice: Invoice, Customer, Financial, Delivery) wraps each topic in a `FormSection`. Sections sit in a grid (`grid grid-cols-2 gap-4` or a named-area grid). A form with one topic has no section.
- **Check:** no `fieldset`, `Card` or custom bordered box around field groups.
- **Evidence:** inventory §4.4 (`.entity__section` cards: `#fafafb`, 10px radius, brand caption, in the 6 modernised dialogs).

### UI-FORM-006 · MUST · Validate on blur and on submit; show the message under the field
- **When a field shows its error:** after it loses focus (touched), and for all fields on submit.
- **How:** `FormField error` renders the message (`text-label text-destructive`) under the control and sets `aria-invalid` (red outline via the shadcn control).
- **Save stays enabled.** Submitting an invalid form shows all errors, switches to the first tab with an error and focuses the first invalid field.
- **Server validation errors** for a field go to that field's `error`. Other server errors follow UI-FB-004.
- **Check:** no `disabled={!isValid}` on Save; errors render via `FormField error`, not a toast.
- **Evidence:** inventory §4.5 (red outline after touch; disabled Save without a reason = weak; G1 shows messages under the field, 52 uses); D-18.

### UI-FORM-007 · MUST · A tab that contains errors is marked
A tab trigger whose content has an invalid field shows its title in `text-destructive` with a leading `AlertCircle` icon, and has a `Tooltip` `t("TabHasErrors")`.
- **Check:** editors with tabs compute errors per tab.
- **Evidence:** inventory §4.4–4.5 (edit-relations-dialog red tab title + tooltip).

### UI-FORM-008 · MUST · Record editors with tabs use the standard tab strip
Tabs (shadcn `Tabs`) are aligned to the **end** (right) of the strip, and the strip sits directly under the command bar. Order:
1. **General** (text)
2. other text tabs for the entity
3. the **standard icon-only tabs**, in this order, each with a `Tooltip`:
   - Attachments (`Paperclip`)
   - Remarks (`MessageSquareText`)
   - Record identification (`Fingerprint`)

A record without these features omits those tabs; the order of the others stays.

**Nested tabs** (a tab group inside a tab, e.g. Invoice lines / Payments under General): **start**-aligned, text only, at most one level deep.
- **Check:** `TabsList` has `justify-end`; General is first; standard tabs are last and icon-only with tooltips; nested `TabsList` is start-aligned and has no further nesting.
- **Evidence:** inventory §4.4 (49 files: `mat-align-tabs="end"`, General + icon tabs; Attachments 44, Remarks 43, Record identification 43); D-35.

```tsx
// ✅
<Tabs defaultValue="general">
  <TabsList className="justify-end">
    <TabsTrigger value="general">{t("General")}</TabsTrigger>
    <TabsTrigger value="capabilities">{t("Capabilities")}</TabsTrigger>
    <IconTabTrigger value="attachments" icon={Paperclip} label={t("Attachments")} />
    <IconTabTrigger value="remarks" icon={MessageSquareText} label={t("Remarks")} />
    <IconTabTrigger value="identification" icon={Fingerprint} label={t("RecordIdentification")} />
  </TabsList>
  …
</Tabs>
```

### UI-FORM-009 · MUST · Dirty state compares values, not touches
`isDirty` is true when the current values differ from the values the form was opened with (deep compare, after normalising empty strings to `null`). Touching a field or opening a tab does not make the form dirty.
- **Check:** `isDirty` is computed from values; a test opens and closes an untouched editor without a discard prompt.
- **Evidence:** inventory §4.6 (the transport-order `hasChanges` was always true = the failure this rule prevents); UI-DLG-008.

### UI-FORM-010 · SHOULD · Booleans are a `Checkbox` with the label to its right
Record fields use `Checkbox` + label to its right, as one click target, aligned to the field grid. `Switch` is only for settings that take effect immediately, without Save.
- **Check:** no native `<input type="checkbox">`; no `Switch` inside an editor that has a Save.
- **Evidence:** inventory component counts (`mat-checkbox` in record forms; slide toggles in settings); §13 (native checkboxes in the admin screens = to replace).
