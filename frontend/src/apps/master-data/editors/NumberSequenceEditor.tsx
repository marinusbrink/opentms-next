import { useCallback, useMemo } from "react";
import { Info } from "lucide-react";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import type { EntityEditorProps } from "@/apps/master-data/components/editor-props";
import { NumberField, TextAreaField, TextField } from "@/apps/master-data/components/fields";
import { MasterDataEditor, type EditorTab, type FieldErrors } from "@/apps/master-data/components/MasterDataEditor";
import {
  MAX_PLATFORM_DESCRIPTION_LENGTH,
  MAX_PLATFORM_NAME_LENGTH,
  NUMBER_SEQUENCE_SCHEMES,
  numberSequences,
  type NumberSequenceDto,
} from "@/domains/platform/master-data";
import {
  MAX_AFFIX_LENGTH,
  MAX_MINIMUM_DIGITS,
  MIN_MINIMUM_DIGITS,
  NumberSequenceSchemes,
  formatNumber,
  today,
  usesMonth,
  usesYear,
  type NumberSequenceScheme,
} from "@/domains/platform/number-sequence-format";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The number sequence editor (design 49, UI design): `sm`; Name, Scheme, Prefix, Year prefix/suffix (schemes with a
 * year), Month prefix/suffix (year-month schemes), Minimum digits, the live example (the first number of today's
 * period, NumberSequenceFormatter mirrored in the domain) and Description. Once numbers have been drawn (`isInUse`)
 * the numbering fields are read-only with an inline note and Delete is disabled with the reason (UI-PERM-005); name and
 * description stay editable (the server's guard). */

interface Values {
  name: string;
  scheme: NumberSequenceScheme;
  prefix: string;
  yearPrefix: string;
  yearSuffix: string;
  monthPrefix: string;
  monthSuffix: string;
  /** Kept as text while typing (fields.tsx); parsed on save. */
  minimumDigits: string;
  description: string;
}

const DEFAULT_MINIMUM_DIGITS = 4;

const initialValuesFor = (initialName: string | undefined) => (dto: NumberSequenceDto | undefined): Values => ({
  name: dto?.name ?? initialName ?? "",
  scheme: dto?.scheme ?? NumberSequenceSchemes.FourDigitYear,
  prefix: dto?.prefix ?? "",
  yearPrefix: dto?.yearPrefix ?? "",
  yearSuffix: dto?.yearSuffix ?? "",
  monthPrefix: dto?.monthPrefix ?? "",
  monthSuffix: dto?.monthSuffix ?? "",
  minimumDigits: String(dto?.minimumDigits ?? DEFAULT_MINIMUM_DIGITS),
  description: dto?.description ?? "",
});

/** The minimum digits as a whole number from 1 to 12, else null. */
const parseDigits = (text: string): number | null => {
  const trimmed = text.trim();
  if (!/^\d+$/.test(trimmed)) return null;
  const n = Number(trimmed);
  return n >= MIN_MINIMUM_DIGITS && n <= MAX_MINIMUM_DIGITS ? n : null;
};

const validate = (v: Values): FieldErrors<Values> => {
  const errors: FieldErrors<Values> = {};
  if (!v.name.trim()) errors.name = "Platform:Required";
  if (parseDigits(v.minimumDigits) == null) errors.minimumDigits = "Platform:MinimumDigitsRange";
  return errors;
};

const affix = (text: string, used: boolean) => (used ? text || null : null);

export function NumberSequenceEditor({ mode, id, readOnly, open, onClose, onSaved, initialName }: EntityEditorProps) {
  const { t } = useL();
  const initialValues = useMemo(() => initialValuesFor(mode === "create" ? initialName : undefined), [mode, initialName]);
  const detail = numberSequences.useDetail(mode === "edit" ? id : undefined);
  const create = numberSequences.useCreate();
  const update = numberSequences.useUpdate();
  const remove = numberSequences.useDelete();
  const canDelete = useIsGranted("Platform.NumberSequences.Delete");
  const inUse = mode === "edit" && detail.data?.isInUse === true;

  const save = useCallback(
    async (v: Values, dto: NumberSequenceDto | undefined) => {
      /* In use: the numbering fields go back exactly as loaded (the server refuses any change to them). Otherwise
       * the affixes a scheme does not use are cleared, so a later scheme change never revives stale ones. */
      const format =
        inUse && dto
          ? {
              scheme: dto.scheme ?? v.scheme,
              prefix: dto.prefix ?? null,
              yearPrefix: dto.yearPrefix ?? null,
              yearSuffix: dto.yearSuffix ?? null,
              monthPrefix: dto.monthPrefix ?? null,
              monthSuffix: dto.monthSuffix ?? null,
              minimumDigits: dto.minimumDigits ?? DEFAULT_MINIMUM_DIGITS,
            }
          : {
              scheme: v.scheme,
              prefix: v.prefix || null,
              yearPrefix: affix(v.yearPrefix, usesYear(v.scheme)),
              yearSuffix: affix(v.yearSuffix, usesYear(v.scheme)),
              monthPrefix: affix(v.monthPrefix, usesMonth(v.scheme)),
              monthSuffix: affix(v.monthSuffix, usesMonth(v.scheme)),
              minimumDigits: parseDigits(v.minimumDigits) ?? DEFAULT_MINIMUM_DIGITS,
            };
      const body = { name: v.name.trim(), description: v.description.trim() || null, ...format };
      return mode === "create"
        ? await create.mutateAsync(body)
        : await update.mutateAsync({ id: id!, dto: { ...body, concurrencyStamp: dto?.concurrencyStamp ?? "" } });
    },
    [create, update, mode, id, inUse],
  );

  const schemeItems = NUMBER_SEQUENCE_SCHEMES.map((s) => ({ value: s.value, label: t(s.key) }));

  const tabs: EditorTab<Values>[] = [
    {
      value: "general",
      labelKey: "Platform:Tab.General",
      fields: ["name", "scheme", "prefix", "yearPrefix", "yearSuffix", "monthPrefix", "monthSuffix", "minimumDigits", "description"],
      render: (form) => {
        const v = form.values;
        const digits = parseDigits(v.minimumDigits);
        const example =
          digits == null
            ? null
            : formatNumber(
                {
                  scheme: v.scheme,
                  prefix: v.prefix,
                  yearPrefix: v.yearPrefix,
                  yearSuffix: v.yearSuffix,
                  monthPrefix: v.monthPrefix,
                  monthSuffix: v.monthSuffix,
                  minimumDigits: digits,
                },
                today(),
                1,
              );
        /* "Next number looks like {0}": the number is set in mono, wherever the language puts the placeholder. */
        const [exampleBefore, exampleAfter = ""] = t("Platform:ExampleLabel").split("{0}");
        return (
          <FormSection title={t("Platform:NumberSequence")}>
            <div className="grid grid-cols-1 gap-3">
              <TextField form={form} field="name" labelKey="Platform:Name" required maxLength={MAX_PLATFORM_NAME_LENGTH} />
              <FormField label={t("Platform:Scheme")} htmlFor={form.id("scheme")} required error={form.error("scheme")} floating="always">
                <Select
                  items={schemeItems}
                  value={form.values.scheme}
                  onValueChange={(next) => {
                    if (next != null) form.set("scheme", next as NumberSequenceScheme);
                    form.blur("scheme");
                  }}
                  disabled={form.readOnly || inUse}
                >
                  <SelectTrigger id={form.id("scheme")} className="h-10 w-full bg-background">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {schemeItems.map((item) => (
                      <SelectItem key={item.value} value={item.value}>
                        {item.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </FormField>
              <TextField form={form} field="prefix" labelKey="Platform:Prefix" maxLength={MAX_AFFIX_LENGTH} disabled={inUse} />
              {usesYear(v.scheme) && (
                <>
                  <TextField form={form} field="yearPrefix" labelKey="Platform:YearPrefix" maxLength={MAX_AFFIX_LENGTH} disabled={inUse} />
                  <TextField form={form} field="yearSuffix" labelKey="Platform:YearSuffix" maxLength={MAX_AFFIX_LENGTH} disabled={inUse} />
                </>
              )}
              {usesMonth(v.scheme) && (
                <>
                  <TextField form={form} field="monthPrefix" labelKey="Platform:MonthPrefix" maxLength={MAX_AFFIX_LENGTH} disabled={inUse} />
                  <TextField form={form} field="monthSuffix" labelKey="Platform:MonthSuffix" maxLength={MAX_AFFIX_LENGTH} disabled={inUse} />
                </>
              )}
              <NumberField form={form} field="minimumDigits" labelKey="Platform:MinimumDigits" required disabled={inUse} />
              {example != null && (
                <p className="text-sm text-muted-foreground" aria-live="polite" data-testid="number-sequence-example">
                  {exampleBefore}
                  <span className="font-mono font-semibold text-foreground">{example}</span>
                  {exampleAfter}
                </p>
              )}
              <TextAreaField form={form} field="description" labelKey="Platform:Description" maxLength={MAX_PLATFORM_DESCRIPTION_LENGTH} />
            </div>
          </FormSection>
        );
      },
    },
  ];

  return (
    <MasterDataEditor<Values, NumberSequenceDto>
      entityKey="NumberSequence"
      resource="Platform"
      queryEntity={numberSequences.entity}
      size="sm"
      mode={mode}
      readOnly={readOnly}
      open={open}
      onClose={onClose}
      onSaved={onSaved}
      detail={detail}
      titleOf={(dto) => dto.name ?? ""}
      initialValues={initialValues}
      validate={validate}
      save={save}
      isSaving={create.isPending || update.isPending}
      tabs={tabs}
      deleteRecord={mode === "edit" && canDelete ? remove : undefined}
      deleteDisabledReasonKey={inUse ? "Platform:NumberSequenceInUseCannotDelete" : undefined}
      notice={
        inUse ? (
          <Alert>
            <Info />
            <AlertDescription>{t("Platform:InUseNote")}</AlertDescription>
          </Alert>
        ) : undefined
      }
    />
  );
}
