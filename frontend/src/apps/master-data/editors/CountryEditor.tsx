import { useCallback, useMemo } from "react";

import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { ReferenceCombobox } from "@/components/ui/reference-combobox";
import type { EntityEditorProps } from "@/apps/master-data/components/editor-props";
import { NumberField, TextAreaField, TextField, numberError, numberToText, parseInteger } from "@/apps/master-data/components/fields";
import { MasterDataEditor, type EditorTab, type FieldErrors } from "@/apps/master-data/components/MasterDataEditor";
import { timeZoneOptions } from "@/apps/master-data/components/time-zones";
import { countries, currencies, type CountryDto } from "@/domains/master-data/entities";
import { resolveLocalizedName, withTextForCulture } from "@/domains/master-data/localized-name";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

interface Values {
  name: string;
  isoAlpha2: string;
  isoAlpha3: string;
  isoNumeric: string;
  defaultCurrencyId: string | null;
  primaryTimeZoneId: string | null;
  description: string;
}

/* One Name field in the user's language (PO, 2026-10-11), as in the currency editor. */
const initialValuesFor = (culture: string) => (dto: CountryDto | undefined): Values => ({
  name: resolveLocalizedName(dto?.names, culture),
  isoAlpha2: dto?.isoAlpha2 ?? "",
  isoAlpha3: dto?.isoAlpha3 ?? "",
  isoNumeric: numberToText(dto?.isoNumeric),
  defaultCurrencyId: dto?.defaultCurrencyId ?? null,
  primaryTimeZoneId: dto?.primaryTimeZoneId ?? null,
  description: dto?.description ?? "",
});

const validate = (v: Values): FieldErrors<Values> => {
  const errors: FieldErrors<Values> = {};
  if (!/^[A-Za-z]{2}$/.test(v.isoAlpha2.trim())) errors.isoAlpha2 = v.isoAlpha2.trim() ? "MasterData:InvalidIsoCode2" : "MasterData:Required";
  if (!/^[A-Za-z]{3}$/.test(v.isoAlpha3.trim())) errors.isoAlpha3 = v.isoAlpha3.trim() ? "MasterData:InvalidIsoCode3" : "MasterData:Required";
  const numeric = numberError(v.isoNumeric, { integer: true, min: 0, max: 999 });
  if (numeric) errors.isoNumeric = numeric;
  if (!v.name.trim()) errors.name = "MasterData:Required";
  return errors;
};

export function CountryEditor({ mode, id, readOnly, open, onClose, onSaved }: EntityEditorProps) {
  const { t, culture } = useL();
  const detail = countries.useDetail(mode === "edit" ? id : undefined);
  const initialValues = useMemo(() => initialValuesFor(culture), [culture]);
  const create = countries.useCreate();
  const update = countries.useUpdate();
  const remove = countries.useDelete();
  const canDelete = useIsGranted("MasterData.Countries.Delete");
  const currencyLookup = currencies.useLookup(open);
  /* The lookup lists active currencies (pickers offer those); a country's current default may be an
   * inactive one, so the loaded record's currency is kept as an option and stays visible. */
  const currencyOptions = useMemo(() => {
    const options = (currencyLookup.data ?? []).map((c) => ({ value: c.id ?? "", label: `${c.code ?? ""} ${c.name ?? ""}`.trim() }));
    const current = detail.data?.defaultCurrencyId;
    if (current && !options.some((o) => o.value === current)) {
      options.push({ value: current, label: detail.data?.defaultCurrencyCode ?? current });
    }
    return options;
  }, [currencyLookup.data, detail.data?.defaultCurrencyId, detail.data?.defaultCurrencyCode]);
  const zones = useMemo(timeZoneOptions, []);

  const save = useCallback(
    async (v: Values, dto: CountryDto | undefined) => {
      const body = {
        isoAlpha2: v.isoAlpha2.trim().toUpperCase(),
        isoAlpha3: v.isoAlpha3.trim().toUpperCase(),
        isoNumeric: parseInteger(v.isoNumeric),
        names: withTextForCulture(dto?.names, culture, v.name),
        defaultCurrencyId: v.defaultCurrencyId,
        primaryTimeZoneId: v.primaryTimeZoneId,
        description: v.description.trim() || null,
      };
      if (mode === "create") {
        await create.mutateAsync(body);
      } else {
        await update.mutateAsync({ id: id!, dto: { ...body, concurrencyStamp: dto?.concurrencyStamp ?? "" } });
      }
    },
    [create, update, mode, id, culture],
  );

  const tabs: EditorTab<Values>[] = [
    {
      value: "general",
      labelKey: "General",
      fields: ["name", "defaultCurrencyId", "isoAlpha2", "isoAlpha3", "isoNumeric", "primaryTimeZoneId", "description"],
      render: (form) => (
        <FormSection title={t("MasterData:Country")}>
          {/* Rows per the PO (2026-10-11): name + default currency; alpha-2 + alpha-3; numeric + time zone; description. */}
          <div className="grid grid-cols-2 gap-x-4 gap-y-3">
            <TextField form={form} field="name" labelKey="MasterData:Name" required maxLength={128} />
            <FormField label={t("MasterData:DefaultCurrency")} htmlFor={form.id("defaultCurrencyId")} floating="always">
              <ReferenceCombobox
                id={form.id("defaultCurrencyId")}
                value={form.values.defaultCurrencyId}
                onChange={(next) => form.set("defaultCurrencyId", next)}
                options={currencyOptions}
                disabled={form.readOnly}
              />
            </FormField>
            <TextField form={form} field="isoAlpha2" labelKey="MasterData:IsoAlpha2" required maxLength={2} upper />
            <TextField form={form} field="isoAlpha3" labelKey="MasterData:IsoAlpha3" required maxLength={3} upper />
            <NumberField form={form} field="isoNumeric" labelKey="MasterData:IsoNumeric" />
            <FormField label={t("MasterData:PrimaryTimeZone")} htmlFor={form.id("primaryTimeZoneId")} floating="always">
              <ReferenceCombobox
                id={form.id("primaryTimeZoneId")}
                value={form.values.primaryTimeZoneId}
                onChange={(next) => form.set("primaryTimeZoneId", next)}
                options={zones}
                disabled={form.readOnly}
              />
            </FormField>
            <TextAreaField form={form} field="description" labelKey="MasterData:Description" maxLength={2500} className="col-span-2" />
          </div>
        </FormSection>
      ),
    },
  ];

  return (
    <MasterDataEditor<Values, CountryDto>
      entityKey="Country"
      queryEntity={countries.entity}
      size="sm"
      mode={mode}
      readOnly={readOnly}
      open={open}
      onClose={onClose}
      onSaved={onSaved}
      detail={detail}
      titleOf={(dto) => dto.isoAlpha2 ?? ""}
      initialValues={initialValues}
      validate={validate}
      save={save}
      isSaving={create.isPending || update.isPending}
      tabs={tabs}
      deleteRecord={mode === "edit" && canDelete ? remove : undefined}
    />
  );
}
