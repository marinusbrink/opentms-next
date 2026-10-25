import { useCallback, useMemo } from "react";

import { FormSection } from "@/components/ui/form-section";
import type { EntityEditorProps } from "@/apps/master-data/components/editor-props";
import { CheckboxField, TextAreaField, TextField } from "@/apps/master-data/components/fields";
import { MasterDataEditor, type EditorTab, type FieldErrors } from "@/apps/master-data/components/MasterDataEditor";
import { resolveLocalizedName, withTextForCulture } from "@/domains/master-data/localized-name";
import { parseTimeOfDay, timeOfDay, timeSlots, type TimeSlotDto } from "@/domains/orders/reference-data";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The time slot editor (design 51, UI design): `sm`; Code, Name, From, Till (HH:mm, UI-FMT-004: typed, validated on
 * blur), an "ends the next day" hint when Till is before From, Archived and Remarks. The times are local at the stop. */

interface Values {
  code: string;
  name: string;
  startTime: string;
  endTime: string;
  isArchived: boolean;
  description: string;
}

const initialValuesFor = (culture: string, initialName: string | undefined) => (dto: TimeSlotDto | undefined): Values => ({
  code: dto?.code ?? "",
  name: dto ? resolveLocalizedName(dto.names, culture) : (initialName ?? ""),
  startTime: timeOfDay(dto?.startTime),
  endTime: timeOfDay(dto?.endTime),
  isArchived: dto?.isArchived ?? false,
  description: dto?.description ?? "",
});

const validate = (v: Values): FieldErrors<Values> => {
  const errors: FieldErrors<Values> = {};
  if (!v.code.trim()) errors.code = "MasterData:Required";
  if (!v.name.trim()) errors.name = "MasterData:Required";
  const start = parseTimeOfDay(v.startTime);
  const end = parseTimeOfDay(v.endTime);
  if (!v.startTime.trim()) errors.startTime = "MasterData:Required";
  else if (!start) errors.startTime = "Orders:InvalidTime";
  if (!v.endTime.trim()) errors.endTime = "MasterData:Required";
  else if (!end) errors.endTime = "Orders:InvalidTime";
  else if (start && start === end) errors.endTime = "OpenTms.Orders:EmptyTimeSlot";
  return errors;
};

export function TimeSlotEditor({ mode, id, readOnly, open, onClose, onSaved, initialName }: EntityEditorProps) {
  const { t, culture } = useL();
  const detail = timeSlots.useDetail(mode === "edit" ? id : undefined);
  const initialValues = useMemo(() => initialValuesFor(culture, mode === "create" ? initialName : undefined), [culture, mode, initialName]);
  const create = timeSlots.useCreate();
  const update = timeSlots.useUpdate();
  const remove = timeSlots.useDelete();
  const canDelete = useIsGranted("Orders.TimeSlots.Delete");

  const save = useCallback(
    async (v: Values, dto: TimeSlotDto | undefined) => {
      const body = {
        code: v.code.trim().toUpperCase(),
        names: withTextForCulture(dto?.names, culture, v.name),
        startTime: parseTimeOfDay(v.startTime)!,
        endTime: parseTimeOfDay(v.endTime)!,
        isArchived: v.isArchived,
        description: v.description.trim() || null,
      };
      return mode === "create"
        ? await create.mutateAsync(body)
        : await update.mutateAsync({ id: id!, dto: { ...body, concurrencyStamp: dto?.concurrencyStamp ?? "" } });
    },
    [create, update, mode, id, culture],
  );

  const tabs: EditorTab<Values>[] = [
    {
      value: "general",
      labelKey: "General",
      fields: ["code", "name", "startTime", "endTime", "isArchived", "description"],
      render: (form) => {
        const start = parseTimeOfDay(form.values.startTime);
        const end = parseTimeOfDay(form.values.endTime);
        const overnight = start != null && end != null && end < start;
        return (
          <FormSection title={t("Orders:TimeSlot")}>
            <div className="grid grid-cols-2 gap-x-4 gap-y-3">
              <TextField form={form} field="code" labelKey="MasterData:Code" required maxLength={16} upper />
              <TextField form={form} field="name" labelKey="MasterData:Name" required maxLength={128} />
              <TextField form={form} field="startTime" labelKey="Orders:StartTime" required maxLength={5} />
              <TextField form={form} field="endTime" labelKey="Orders:EndTime" required maxLength={5} />
              {overnight && <p className="col-span-2 text-label text-muted-foreground">{t("Orders:EndsNextDay")}</p>}
              <CheckboxField form={form} field="isArchived" labelKey="MasterData:IsArchived" />
              <TextAreaField form={form} field="description" labelKey="MasterData:Remarks" maxLength={2500} className="col-span-2" />
            </div>
          </FormSection>
        );
      },
    },
  ];

  return (
    <MasterDataEditor<Values, TimeSlotDto>
      entityKey="TimeSlot"
      resource="Orders"
      queryEntity={timeSlots.entity}
      size="sm"
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
      deleteRecord={mode === "edit" && canDelete ? remove : undefined}
    />
  );
}
