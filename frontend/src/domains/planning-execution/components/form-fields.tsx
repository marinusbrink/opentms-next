import { FormField } from "@/components/ui/form-field";
import { Input } from "@/components/ui/input";
import { ReferenceCombobox, type ReferenceOption } from "@/components/ui/reference-combobox";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import type { FormContext } from "@/lib/forms/use-form-state";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The typed field helpers the shared Planning dialogs use (the New trip dialog, design 70: shared by the Transport app
 * and the Planboard). They render exactly like the Transport app's own helpers (apps/transport/components/fields.tsx),
 * which a domain component may not import (an app never imports another app, and a domain never imports an app). */

interface BaseProps<TValues extends object> {
  form: FormContext<TValues>;
  field: keyof TValues & string;
  labelKey: string;
  required?: boolean;
  className?: string;
  disabled?: boolean;
  hint?: string;
}

export function TextField<TValues extends object>({ form, field, labelKey, required, maxLength, className, disabled, hint }: BaseProps<TValues> & { maxLength?: number }) {
  const { t } = useL();
  const value = (form.values[field] as unknown as string | null | undefined) ?? "";
  return (
    <FormField label={t(labelKey)} htmlFor={form.id(field)} required={required} error={form.error(field)} hint={hint} className={className}>
      <Input
        id={form.id(field)}
        value={value}
        maxLength={maxLength}
        disabled={form.readOnly || disabled}
        onChange={(e) => form.set(field, e.target.value as TValues[typeof field])}
        onBlur={() => form.blur(field)}
      />
    </FormField>
  );
}

interface SelectItemDef {
  value: number | string;
  label: string;
}

/** A fixed enum (UI-LOOK-001: a Select). Values travel as strings inside the Select. */
export function SelectField<TValues extends object>({ form, field, labelKey, required, className, disabled, items }: BaseProps<TValues> & { items: SelectItemDef[] }) {
  const { t } = useL();
  const value = form.values[field] as unknown as number | string | null | undefined;
  /* The caller's localized enum table (never records: UI-LOOK-001). */
  const all = [...items.map((item) => ({ value: String(item.value), label: item.label }))];
  return (
    <FormField label={t(labelKey)} htmlFor={form.id(field)} required={required} error={form.error(field)} className={className} floating="always">
      <Select
        items={all}
        value={value == null ? "" : String(value)}
        onValueChange={(next) => {
          const picked = items.find((item) => String(item.value) === next);
          form.set(field, (picked ? picked.value : null) as TValues[typeof field]);
        }}
        disabled={form.readOnly || disabled}
      >
        <SelectTrigger id={form.id(field)} className="h-10 w-full bg-background" aria-invalid={form.error(field) != null}>
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          {all.map((item) => (
            <SelectItem key={item.value} value={item.value}>
              {item.label}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    </FormField>
  );
}

/** A record picker over a bounded lookup, with "Add new …" (UI-LOOK-004) and the edit pencil (UI-LOOK-005) when passed. */
export function ComboField<TValues extends object>({
  form,
  field,
  labelKey,
  required,
  className,
  disabled,
  options,
  onCreateNew,
  createLabel,
  onEdit,
  editLabel,
}: BaseProps<TValues> & { options: ReferenceOption[]; onCreateNew?: (term: string) => void; createLabel?: string; onEdit?: (value: string) => void; editLabel?: string }) {
  const { t } = useL();
  const value = (form.values[field] as unknown as string | null | undefined) ?? null;
  return (
    <FormField label={t(labelKey)} htmlFor={form.id(field)} required={required} error={form.error(field)} className={className} floating="always">
      <ReferenceCombobox
        id={form.id(field)}
        value={value}
        onChange={(next) => form.set(field, next as TValues[typeof field])}
        options={options}
        required={required}
        disabled={form.readOnly || disabled}
        aria-invalid={form.error(field) != null}
        onCreateNew={form.readOnly ? undefined : onCreateNew}
        createLabel={createLabel}
        onEdit={onEdit}
        editLabel={editLabel}
      />
    </FormField>
  );
}

/** Lookup items as combobox options. */
export function toOptions(items: { id?: string; name?: string | null; code?: string | null }[] | undefined): ReferenceOption[] {
  return (items ?? []).map((item) => ({ value: item.id ?? "", label: item.name ?? item.code ?? "" }));
}
