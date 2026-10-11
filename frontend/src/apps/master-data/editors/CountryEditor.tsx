import { useCallback, useMemo } from "react";

import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { ReferenceCombobox } from "@/components/ui/reference-combobox";
import type { EntityEditorProps } from "@/apps/master-data/components/editor-props";
import { NumberField, TextAreaField, TextField, numberError, numberToText, parseInteger } from "@/apps/master-data/components/fields";
import { LocalizedNameFields } from "@/apps/master-data/components/LocalizedNameFields";
import { MasterDataEditor, type EditorTab, type FieldErrors } from "@/apps/master-data/components/MasterDataEditor";
import { countries, currencies, type CountryDto } from "@/domains/master-data/entities";
import { localizedNameFromMap, localizedNameToMap, validateLocalizedName, type LocalizedNameValues } from "@/domains/master-data/localized-name";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

interface Values {
  isoAlpha2: string;
  isoAlpha3: string;
  isoNumeric: string;
  names: LocalizedNameValues;
  defaultCurrencyId: string | null;
  primaryTimeZoneId: string | null;
  description: string;
}

const initialValues = (dto: CountryDto | undefined): Values => ({
  isoAlpha2: dto?.isoAlpha2 ?? "",
  isoAlpha3: dto?.isoAlpha3 ?? "",
  isoNumeric: numberToText(dto?.isoNumeric),
  names: localizedNameFromMap(dto?.names),
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
  const name = validateLocalizedName(v.names);
  if (name) errors.names = name;
  return errors;
};

/** The browser's IANA zones (every id the server accepts is in this list on a current runtime). */
function timeZoneOptions(): { value: string; label: string }[] {
  const ids = typeof Intl.supportedValuesOf === "function" ? Intl.supportedValuesOf("timeZone") : [];
  return ids.map((id) => ({ value: id, label: id }));
}

export function CountryEditor({ mode, id, readOnly, open, onClose, onSaved }: EntityEditorProps) {
  const { t } = useL();
  const detail = countries.useDetail(mode === "edit" ? id : undefined);
  const create = countries.useCreate();
  const update = countries.useUpdate();
  const remove = countries.useDelete();
  const canDelete = useIsGranted("MasterData.Countries.Delete");
  const currencyLookup = currencies.useLookup(open);
  const currencyOptions = useMemo(
    () => (currencyLookup.data ?? []).map((c) => ({ value: c.id ?? "", label: `${c.code ?? ""} ${c.name ?? ""}`.trim() })),
    [currencyLookup.data],
  );
  const zones = useMemo(timeZoneOptions, []);

  const save = useCallback(
    async (v: Values, dto: CountryDto | undefined) => {
      const body = {
        isoAlpha2: v.isoAlpha2.trim().toUpperCase(),
        isoAlpha3: v.isoAlpha3.trim().toUpperCase(),
        isoNumeric: parseInteger(v.isoNumeric),
        names: localizedNameToMap(v.names),
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
    [create, update, mode, id],
  );

  const tabs: EditorTab<Values>[] = [
    {
      value: "general",
      labelKey: "General",
      fields: ["isoAlpha2", "isoAlpha3", "isoNumeric", "names", "defaultCurrencyId", "primaryTimeZoneId", "description"],
      render: (form) => (
        <FormSection title={t("MasterData:Country")}>
          <div className="grid grid-cols-1 gap-3">
            <TextField form={form} field="isoAlpha2" labelKey="MasterData:IsoAlpha2" required maxLength={2} upper />
            <TextField form={form} field="isoAlpha3" labelKey="MasterData:IsoAlpha3" required maxLength={3} upper />
            <NumberField form={form} field="isoNumeric" labelKey="MasterData:IsoNumeric" />
            <LocalizedNameFields form={form} />
            <FormField label={t("MasterData:DefaultCurrency")} htmlFor={form.id("defaultCurrencyId")} floating="always">
              <ReferenceCombobox
                id={form.id("defaultCurrencyId")}
                value={form.values.defaultCurrencyId}
                onChange={(next) => form.set("defaultCurrencyId", next)}
                options={currencyOptions}
                disabled={form.readOnly}
              />
            </FormField>
            <FormField label={t("MasterData:PrimaryTimeZone")} htmlFor={form.id("primaryTimeZoneId")} floating="always">
              <ReferenceCombobox
                id={form.id("primaryTimeZoneId")}
                value={form.values.primaryTimeZoneId}
                onChange={(next) => form.set("primaryTimeZoneId", next)}
                options={zones}
                disabled={form.readOnly}
              />
            </FormField>
            <TextAreaField form={form} field="description" labelKey="MasterData:Description" maxLength={2500} />
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
