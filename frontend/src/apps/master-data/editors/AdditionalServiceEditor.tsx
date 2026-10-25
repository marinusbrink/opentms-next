import { useCallback, useMemo } from "react";

import { FormField } from "@/components/ui/form-field";
import { ReferenceCombobox } from "@/components/ui/reference-combobox";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { useCreateInPlace } from "@/apps/master-data/components/create-in-place";
import type { EntityEditorProps } from "@/apps/master-data/components/editor-props";
import { CheckboxField, TextAreaField, TextField } from "@/apps/master-data/components/fields";
import { MasterDataEditor, type EditorTab, type FieldErrors } from "@/apps/master-data/components/MasterDataEditor";
import {
  SERVICE_UNITS,
  additionalServices,
  vatPercentages,
  type AdditionalServiceDto,
  type ServiceUnit,
} from "@/domains/financial/reference-data";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The additional service editor (design 52, UI design): `sm`; code, name, unit, VAT (empty = the tenant's default VAT
 * percentage), revenue and cost account, archived, remarks. No price: a price is a rate line or a price-list row
 * (AD-08). */

interface Values {
  code: string;
  name: string;
  unit: ServiceUnit;
  vatPercentageId: string | null;
  revenueLedgerAccount: string;
  costLedgerAccount: string;
  isArchived: boolean;
  description: string;
}

const initialValuesFor = (initialName: string | undefined) => (dto: AdditionalServiceDto | undefined): Values => ({
  code: dto?.code ?? "",
  name: dto?.name ?? initialName ?? "",
  unit: dto?.unit ?? 1,
  vatPercentageId: dto?.vatPercentageId ?? null,
  revenueLedgerAccount: dto?.revenueLedgerAccount ?? "",
  costLedgerAccount: dto?.costLedgerAccount ?? "",
  isArchived: dto?.isArchived ?? false,
  description: dto?.description ?? "",
});

const validate = (v: Values): FieldErrors<Values> => {
  const errors: FieldErrors<Values> = {};
  if (!v.code.trim()) errors.code = "MasterData:Required";
  if (!v.name.trim()) errors.name = "MasterData:Required";
  return errors;
};

export function AdditionalServiceEditor({ mode, id, readOnly, open, onClose, onSaved, initialName }: EntityEditorProps) {
  const { t } = useL();
  const detail = additionalServices.useDetail(mode === "edit" ? id : undefined);
  const initialValues = useMemo(() => initialValuesFor(mode === "create" ? initialName : undefined), [mode, initialName]);
  const create = additionalServices.useCreate();
  const update = additionalServices.useUpdate();
  const remove = additionalServices.useDelete();
  const canDelete = useIsGranted("Financial.AdditionalServices.Delete");
  const inPlace = useCreateInPlace();
  const vatLookup = vatPercentages.useLookup(open);
  const vatOptions = useMemo(() => {
    const options = (vatLookup.data ?? []).map((v) => ({ value: v.id ?? "", label: v.name ?? v.code ?? "" }));
    /* An archived category is not in the lookup; the service may still point at it. */
    const current = detail.data;
    if (current?.vatPercentageId && !options.some((o) => o.value === current.vatPercentageId)) {
      options.push({ value: current.vatPercentageId, label: current.vatPercentageName ?? "" });
    }
    return options;
  }, [vatLookup.data, detail.data]);

  const save = useCallback(
    async (v: Values, dto: AdditionalServiceDto | undefined) => {
      const body = {
        code: v.code.trim().toUpperCase(),
        name: v.name.trim(),
        unit: v.unit,
        vatPercentageId: v.vatPercentageId,
        revenueLedgerAccount: v.revenueLedgerAccount.trim() || null,
        costLedgerAccount: v.costLedgerAccount.trim() || null,
        isArchived: v.isArchived,
        description: v.description.trim() || null,
      };
      return mode === "create"
        ? await create.mutateAsync(body)
        : await update.mutateAsync({ id: id!, dto: { ...body, concurrencyStamp: dto?.concurrencyStamp ?? "" } });
    },
    [create, update, mode, id],
  );

  const unitItems = SERVICE_UNITS.map((u) => ({ value: u.value, label: t(u.key) }));

  const tabs: EditorTab<Values>[] = [
    {
      value: "general",
      labelKey: "General",
      fields: ["code", "name", "unit", "vatPercentageId", "revenueLedgerAccount", "costLedgerAccount", "isArchived", "description"],
      render: (form) => (
        <div className="grid grid-cols-2 gap-x-4 gap-y-3">
          <TextField form={form} field="code" labelKey="MasterData:Code" required maxLength={16} upper />
          <TextField form={form} field="name" labelKey="MasterData:Name" required maxLength={128} />
          <FormField label={t("Financial:Unit")} htmlFor={form.id("unit")} required floating="always">
            <Select items={unitItems} value={form.values.unit} onValueChange={(next) => form.set("unit", (next as ServiceUnit | null) ?? 1)} disabled={form.readOnly}>
              <SelectTrigger id={form.id("unit")} className="h-10 w-full bg-background">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {unitItems.map((item) => (
                  <SelectItem key={item.value} value={item.value}>
                    {item.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </FormField>
          <FormField label={t("Financial:Vat")} htmlFor={form.id("vatPercentageId")} floating="always">
            {/* DEVIATION(UI-LOOK-001): VAT categories use ReferenceCombobox over their lookup (under 500 per tenant), approved in docs/designs/52-financial-master-data.md */}
            <ReferenceCombobox
              id={form.id("vatPercentageId")}
              value={form.values.vatPercentageId}
              onChange={(next) => form.set("vatPercentageId", next)}
              options={vatOptions}
              placeholder={t("Financial:VatDefaultPlaceholder")}
              disabled={form.readOnly}
              {...inPlace.offer("VatPercentage", form.readOnly, (r) => form.set("vatPercentageId", r.id))}
            />
          </FormField>
          <TextField form={form} field="revenueLedgerAccount" labelKey="Financial:RevenueLedgerAccount" maxLength={32} />
          <TextField form={form} field="costLedgerAccount" labelKey="Financial:CostLedgerAccount" maxLength={32} />
          <CheckboxField form={form} field="isArchived" labelKey="MasterData:IsArchived" className="col-span-2" />
          <TextAreaField form={form} field="description" labelKey="MasterData:Remarks" maxLength={2500} className="col-span-2" />
        </div>
      ),
    },
  ];

  return (
    <>
      <MasterDataEditor<Values, AdditionalServiceDto>
        entityKey="AdditionalService"
        resource="Financial"
        queryEntity={additionalServices.entity}
        size="sm"
        mode={mode}
        readOnly={readOnly}
        open={open}
        onClose={onClose}
        onSaved={onSaved}
        detail={detail}
        titleOf={(dto) => dto.name ?? dto.code ?? ""}
        initialValues={initialValues}
        validate={validate}
        save={save}
        isSaving={create.isPending || update.isPending}
        tabs={tabs}
        deleteRecord={mode === "edit" && canDelete ? remove : undefined}
      />
      {inPlace.dialog}
    </>
  );
}
