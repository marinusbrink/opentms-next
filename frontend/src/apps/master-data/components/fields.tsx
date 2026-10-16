import { Checkbox } from "@/components/ui/checkbox";
import { FormField } from "@/components/ui/form-field";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import type { FormContext } from "@/apps/master-data/components/MasterDataEditor";
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
}

export function TextField<TValues extends object>({ form, field, labelKey, required, maxLength, className, upper }: TextProps<TValues>) {
  const { t } = useL();
  const value = (form.values[field] as unknown as string | null | undefined) ?? "";
  return (
    <FormField label={t(labelKey)} htmlFor={form.id(field)} required={required} error={form.error(field)} className={className}>
      <Input
        id={form.id(field)}
        value={value}
        maxLength={maxLength}
        disabled={form.readOnly}
        onChange={(e) => form.set(field, (upper ? e.target.value.toUpperCase() : e.target.value) as TValues[typeof field])}
        onBlur={() => form.blur(field)}
      />
    </FormField>
  );
}

export function NumberField<TValues extends object>({ form, field, labelKey, required, className }: TextProps<TValues>) {
  const { t } = useL();
  const value = (form.values[field] as unknown as string | null | undefined) ?? "";
  return (
    <FormField label={t(labelKey)} htmlFor={form.id(field)} required={required} error={form.error(field)} className={className}>
      <Input
        id={form.id(field)}
        inputMode="decimal"
        value={value}
        disabled={form.readOnly}
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

/** "12,5" or "12.5" → 12.5; empty → null; anything else → NaN (the validator reports it). */
export function parseNumber(value: string | null | undefined): number | null {
  const text = (value ?? "").trim().replace(",", ".");
  if (!text) return null;
  const n = Number(text);
  return Number.isFinite(n) ? n : NaN;
}

export function parseInteger(value: string | null | undefined): number | null {
  const n = parseNumber(value);
  return n == null || Number.isNaN(n) ? n : Number.isInteger(n) ? n : NaN;
}

export function numberToText(value: number | null | undefined): string {
  return value == null ? "" : String(value);
}

/** The validation key for a numeric text: undefined when empty or a number. */
export function numberError(value: string | null | undefined, options?: { integer?: boolean; min?: number; max?: number }): string | undefined {
  const n = options?.integer ? parseInteger(value) : parseNumber(value);
  if (n == null) return undefined;
  if (Number.isNaN(n)) return "MasterData:InvalidNumber";
  if (options?.min != null && n < options.min) return "MasterData:InvalidNumber";
  if (options?.max != null && n > options.max) return "MasterData:InvalidNumber";
  return undefined;
}

const EMAIL_SHAPE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/** The validation key for an e-mail text: undefined when empty or shaped like an address (the server checks the same shape). */
export function emailError(value: string | null | undefined): string | undefined {
  const text = (value ?? "").trim();
  return !text || EMAIL_SHAPE.test(text) ? undefined : "MasterData:InvalidEmail";
}
