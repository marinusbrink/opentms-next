import { useCallback, useMemo } from "react";

import { FormField } from "@/components/ui/form-field";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import type { EntityEditorProps } from "@/apps/master-data/components/editor-props";
import { CheckboxField, TextAreaField, TextField } from "@/apps/master-data/components/fields";
import { MasterDataEditor, type EditorTab, type FieldErrors } from "@/apps/master-data/components/MasterDataEditor";
import {
  INBOUND_INVOICE_KINDS,
  inboundInvoiceTypes,
  type InboundInvoiceKind,
  type InboundInvoiceTypeDto,
} from "@/domains/financial/reference-data";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The inbound invoice type editor (design 52, UI design): `sm`; code, name, kind, cost account, archived, remarks.
 * Not numbered: an inbound invoice keeps the supplier's number (Assumption 3). */

interface Values {
  code: string;
  name: string;
  kind: InboundInvoiceKind;
  costLedgerAccount: string;
  isArchived: boolean;
  description: string;
}

const initialValuesFor = (initialName: string | undefined) => (dto: InboundInvoiceTypeDto | undefined): Values => ({
  code: dto?.code ?? "",
  name: dto?.name ?? initialName ?? "",
  kind: dto?.kind ?? 1,
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

export function InboundInvoiceTypeEditor({ mode, id, readOnly, open, onClose, onSaved, initialName }: EntityEditorProps) {
  const { t } = useL();
  const detail = inboundInvoiceTypes.useDetail(mode === "edit" ? id : undefined);
  const initialValues = useMemo(() => initialValuesFor(mode === "create" ? initialName : undefined), [mode, initialName]);
  const create = inboundInvoiceTypes.useCreate();
  const update = inboundInvoiceTypes.useUpdate();
  const remove = inboundInvoiceTypes.useDelete();
  const canDelete = useIsGranted("Financial.InboundInvoiceTypes.Delete");

  const save = useCallback(
    async (v: Values, dto: InboundInvoiceTypeDto | undefined) => {
      const body = {
        code: v.code.trim().toUpperCase(),
        name: v.name.trim(),
        kind: v.kind,
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

  const kindItems = INBOUND_INVOICE_KINDS.map((k) => ({ value: k.value, label: t(k.key) }));

  const tabs: EditorTab<Values>[] = [
    {
      value: "general",
      labelKey: "General",
      fields: ["code", "name", "kind", "costLedgerAccount", "isArchived", "description"],
      render: (form) => (
        <div className="grid grid-cols-2 gap-x-4 gap-y-3">
          <TextField form={form} field="code" labelKey="MasterData:Code" required maxLength={16} upper />
          <TextField form={form} field="name" labelKey="MasterData:Name" required maxLength={128} />
          <FormField label={t("MasterData:Kind")} htmlFor={form.id("kind")} required floating="always">
            <Select items={kindItems} value={form.values.kind} onValueChange={(next) => form.set("kind", (next as InboundInvoiceKind | null) ?? 1)} disabled={form.readOnly}>
              <SelectTrigger id={form.id("kind")} className="h-10 w-full bg-background">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {kindItems.map((item) => (
                  <SelectItem key={item.value} value={item.value}>
                    {item.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </FormField>
          <TextField form={form} field="costLedgerAccount" labelKey="Financial:CostLedgerAccount" maxLength={32} />
          <CheckboxField form={form} field="isArchived" labelKey="MasterData:IsArchived" />
          <TextAreaField form={form} field="description" labelKey="MasterData:Remarks" maxLength={2500} className="col-span-2" />
        </div>
      ),
    },
  ];

  return (
    <MasterDataEditor<Values, InboundInvoiceTypeDto>
      entityKey="InboundInvoiceType"
      resource="Financial"
      queryEntity={inboundInvoiceTypes.entity}
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
  );
}
