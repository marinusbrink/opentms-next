import { useCallback, useMemo } from "react";

import { Badge } from "@/components/ui/badge";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import type { EntityEditorProps } from "@/apps/master-data/components/editor-props";
import { CheckboxField, TextAreaField, TextField, parseInteger } from "@/apps/master-data/components/fields";
import { MasterDataEditor, type EditorTab, type FieldErrors } from "@/apps/master-data/components/MasterDataEditor";
import {
  NUMBERING_FIELDS,
  NumberingFields,
  numberingDtoOf,
  numberingValuesOf,
  validateNumbering,
  type NumberingFormValues,
} from "@/apps/master-data/components/numbering-fields";
import { resolveLocalizedName, withTextForCulture } from "@/domains/master-data/localized-name";
import {
  OUTBOUND_INVOICE_KINDS,
  outboundInvoiceTypes,
  type OutboundInvoiceKind,
  type OutboundInvoiceTypeDto,
} from "@/domains/financial/reference-data";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The outbound invoice type editor (design 52, UI design): `md`; General (code, name, kind, archived), Numbering
 * (NumberingFields, gapless per design 50), Remarks, Record identification. The default of its kind keeps its kind and
 * cannot be archived. */

interface Values extends NumberingFormValues {
  code: string;
  name: string;
  kind: OutboundInvoiceKind;
  isArchived: boolean;
  description: string;
}

/* A new type starts as the seeded invoice type does (design 52): F, year, "-", five digits. */
const NEW_NUMBERING = { prefix: "F", includeYear: true, includeMonth: false, separator: "-", digits: 5 };

const initialValuesFor = (culture: string, initialName: string | undefined) => (dto: OutboundInvoiceTypeDto | undefined): Values => ({
  code: dto?.code ?? "",
  name: dto ? resolveLocalizedName(dto.names, culture) : (initialName ?? ""),
  kind: dto?.kind ?? 1,
  isArchived: dto?.isArchived ?? false,
  description: dto?.description ?? "",
  ...numberingValuesOf(dto?.numbering, dto?.nextNumber, NEW_NUMBERING),
});

export function OutboundInvoiceTypeEditor({ mode, id, readOnly, open, onClose, onSaved, initialName }: EntityEditorProps) {
  const { t, culture } = useL();
  const detail = outboundInvoiceTypes.useDetail(mode === "edit" ? id : undefined);
  const initialValues = useMemo(() => initialValuesFor(culture, mode === "create" ? initialName : undefined), [culture, mode, initialName]);
  const create = outboundInvoiceTypes.useCreate();
  const update = outboundInvoiceTypes.useUpdate();
  const remove = outboundInvoiceTypes.useDelete();
  const canDelete = useIsGranted("Financial.OutboundInvoiceTypes.Delete");
  const minimumNextNumber = Number(detail.data?.nextNumber ?? 1);
  const isDefault = detail.data?.isDefault === true;

  const validate = useCallback(
    (v: Values): FieldErrors<Values> => {
      const errors: FieldErrors<Values> = { ...validateNumbering(v, minimumNextNumber) };
      if (!v.code.trim()) errors.code = "MasterData:Required";
      if (!v.name.trim()) errors.name = "MasterData:Required";
      if (v.isArchived && isDefault) errors.isArchived = "MasterData:ArchivedCannotBeDefault";
      return errors;
    },
    [minimumNextNumber, isDefault],
  );

  const save = useCallback(
    async (v: Values, dto: OutboundInvoiceTypeDto | undefined) => {
      const body = {
        code: v.code.trim().toUpperCase(),
        names: withTextForCulture(dto?.names, culture, v.name),
        kind: v.kind,
        numbering: numberingDtoOf(v),
        nextNumber: parseInteger(v.nextNumber),
        isArchived: v.isArchived,
        description: v.description.trim() || null,
      };
      return mode === "create"
        ? await create.mutateAsync(body)
        : await update.mutateAsync({ id: id!, dto: { ...body, concurrencyStamp: dto?.concurrencyStamp ?? "" } });
    },
    [create, update, mode, id, culture],
  );

  const kindItems = OUTBOUND_INVOICE_KINDS.map((k) => ({ value: k.value, label: t(k.key) }));

  const tabs: EditorTab<Values>[] = [
    {
      value: "general",
      labelKey: "General",
      fields: ["code", "name", "kind", "isArchived"],
      render: (form) => (
        <FormSection title={t("Financial:OutboundInvoiceType")}>
          <div className="grid grid-cols-2 gap-x-4 gap-y-3">
            <TextField form={form} field="code" labelKey="MasterData:Code" required maxLength={16} upper />
            <TextField form={form} field="name" labelKey="MasterData:Name" required maxLength={128} />
            <FormField label={t("MasterData:Kind")} htmlFor={form.id("kind")} required floating="always">
              <Select
                items={kindItems}
                value={form.values.kind}
                onValueChange={(next) => form.set("kind", (next as OutboundInvoiceKind | null) ?? 1)}
                /* The default of its kind keeps its kind (server: DefaultRequired). */
                disabled={form.readOnly || isDefault}
              >
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
            <CheckboxField form={form} field="isArchived" labelKey="MasterData:IsArchived" />
          </div>
        </FormSection>
      ),
    },
    {
      value: "numbering",
      labelKey: "MasterData:Numbering",
      fields: [...NUMBERING_FIELDS],
      render: (form) => <NumberingFields form={form} />,
    },
    {
      value: "remarks",
      labelKey: "MasterData:Remarks",
      fields: ["description"],
      render: (form) => <TextAreaField form={form} field="description" labelKey="MasterData:Remarks" maxLength={2500} />,
    },
  ];

  return (
    <MasterDataEditor<Values, OutboundInvoiceTypeDto>
      entityKey="OutboundInvoiceType"
      resource="Financial"
      queryEntity={outboundInvoiceTypes.entity}
      size="md"
      mode={mode}
      readOnly={readOnly}
      open={open}
      onClose={onClose}
      onSaved={onSaved}
      detail={detail}
      titleOf={(dto) => resolveLocalizedName(dto.names, culture) || dto.code || ""}
      initialValues={initialValues}
      validate={validate}
      save={save}
      isSaving={create.isPending || update.isPending}
      tabs={tabs}
      badges={isDefault ? <Badge variant="secondary">{t("MasterData:IsDefault")}</Badge> : undefined}
      deleteRecord={mode === "edit" && canDelete && !isDefault ? remove : undefined}
    />
  );
}
