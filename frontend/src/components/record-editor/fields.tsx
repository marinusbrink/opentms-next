import type { Ref } from "react";

import { Checkbox } from "@/components/ui/checkbox";
import { FormField } from "@/components/ui/form-field";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import type { FormContext } from "@/lib/forms/use-form-state";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* Small typed field helpers over FormField for the Master Data editors (UI-FORM-001/002/010).
 * Numbers are kept as strings in the form state and parsed on save, so typing stays free. */

interface TextProps<TValues extends object> {
  form: FormContext<TValues>;
  field: keyof TValues & string;
  labelKey: string;
  required?: boolean;
  maxLength?: number;
  className?: string;
  /** Upper-cases while typing (codes). */
  upper?: boolean;
  /** A business-state block on top of read-only (UI-PERM-005), e.g. a number sequence in use (design 54). */
  disabled?: boolean;
  /** Localization key of a muted help text under the control. */
  hintKey?: string;
  /** A muted help text that is already localized (it carries a value, e.g. a template name); wins over hintKey. */
  hint?: string;
  /** The input element, for a caller that inserts at its caret (the e-mail template's Tags panel). */
  inputRef?: Ref<HTMLInputElement>;
  /** The input received focus (the caller tracks the field last focused). */
  onFocus?: () => void;
}

export function TextField<TValues extends object>({ form, field, labelKey, required, maxLength, className, upper, disabled, hintKey, hint, inputRef, onFocus }: TextProps<TValues>) {
  const { t } = useL();
  const value = (form.values[field] as unknown as string | null | undefined) ?? "";
  return (
    <FormField label={t(labelKey)} htmlFor={form.id(field)} required={required} error={form.error(field)} hint={hint ?? (hintKey ? t(hintKey) : undefined)} className={className}>
      <Input
        ref={inputRef}
        id={form.id(field)}
        value={value}
        maxLength={maxLength}
        disabled={form.readOnly || disabled}
        onChange={(e) => form.set(field, (upper ? e.target.value.toUpperCase() : e.target.value) as TValues[typeof field])}
        onBlur={() => form.blur(field)}
        onFocus={onFocus}
      />
    </FormField>
  );
}

export function NumberField<TValues extends object>({ form, field, labelKey, required, className, disabled, hintKey }: TextProps<TValues>) {
  const { t } = useL();
  const value = (form.values[field] as unknown as string | null | undefined) ?? "";
  return (
    <FormField label={t(labelKey)} htmlFor={form.id(field)} required={required} error={form.error(field)} hint={hintKey ? t(hintKey) : undefined} className={className}>
      <Input
        id={form.id(field)}
        inputMode="decimal"
        value={value}
        disabled={form.readOnly || disabled}
        onChange={(e) => form.set(field, e.target.value as TValues[typeof field])}
        onBlur={() => form.blur(field)}
      />
    </FormField>
  );
}

export function TextAreaField<TValues extends object>({ form, field, labelKey, maxLength, className }: TextProps<TValues>) {
  const { t } = useL();
  const value = (form.values[field] as unknown as string | null | undefined) ?? "";
  return (
    <FormField label={t(labelKey)} htmlFor={form.id(field)} error={form.error(field)} className={className} floating="always">
      <Textarea
        id={form.id(field)}
        value={value}
        maxLength={maxLength}
        rows={3}
        disabled={form.readOnly}
        onChange={(e) => form.set(field, e.target.value as TValues[typeof field])}
        onBlur={() => form.blur(field)}
      />
    </FormField>
  );
}

interface CheckboxProps<TValues extends object> extends TextProps<TValues> {
  /** A business-state block on top of read-only (UI-PERM-005); the reason is the text `describedBy` points at. */
  disabled?: boolean;
  /** Id of the element that explains the state (a hint next to or below the switch). */
  describedBy?: string;
}

export function CheckboxField<TValues extends object>({ form, field, labelKey, className, disabled, describedBy }: CheckboxProps<TValues>) {
  const { t } = useL();
  const checked = Boolean(form.values[field]);
  return (
    <label className={`flex h-10 items-center gap-2 text-sm ${className ?? ""}`} htmlFor={form.id(field)}>
      <Checkbox
        id={form.id(field)}
        checked={checked}
        disabled={form.readOnly || disabled}
        aria-describedby={describedBy}
        onCheckedChange={(next) => form.set(field, (next === true) as TValues[typeof field])}
      />
      {t(labelKey)}
    </label>
  );
}

interface EnumSelectProps<TValues extends object, T extends number> {
  form: FormContext<TValues>;
  field: keyof TValues & string;
  labelKey: string;
  /** The options, already localized ({ value, label }). */
  items: { value: T; label: string }[];
  required?: boolean;
  className?: string;
  /** A business-state block on top of read-only (UI-PERM-005). */
  disabled?: boolean;
  /** Optional enums keep a "none" row; the key of its text (the value is then null). */
  noneKey?: string;
  /** A fixed option is cleared to this value instead of null (the first item by default). */
  fallback?: T;
}

/** A `Select` over a fixed enum (UI-LOOK-001: up to 15 items) inside a FormField; the value is the enum number or null. */
export function EnumSelectField<TValues extends object, T extends number>({ form, field, labelKey, items, required, className, disabled, noneKey, fallback }: EnumSelectProps<TValues, T>) {
  const { t } = useL();
  const value = (form.values[field] as unknown as T | null | undefined) ?? null;
  const options = noneKey ? [{ value: null as T | null, label: t(noneKey) }, ...items] : items;
  return (
    <FormField label={t(labelKey)} htmlFor={form.id(field)} required={required} error={form.error(field)} className={className} floating="always">
      <Select
        items={options}
        value={value}
        onValueChange={(next) => {
          const picked = (next as T | null) ?? (noneKey ? null : (fallback ?? items[0]?.value ?? null));
          form.set(field, picked as unknown as TValues[typeof field]);
          form.blur(field);
        }}
        disabled={form.readOnly || disabled}
      >
        <SelectTrigger id={form.id(field)} className="h-10 w-full bg-background" aria-invalid={form.error(field) != null}>
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          {options.map((item) => (
            <SelectItem key={item.value ?? "none"} value={item.value}>
              {item.label}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    </FormField>
  );
}

export { numberError, numberToText, parseInteger, parseNumber } from "@/lib/forms/use-form-state";

const EMAIL_SHAPE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/** The validation key for an e-mail text: undefined when empty or shaped like an address (the server checks the same shape). */
export function emailError(value: string | null | undefined): string | undefined {
  const text = (value ?? "").trim();
  return !text || EMAIL_SHAPE.test(text) ? undefined : "MasterData:InvalidEmail";
}
