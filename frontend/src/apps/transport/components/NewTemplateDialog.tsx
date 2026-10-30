import { useMemo, useRef, useState } from "react";
import { Save } from "lucide-react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { EntityDialog } from "@/components/ui/entity-dialog";
import { ComboField, TextField } from "@/apps/transport/components/fields";
import { RelationField } from "@/domains/master-data/components/RelationFields";
import { useCreateInPlace } from "@/domains/master-data/components/create-in-place";
import type { RelationOption } from "@/domains/master-data/entities";
import { useCreateTemplate } from "@/domains/orders/templates";
import { useTransportOrderTypeRows } from "@/domains/orders/transport-orders";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useFormState, type FieldErrors } from "@/lib/forms/use-form-state";
import { toIsoDate } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { useOpenRecord } from "@/lib/use-open-record";

/* The New template dialog (design 61 "Templates"): the minimum fields of a template; the Templates list opens the
 * template's page after the save, the "New from template" wizard's picker selects it (UI-LOOK-004). */

interface NewTemplateValues {
  name: string;
  transportOrderTypeId: string | null;
  customer: RelationOption | null;
}

const validateNewTemplate = (v: NewTemplateValues): FieldErrors<NewTemplateValues> => {
  const errors: FieldErrors<NewTemplateValues> = {};
  if (!v.name.trim()) errors.name = "MasterData:Required";
  if (!v.transportOrderTypeId) errors.transportOrderTypeId = "MasterData:Required";
  if (!v.customer) errors.customer = "MasterData:Required";
  return errors;
};

export interface NewTemplateDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** The name a picker's "Add new template…" was typed with (UI-LOOK-004). */
  initialName?: string;
  /** Given by a picker: the created template is handed back (and selected there) instead of opening its page. */
  onCreated?: (template: { id: string; name: string }) => void;
}

/** New template: the minimum fields, then the page (as New order); from a picker, the template is handed back. */
export function NewTemplateDialog({ open, onOpenChange, initialName, onCreated }: NewTemplateDialogProps) {
  const { t } = useL();
  const openRecord = useOpenRecord();
  const create = useCreateTemplate();
  const types = useTransportOrderTypeRows(open);
  const typeOptions = useMemo(() => (types.data ?? []).map((row) => ({ value: row.id ?? "", label: row.name ?? row.code ?? "" })), [types.data]);
  const [error, setError] = useState<string | null>(null);
  const formRef = useRef<HTMLFormElement>(null);
  const initial = useMemo<NewTemplateValues>(() => ({ name: initialName ?? "", transportOrderTypeId: (types.data ?? []).find((row) => row.isDefault)?.id ?? null, customer: null }), [types.data, initialName]);
  const { form, isDirty, submit } = useFormState<NewTemplateValues>(initial, validateNewTemplate, false);
  const inPlace = useCreateInPlace();

  const save = async () => {
    const values = submit(formRef.current);
    if (!values) return;
    setError(null);
    try {
      const created = await create.mutateAsync({
        name: values.name.trim(),
        transportOrderTypeId: values.transportOrderTypeId!,
        customerRelationId: values.customer!.id,
        isActive: true,
        schedule: { recurrence: 0, interval: 1, daysOfWeek: 0, horizonDays: 5, validFrom: toIsoDate(new Date()) },
      });
      toast.success(t("Orders:TransportOrderTemplateSaved"));
      onOpenChange(false);
      if (!created.id) return;
      if (onCreated) onCreated({ id: created.id, name: created.name ?? values.name.trim() });
      else openRecord("TransportOrderTemplate", created.id);
    } catch (err) {
      setError(getApiErrorMessage(err, t));
    }
  };

  return (
    <EntityDialog
      open={open}
      onOpenChange={onOpenChange}
      variant="editor"
      size="sm"
      title={t("Orders:NewTransportOrderTemplate")}
      isDirty={isDirty}
      isBusy={create.isPending}
      commands={[{ id: "save", labelKey: create.isPending ? "Saving" : "Save", icon: Save, isPrimary: true, onClick: () => void save() }]}
    >
      <form
        ref={formRef}
        noValidate
        onSubmit={(e) => {
          e.preventDefault();
          void save();
        }}
        className="grid grid-cols-1 gap-3"
      >
        {error && (
          <Alert variant="destructive">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}
        <TextField form={form} field="name" labelKey="Orders:Name" required maxLength={128} />
        <ComboField
          form={form}
          field="transportOrderTypeId"
          labelKey="Orders:TransportOrderType"
          required
          options={typeOptions}
          {...inPlace.offer("TransportOrderType", create.isPending, (r) => form.set("transportOrderTypeId", r.id))}
        />
        <RelationField id={form.id("customer")} label={t("Orders:Customer")} value={form.values.customer} onChange={(next) => form.set("customer", next)} roles={1} required error={form.error("customer")} />
        <button type="submit" className="hidden" aria-hidden="true" tabIndex={-1} />
      </form>
      {inPlace.dialog}
    </EntityDialog>
  );
}
