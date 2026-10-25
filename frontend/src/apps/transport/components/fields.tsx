import { Checkbox } from "@/components/ui/checkbox";
import { DatePicker } from "@/components/ui/date-picker";
import { FormField } from "@/components/ui/form-field";
import { Input } from "@/components/ui/input";
import { ReferenceCombobox, type ReferenceOption } from "@/components/ui/reference-combobox";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import type { FormContext } from "@/lib/forms/use-form-state";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* Small typed field helpers over FormField for the Transport app's forms (UI-FORM-001/002/010, UI-LOOK-001):
 * numbers are kept as strings in the form state and parsed on save, so typing stays free. */

interface BaseProps<TValues extends object> {
  form: FormContext<TValues>;
  field: keyof TValues & string;
  labelKey: string;
  required?: boolean;
  className?: string;
  /** A business-state block on top of read-only (UI-PERM-005). */
  disabled?: boolean;
  hint?: string;
}

interface TextProps<TValues extends object> extends BaseProps<TValues> {
  maxLength?: number;
  upper?: boolean;
}

export function TextField<TValues extends object>({ form, field, labelKey, required, maxLength, className, upper, disabled, hint }: TextProps<TValues>) {
  const { t } = useL();
  const value = (form.values[field] as unknown as string | null | undefined) ?? "";
  return (
    <FormField label={t(labelKey)} htmlFor={form.id(field)} required={required} error={form.error(field)} hint={hint} className={className}>
      <Input
        id={form.id(field)}
        value={value}
        maxLength={maxLength}
        disabled={form.readOnly || disabled}
        onChange={(e) => form.set(field, (upper ? e.target.value.toUpperCase() : e.target.value) as TValues[typeof field])}
        onBlur={() => form.blur(field)}
      />
    </FormField>
  );
}

export function NumberField<TValues extends object>({ form, field, labelKey, required, className, disabled, hint }: BaseProps<TValues>) {
  const { t } = useL();
  const value = (form.values[field] as unknown as string | null | undefined) ?? "";
  return (
    <FormField label={t(labelKey)} htmlFor={form.id(field)} required={required} error={form.error(field)} hint={hint} className={className}>
      <Input
        id={form.id(field)}
        inputMode="decimal"
        className="tabular-nums"
        value={value}
        disabled={form.readOnly || disabled}
        onChange={(e) => form.set(field, e.target.value as TValues[typeof field])}
        onBlur={() => form.blur(field)}
      />
    </FormField>
  );
}

export function TextAreaField<TValues extends object>({ form, field, labelKey, maxLength, className, disabled, rows = 3 }: TextProps<TValues> & { rows?: number }) {
  const { t } = useL();
  const value = (form.values[field] as unknown as string | null | undefined) ?? "";
  return (
    <FormField label={t(labelKey)} htmlFor={form.id(field)} error={form.error(field)} className={className} floating="always">
      <Textarea
        id={form.id(field)}
        value={value}
        maxLength={maxLength}
        rows={rows}
        disabled={form.readOnly || disabled}
        onChange={(e) => form.set(field, e.target.value as TValues[typeof field])}
        onBlur={() => form.blur(field)}
      />
    </FormField>
  );
}

export function CheckboxField<TValues extends object>({ form, field, labelKey, className, disabled }: BaseProps<TValues>) {
  const { t } = useL();
  const checked = Boolean(form.values[field]);
  return (
    <label className={`flex h-10 items-center gap-2 text-sm ${className ?? ""}`} htmlFor={form.id(field)}>
      <Checkbox id={form.id(field)} checked={checked} disabled={form.readOnly || disabled} onCheckedChange={(next) => form.set(field, (next === true) as TValues[typeof field])} />
      {t(labelKey)}
    </label>
  );
}

/** A date-only field (`YYYY-MM-DD` in the state; UI-FMT-004). */
export function DateField<TValues extends object>({ form, field, labelKey, required, className, disabled }: BaseProps<TValues>) {
  const { t } = useL();
  const value = (form.values[field] as unknown as string | null | undefined) ?? null;
  return (
    <FormField label={t(labelKey)} htmlFor={form.id(field)} required={required} error={form.error(field)} className={className} floating="always">
      <DatePicker id={form.id(field)} value={value} onChange={(next) => form.set(field, next as TValues[typeof field])} required={required} disabled={form.readOnly || disabled} />
    </FormField>
  );
}

interface SelectItemDef {
  value: number | string;
  label: string;
}

/** A fixed enum or small reference list (UI-LOOK-001: a Select). Values travel as strings inside the Select. */
export function SelectField<TValues extends object>({ form, field, labelKey, required, className, disabled, items, allowEmpty }: BaseProps<TValues> & { items: SelectItemDef[]; allowEmpty?: boolean }) {
  const { t } = useL();
  const value = form.values[field] as unknown as number | string | null | undefined;
  const EMPTY = "";
  const all = [...(allowEmpty ? [{ value: EMPTY, label: "" }] : []), ...items.map((item) => ({ value: String(item.value), label: item.label }))];
  return (
    <FormField label={t(labelKey)} htmlFor={form.id(field)} required={required} error={form.error(field)} className={className} floating="always">
      <Select
        items={all}
        value={value == null ? EMPTY : String(value)}
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

/** A record picker over a bounded lookup (UI-LOOK-001: a searched Combobox over a list loaded once). */
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
}: BaseProps<TValues> & { options: ReferenceOption[]; onCreateNew?: (term: string) => void; createLabel?: string }) {
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
      />
    </FormField>
  );
}

/** Lookup items as combobox options; a stored id whose row is gone (archived, deleted) keeps its name (design 51). */
export function toOptions(items: { id?: string; name?: string | null; code?: string | null }[] | undefined, stored?: { id: string | null | undefined; name: string | null | undefined }): ReferenceOption[] {
  const options = (items ?? []).map((item) => ({ value: item.id ?? "", label: item.name ?? item.code ?? "" }));
  if (stored?.id && !options.some((o) => o.value === stored.id)) options.push({ value: stored.id, label: stored.name ?? "" });
  return options;
}
