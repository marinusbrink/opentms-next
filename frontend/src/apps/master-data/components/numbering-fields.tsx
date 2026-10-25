import { Checkbox } from "@/components/ui/checkbox";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { CheckboxField, NumberField, TextField, numberError, parseInteger } from "@/apps/master-data/components/fields";
import type { FieldErrors, FormContext } from "@/apps/master-data/components/MasterDataEditor";
import { NUMBERING_SEPARATORS, formatNumber, numberingError } from "@/lib/reference-data/number-sequence";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The numbering section of every numbered type (design 50): prefix, separator, year, month, digits, the next number
 * and a live preview of what the next save issues. The editor keeps these fields in its own form values. */

export interface NumberingFormValues {
  numberingPrefix: string;
  numberingIncludeYear: boolean;
  numberingIncludeMonth: boolean;
  numberingSeparator: string;
  numberingDigits: string;
  nextNumber: string;
}

export interface NumberingDto {
  prefix?: string | null;
  includeYear?: boolean;
  includeMonth?: boolean;
  separator?: string | null;
  digits?: number;
}

export const NUMBERING_FIELDS = [
  "numberingPrefix",
  "numberingIncludeYear",
  "numberingIncludeMonth",
  "numberingSeparator",
  "numberingDigits",
  "nextNumber",
] as const;

/** The form values for a stored numbering (or the given defaults for a new type). */
export function numberingValuesOf(numbering: NumberingDto | null | undefined, nextNumber: number | string | null | undefined, defaults: NumberingDto): NumberingFormValues {
  const n = numbering ?? defaults;
  return {
    numberingPrefix: n.prefix ?? "",
    numberingIncludeYear: n.includeYear ?? false,
    numberingIncludeMonth: n.includeMonth ?? false,
    numberingSeparator: n.separator ?? "",
    numberingDigits: String(n.digits ?? 5),
    nextNumber: nextNumber == null ? "1" : String(nextNumber),
  };
}

export function numberingDtoOf(v: NumberingFormValues): Required<NumberingDto> {
  return {
    prefix: v.numberingPrefix.trim() || null,
    includeYear: v.numberingIncludeYear,
    includeMonth: v.numberingIncludeYear && v.numberingIncludeMonth,
    separator: v.numberingSeparator,
    digits: parseInteger(v.numberingDigits) ?? 5,
  } as Required<NumberingDto>;
}

export function validateNumbering<T extends NumberingFormValues>(v: T, minimumNextNumber: number): FieldErrors<T> {
  const errors: FieldErrors<T> = {};
  const digitsError = numberError(v.numberingDigits, { integer: true, min: 3, max: 10 });
  if (digitsError || !v.numberingDigits.trim()) (errors as FieldErrors<NumberingFormValues>).numberingDigits = "MasterData:Numbering.InvalidDigits";
  const shapeError = numberingError(numberingDtoOf(v));
  if (shapeError === "MasterData:Numbering.InvalidPrefix") (errors as FieldErrors<NumberingFormValues>).numberingPrefix = shapeError;
  const next = parseInteger(v.nextNumber);
  if (next == null || Number.isNaN(next) || next < minimumNextNumber) (errors as FieldErrors<NumberingFormValues>).nextNumber = "MasterData:Numbering.NextNumberTooLow";
  return errors;
}

export function NumberingFields<TValues extends NumberingFormValues>({ form }: { form: FormContext<TValues> }) {
  const { t } = useL();
  const v = form.values;
  const next = parseInteger(v.nextNumber);
  const preview = next != null && !Number.isNaN(next) && next > 0 && !numberingError(numberingDtoOf(v)) ? formatNumber(numberingDtoOf(v), next, new Date()) : "—";
  const separators = NUMBERING_SEPARATORS.map((value) => ({ value, label: value === "" ? t("MasterData:Numbering.NoSeparator") : value }));

  return (
    <FormSection title={t("MasterData:Numbering")}>
      <div className="grid grid-cols-2 gap-x-4 gap-y-3">
        <TextField form={form} field="numberingPrefix" labelKey="MasterData:Numbering.Prefix" maxLength={16} upper />
        <FormField label={t("MasterData:Numbering.Separator")} htmlFor={form.id("numberingSeparator")} floating="always">
          <Select
            items={separators}
            value={v.numberingSeparator}
            onValueChange={(value) => form.set("numberingSeparator", ((value as string | null) ?? "") as TValues["numberingSeparator"])}
            disabled={form.readOnly}
          >
            <SelectTrigger id={form.id("numberingSeparator")} className="h-10 w-full bg-background">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {separators.map((item) => (
                <SelectItem key={item.value || "none"} value={item.value}>
                  {item.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </FormField>
        <CheckboxField form={form} field="numberingIncludeYear" labelKey="MasterData:Numbering.IncludeYear" />
        <label className="flex h-10 items-center gap-2 text-sm" htmlFor={form.id("numberingIncludeMonth")}>
          <Checkbox
            id={form.id("numberingIncludeMonth")}
            checked={v.numberingIncludeYear && v.numberingIncludeMonth}
            disabled={form.readOnly || !v.numberingIncludeYear}
            onCheckedChange={(next) => form.set("numberingIncludeMonth", (next === true) as TValues["numberingIncludeMonth"])}
          />
          {t("MasterData:Numbering.IncludeMonth")}
        </label>
        <NumberField form={form} field="numberingDigits" labelKey="MasterData:Numbering.Digits" required />
        <NumberField form={form} field="nextNumber" labelKey="MasterData:Numbering.NextNumber" required />
        <p className="col-span-2 text-sm text-muted-foreground" data-testid="numbering-preview">
          {t("MasterData:Numbering.Preview").replace("{0}", preview)}
        </p>
      </div>
    </FormSection>
  );
}
