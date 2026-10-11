import { useCallback, useState } from "react";
import { Star } from "lucide-react";
import { toast } from "sonner";

import { Badge } from "@/components/ui/badge";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { FormSection } from "@/components/ui/form-section";
import type { EntityEditorProps } from "@/apps/master-data/components/editor-props";
import { CheckboxField, NumberField, TextAreaField, TextField, numberError, numberToText, parseInteger } from "@/apps/master-data/components/fields";
import { LocalizedNameFields } from "@/apps/master-data/components/LocalizedNameFields";
import { MasterDataEditor, type EditorTab, type FieldErrors } from "@/apps/master-data/components/MasterDataEditor";
import { currencies, useSetDefaultCurrency, type CurrencyDto } from "@/domains/master-data/entities";
import { localizedNameFromMap, localizedNameToMap, validateLocalizedName, type LocalizedNameValues } from "@/domains/master-data/localized-name";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

interface Values {
  isoCode: string;
  isoNumeric: string;
  minorUnits: string;
  symbol: string;
  names: LocalizedNameValues;
  description: string;
  isActive: boolean;
}

const initialValues = (dto: CurrencyDto | undefined): Values => ({
  isoCode: dto?.isoCode ?? "",
  isoNumeric: numberToText(dto?.isoNumeric),
  minorUnits: numberToText(dto?.minorUnits),
  symbol: dto?.symbol ?? "",
  names: localizedNameFromMap(dto?.names),
  description: dto?.description ?? "",
  isActive: dto?.isActive ?? true,
});

const validate = (v: Values): FieldErrors<Values> => {
  const errors: FieldErrors<Values> = {};
  if (!/^[A-Za-z]{3}$/.test(v.isoCode.trim())) errors.isoCode = v.isoCode.trim() ? "MasterData:InvalidIsoCode3" : "MasterData:Required";
  const numeric = numberError(v.isoNumeric, { integer: true, min: 0, max: 999 });
  if (numeric) errors.isoNumeric = numeric;
  const minor = numberError(v.minorUnits, { integer: true, min: 0, max: 4 });
  if (minor) errors.minorUnits = minor;
  const name = validateLocalizedName(v.names);
  if (name) errors.names = name;
  return errors;
};

export function CurrencyEditor({ mode, id, readOnly, open, onClose, onSaved }: EntityEditorProps) {
  const { t } = useL();
  const detail = currencies.useDetail(mode === "edit" ? id : undefined);
  const create = currencies.useCreate();
  const update = currencies.useUpdate();
  const remove = currencies.useDelete();
  const setDefault = useSetDefaultCurrency();
  const canDelete = useIsGranted("MasterData.Currencies.Delete");
  const [confirmDefault, setConfirmDefault] = useState(false);

  const save = useCallback(
    async (v: Values, dto: CurrencyDto | undefined) => {
      const body = {
        isoCode: v.isoCode.trim().toUpperCase(),
        isoNumeric: parseInteger(v.isoNumeric),
        minorUnits: parseInteger(v.minorUnits),
        symbol: v.symbol.trim() || null,
        names: localizedNameToMap(v.names),
        description: v.description.trim() || null,
        isActive: v.isActive,
      };
      if (mode === "create") {
        await create.mutateAsync(body);
      } else {
        await update.mutateAsync({ id: id!, dto: { ...body, concurrencyStamp: dto?.concurrencyStamp ?? "" } });
      }
    },
    [create, update, mode, id],
  );

  const makeDefault = async () => {
    try {
      await setDefault.mutateAsync(id!);
      setConfirmDefault(false);
      toast.success(t("MasterData:DefaultCurrencySet"));
      onSaved();
      onClose();
    } catch (err) {
      setConfirmDefault(false);
      toast.error(getApiErrorMessage(err, t));
    }
  };

  const tabs: EditorTab<Values>[] = [
    {
      value: "general",
      labelKey: "General",
      fields: ["isoCode", "isoNumeric", "minorUnits", "symbol", "names", "description"],
      render: (form) => (
        <FormSection title={t("MasterData:Currency")}>
          <div className="grid grid-cols-1 gap-3">
            <TextField form={form} field="isoCode" labelKey="MasterData:IsoCode" required maxLength={3} upper />
            <NumberField form={form} field="isoNumeric" labelKey="MasterData:IsoNumeric" />
            <NumberField form={form} field="minorUnits" labelKey="MasterData:MinorUnits" />
            <TextField form={form} field="symbol" labelKey="MasterData:Symbol" maxLength={8} />
            <LocalizedNameFields form={form} />
            <TextAreaField form={form} field="description" labelKey="MasterData:Description" maxLength={2500} />
            <CheckboxField form={form} field="isActive" labelKey="MasterData:IsActive" />
          </div>
        </FormSection>
      ),
    },
  ];

  const isDefault = detail.data?.isDefault === true;

  return (
    <>
      <MasterDataEditor<Values, CurrencyDto>
        entityKey="Currency"
        queryEntity={currencies.entity}
        size="sm"
        mode={mode}
        readOnly={readOnly}
        open={open}
        onClose={onClose}
        onSaved={onSaved}
        detail={detail}
        titleOf={(dto) => dto.isoCode ?? ""}
        initialValues={initialValues}
        validate={validate}
        save={save}
        isSaving={create.isPending || update.isPending}
        tabs={tabs}
        badges={isDefault ? <Badge variant="secondary">{t("MasterData:IsDefault")}</Badge> : undefined}
        extraCommands={
          mode === "edit" && !readOnly && !isDefault
            ? [{ id: "set-default", labelKey: "MasterData:SetAsDefault", icon: Star, groupKey: "MasterData:Currency", disabled: !detail.data?.isActive, onClick: () => setConfirmDefault(true) }]
            : undefined
        }
        deleteRecord={mode === "edit" && canDelete ? remove : undefined}
      />
      <ConfirmDialog
        open={confirmDefault}
        onOpenChange={(next) => {
          if (!next) setConfirmDefault(false);
        }}
        title={t("MasterData:SetAsDefault")}
        description={t("MasterData:SetAsDefaultQuestion").replace("{0}", detail.data?.isoCode ?? "")}
        confirmLabel={t("MasterData:SetAsDefault")}
        onConfirm={makeDefault}
      />
    </>
  );
}
