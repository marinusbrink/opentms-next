import { useCallback, useMemo, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Star } from "lucide-react";
import { toast } from "sonner";

import { Badge } from "@/components/ui/badge";
import { CheckboxGroup } from "@/components/ui/checkbox-group";
import type { RibbonCommand } from "@/components/ui/entity-dialog";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { MultiSelect } from "@/components/ui/multi-select";
import { EntityLookup } from "@/components/ui/entity-lookup";
import { useCreateInPlace, withId } from "@/domains/master-data/components/create-in-place";
import type { EntityEditorProps } from "@/components/record-editor/editor-props";
import { CheckboxField, EnumSelectField, NumberField, TextAreaField, TextField, numberError, numberToText, parseInteger } from "@/components/record-editor/fields";
import { MasterDataEditor, type EditorTab, type FieldErrors } from "@/components/record-editor/MasterDataEditor";
import { masterDataQueryKey } from "@/domains/master-data/shared";
import { consignmentNoteFixedDescriptions } from "@/domains/orders/consignment-note-fixed-descriptions";
import {
  CMR_GOODS_COLUMNS,
  CMR_INSTRUCTION_ITEMS,
  CMR_PARTY_BOXES,
  CMR_PARTY_SOURCES,
  CMR_REMARK_ITEMS,
  DEFAULT_COPIES,
  MAX_COPIES,
  MIN_COPIES,
  consignmentNoteProfiles,
  useSetDefaultConsignmentNoteProfile,
  type CmrGoodsColumn,
  type CmrInstructionItem,
  type CmrPartySource,
  type CmrRemarkItem,
  type ConsignmentNoteProfileDto,
} from "@/domains/orders/consignment-note-profiles";
import { CONSIGNMENT_NOTE_PURPOSE, reportDefinitionLookupPage, type ReportDefinitionDto, type ReportDefinitionOption } from "@/domains/platform/master-data";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The CMR profile editor (design 58 "CMR", design 61): `lg`; General (name, default badge, copies 1–5, borders, QR
 * code, group by leg, the report definition of purpose ConsignmentNote or the tenant's default, archived), Boxes (one
 * Select per party box, checkbox groups for boxes 6–12, 13 and 18, the fixed texts as a multi-select), Remarks, Record
 * identification. "Set as default" is a ribbon command on a saved, non-default profile. */

interface Values {
  name: string;
  copies: string;
  showBorders: boolean;
  showQrCode: boolean;
  groupByLeg: boolean;
  reportDefinition: ReportDefinitionOption | null;
  isArchived: boolean;
  description: string;
  box1Sender: CmrPartySource;
  box2Consignee: CmrPartySource;
  box3DeliveryPlace: CmrPartySource;
  box4TakingOverPlace: CmrPartySource;
  box16Carrier: CmrPartySource;
  box17SuccessiveCarrier: CmrPartySource;
  box21EstablishedBy: CmrPartySource;
  box6Columns: string[];
  box13Items: string[];
  box18Items: string[];
  fixedDescriptionIds: string[];
}

const ids = (values: readonly number[] | null | undefined) => (values ?? []).map(String);

const initialValuesFor = (initialName: string | undefined) => (dto: ConsignmentNoteProfileDto | undefined): Values => ({
  name: dto?.name ?? initialName ?? "",
  copies: numberToText(dto?.copies ?? DEFAULT_COPIES),
  showBorders: dto?.showBorders ?? true,
  showQrCode: dto?.showQrCode ?? true,
  groupByLeg: dto?.groupByLeg ?? false,
  reportDefinition: dto?.reportDefinitionId ? { id: dto.reportDefinitionId, name: dto.reportDefinitionName ?? "" } : null,
  isArchived: dto?.isArchived ?? false,
  description: dto?.description ?? "",
  box1Sender: dto?.box1Sender ?? 1,
  box2Consignee: dto?.box2Consignee ?? 3,
  box3DeliveryPlace: dto?.box3DeliveryPlace ?? 3,
  box4TakingOverPlace: dto?.box4TakingOverPlace ?? 2,
  box16Carrier: dto?.box16Carrier ?? 5,
  box17SuccessiveCarrier: dto?.box17SuccessiveCarrier ?? 9,
  box21EstablishedBy: dto?.box21EstablishedBy ?? 4,
  box6Columns: ids(dto?.box6Columns),
  box13Items: ids(dto?.box13Items),
  box18Items: ids(dto?.box18Items),
  fixedDescriptionIds: dto?.fixedDescriptionIds ?? [],
});

const validate = (v: Values): FieldErrors<Values> => {
  const errors: FieldErrors<Values> = {};
  if (!v.name.trim()) errors.name = "MasterData:Required";
  const copies = parseInteger(v.copies) == null ? "MasterData:Required" : numberError(v.copies, { integer: true, min: MIN_COPIES, max: MAX_COPIES });
  if (copies) errors.copies = copies;
  return errors;
};

export function ConsignmentNoteProfileEditor({ mode, id, readOnly, open, onClose, onSaved, initialName }: EntityEditorProps) {
  const { t } = useL();
  const queryClient = useQueryClient();
  const detail = consignmentNoteProfiles.useDetail(mode === "edit" ? id : undefined);
  const initialValues = useMemo(() => initialValuesFor(mode === "create" ? initialName : undefined), [mode, initialName]);
  const create = consignmentNoteProfiles.useCreate();
  const update = consignmentNoteProfiles.useUpdate();
  const remove = consignmentNoteProfiles.useDelete();
  const setDefault = useSetDefaultConsignmentNoteProfile();
  const canDelete = useIsGranted("Orders.ConsignmentNoteProfiles.Delete");
  const inPlace = useCreateInPlace();
  const [commandError, setCommandError] = useState<string | null>(null);

  const fixedLookup = consignmentNoteFixedDescriptions.useLookup(open);
  const definitionPage = useMemo(() => reportDefinitionLookupPage(CONSIGNMENT_NOTE_PURPOSE), []);

  const fixedOptions = useMemo(() => {
    const options = (fixedLookup.data ?? []).map((f) => ({ id: f.id ?? "", label: f.name ?? "" }));
    /* An archived text the profile still names keeps its name (the lookup lists active texts only). */
    (detail.data?.fixedDescriptionIds ?? []).forEach((fixedId, index) => {
      if (!options.some((o) => o.id === fixedId)) options.push({ id: fixedId, label: detail.data?.fixedDescriptionNames?.[index] ?? "" });
    });
    return options;
  }, [fixedLookup.data, detail.data]);

  const sourceItems = useMemo(() => CMR_PARTY_SOURCES.map((s) => ({ value: s.value, label: t(s.key) })), [t]);
  const goodsOptions = useMemo(() => CMR_GOODS_COLUMNS.map((c) => ({ id: String(c.value), label: t(c.key) })), [t]);
  const instructionOptions = useMemo(() => CMR_INSTRUCTION_ITEMS.map((c) => ({ id: String(c.value), label: t(c.key) })), [t]);
  const remarkOptions = useMemo(() => CMR_REMARK_ITEMS.map((c) => ({ id: String(c.value), label: t(c.key) })), [t]);

  const save = useCallback(
    async (v: Values, dto: ConsignmentNoteProfileDto | undefined) => {
      const body = {
        name: v.name.trim(),
        description: v.description.trim() || null,
        copies: parseInteger(v.copies) ?? DEFAULT_COPIES,
        showBorders: v.showBorders,
        showQrCode: v.showQrCode,
        groupByLeg: v.groupByLeg,
        box1Sender: v.box1Sender,
        box2Consignee: v.box2Consignee,
        box3DeliveryPlace: v.box3DeliveryPlace,
        box4TakingOverPlace: v.box4TakingOverPlace,
        box16Carrier: v.box16Carrier,
        box17SuccessiveCarrier: v.box17SuccessiveCarrier,
        box21EstablishedBy: v.box21EstablishedBy,
        box6Columns: v.box6Columns.map(Number) as CmrGoodsColumn[],
        box13Items: v.box13Items.map(Number) as CmrInstructionItem[],
        box18Items: v.box18Items.map(Number) as CmrRemarkItem[],
        fixedDescriptionIds: v.fixedDescriptionIds,
        reportDefinitionId: v.reportDefinition?.id ?? null,
        isArchived: v.isArchived,
      };
      return mode === "create" ? await create.mutateAsync(body) : await update.mutateAsync({ id: id!, dto: { ...body, concurrencyStamp: dto?.concurrencyStamp ?? "" } });
    },
    [create, update, mode, id],
  );

  const handleSetDefault = async () => {
    if (!id) return;
    setCommandError(null);
    try {
      await setDefault.mutateAsync(id);
      await queryClient.invalidateQueries({ queryKey: masterDataQueryKey(consignmentNoteProfiles.entity) });
      toast.success(t("Orders:ConsignmentNoteProfileSaved"));
      onSaved();
    } catch (err) {
      setCommandError(getApiErrorMessage(err, t));
    }
  };

  /* Acts on the SAVED profile: unsaved changes and an archived or already default profile block it (UI-PERM-005). */
  const actionCommands = ({ isDirty }: { isDirty: boolean }): RibbonCommand[] =>
    mode === "edit" && !readOnly
      ? [
          {
            id: "set-default",
            labelKey: "MasterData:SetAsDefault",
            icon: Star,
            groupKey: "MasterData:IsDefault",
            disabled: detail.data == null || detail.data.isDefault === true || detail.data.isArchived === true || isDirty,
            disabledReasonKey: detail.data?.isArchived ? "MasterData:ArchivedCannotBeDefault" : isDirty ? "Platform:SaveChangesFirst" : undefined,
            onClick: () => void handleSetDefault(),
          },
        ]
      : [];

  const tabs: EditorTab<Values>[] = [
    {
      value: "general",
      labelKey: "General",
      fields: ["name", "copies", "showBorders", "showQrCode", "groupByLeg", "reportDefinition", "isArchived"],
      render: (form) => (
        <FormSection title={t("Orders:ConsignmentNoteProfile")}>
          <div className="grid grid-cols-2 gap-x-4 gap-y-3">
            <TextField form={form} field="name" labelKey="MasterData:Name" required maxLength={128} />
            <NumberField form={form} field="copies" labelKey="Orders:Copies" required hintKey="Orders:CopiesHint" />
            <FormField label={t("Orders:ReportDefinition")} htmlFor={form.id("reportDefinition")} floating="always" hint={t("Orders:ReportDefinitionDefaultHint")}>
              <EntityLookup<ReportDefinitionOption>
                id={form.id("reportDefinition")}
                value={form.values.reportDefinition}
                onChange={(next) => form.set("reportDefinition", next)}
                fetchPage={definitionPage}
                getLabel={(d) => d.name}
                disabled={form.readOnly}
                {...inPlace.offer<ReportDefinitionDto>("ReportDefinition", form.readOnly, (r) => {
                  /* A definition of another purpose cannot render a CMR: it is created, not picked. */
                  if (r.dto.purpose === CONSIGNMENT_NOTE_PURPOSE) form.set("reportDefinition", { id: r.id, name: r.name });
                })}
              />
            </FormField>
            <div className="flex flex-wrap gap-x-6">
              <CheckboxField form={form} field="showBorders" labelKey="Orders:ShowBorders" />
              <CheckboxField form={form} field="showQrCode" labelKey="Orders:ShowQrCode" />
              <CheckboxField form={form} field="groupByLeg" labelKey="Orders:GroupByLeg" />
            </div>
            <CheckboxField form={form} field="isArchived" labelKey="MasterData:IsArchived" />
          </div>
        </FormSection>
      ),
    },
    {
      value: "boxes",
      labelKey: "Orders:Boxes",
      fields: ["box1Sender", "box2Consignee", "box3DeliveryPlace", "box4TakingOverPlace", "box16Carrier", "box17SuccessiveCarrier", "box21EstablishedBy", "box6Columns", "box13Items", "box18Items", "fixedDescriptionIds"],
      render: (form) => (
        <>
          <FormSection title={t("Orders:PartyBoxes")}>
            <div className="grid grid-cols-2 gap-x-4 gap-y-3">
              {CMR_PARTY_BOXES.map((box) => (
                <EnumSelectField key={box.field} form={form} field={box.field} labelKey={box.labelKey} items={sourceItems} required />
              ))}
            </div>
          </FormSection>
          <FormSection title={t("Orders:ContentBoxes")}>
            <div className="grid grid-cols-2 gap-x-4 gap-y-3">
              <FormField label={t("Orders:Box6Columns")} htmlFor={form.id("box6Columns")} floating="always" className="row-span-2">
                <CheckboxGroup id={form.id("box6Columns")} value={form.values.box6Columns} onChange={(next) => form.set("box6Columns", next)} options={goodsOptions} columns={2} disabled={form.readOnly} />
              </FormField>
              <FormField label={t("Orders:Box13Items")} htmlFor={form.id("box13Items")} floating="always">
                <CheckboxGroup id={form.id("box13Items")} value={form.values.box13Items} onChange={(next) => form.set("box13Items", next)} options={instructionOptions} disabled={form.readOnly} />
              </FormField>
              <FormField label={t("Orders:FixedDescriptions")} htmlFor={form.id("fixedDescriptionIds")} floating="always" hint={t("Orders:FixedDescriptionsHint")}>
                <MultiSelect
                  id={form.id("fixedDescriptionIds")}
                  value={form.values.fixedDescriptionIds}
                  onChange={(next) => form.set("fixedDescriptionIds", next)}
                  options={fixedOptions}
                  disabled={form.readOnly}
                  {...inPlace.offer("ConsignmentNoteFixedDescription", form.readOnly, (r) => form.update((v) => ({ ...v, fixedDescriptionIds: withId(v.fixedDescriptionIds, r.id) })))}
                />
              </FormField>
              <FormField label={t("Orders:Box18Items")} htmlFor={form.id("box18Items")} floating="always" className="col-span-2">
                <CheckboxGroup id={form.id("box18Items")} value={form.values.box18Items} onChange={(next) => form.set("box18Items", next)} options={remarkOptions} columns={2} disabled={form.readOnly} />
              </FormField>
            </div>
          </FormSection>
        </>
      ),
    },
    {
      value: "remarks",
      labelKey: "MasterData:Remarks",
      fields: ["description"],
      render: (form) => <TextAreaField form={form} field="description" labelKey="MasterData:Remarks" maxLength={2500} />,
    },
  ];

  return (
    <>
      <MasterDataEditor<Values, ConsignmentNoteProfileDto>
        entityKey="ConsignmentNoteProfile"
        resource="Orders"
        queryEntity={consignmentNoteProfiles.entity}
        size="lg"
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
        actionCommands={actionCommands}
        isCommandBusy={setDefault.isPending}
        commandError={commandError}
        badges={detail.data?.isDefault ? <Badge variant="secondary">{t("MasterData:IsDefault")}</Badge> : undefined}
        deleteRecord={mode === "edit" && canDelete && !detail.data?.isDefault ? remove : undefined}
      />
      {inPlace.dialog}
    </>
  );
}
