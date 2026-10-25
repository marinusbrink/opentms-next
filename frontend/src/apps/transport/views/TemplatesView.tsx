import { useMemo, useRef, useState } from "react";
import type { ColDef } from "ag-grid-community";
import { CirclePlay, Plus, Save, Trash2 } from "lucide-react";
import { toast } from "sonner";

import { useAppFeatures } from "@/app/app-features";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { AppCommandBar, type AppCommandBarCommand } from "@/components/ui/app-command-bar";
import { Checkbox } from "@/components/ui/checkbox";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { EntityDialog } from "@/components/ui/entity-dialog";
import { FeatureUnavailable } from "@/components/ui/feature-unavailable";
import { BoolCell, DateCell, NumberCell } from "@/components/ui/grid-cells";
import type { GridSelectionDto } from "@/components/ui/opentms-grid";
import { LazyOpenTmsGrid } from "@/components/ui/opentms-grid-lazy";
import { PermissionDenied } from "@/components/ui/permission-denied";
import { ComboField, TextField } from "@/apps/transport/components/fields";
import { RelationField } from "@/apps/transport/components/RelationFields";
import { MaterialiseWizard, type MaterialiseSource } from "@/apps/transport/wizards/MaterialiseWizard";
import type { RelationOption } from "@/domains/master-data/entities";
import { RECURRENCE_DAYS, TEMPLATE_RECURRENCES, useCreateTemplate, useDeleteTemplate, useTemplatesGridFetcher, type TemplateRow } from "@/domains/orders/templates";
import { enumKey, useTransportOrderTypeRows } from "@/domains/orders/transport-orders";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useFormState, type FieldErrors } from "@/lib/forms/use-form-state";
import { toIsoDate } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { useOpenRecord } from "@/lib/use-open-record";

/* Design 61, "Templates": the templates list (name, type, customer, recurrence, next date, active) with New (a small
 * editor dialog for the minimum fields that opens the template page), Create orders (the wizard) and Delete; the
 * editor is the order editor in template mode (TemplatePage). */
export function TemplatesView() {
  const { t } = useL();
  const features = useAppFeatures();
  const openRecord = useOpenRecord();
  const canView = useIsGranted("Orders.TransportOrderTemplates");
  const canCreate = useIsGranted("Orders.TransportOrderTemplates.Create");
  const canDelete = useIsGranted("Orders.TransportOrderTemplates.Delete");
  const canCreateOrders = useIsGranted("Orders.TransportOrders.Create");
  const [includeInactive, setIncludeInactive] = useState(false);
  const [selection, setSelection] = useState<GridSelectionDto | null>(null);
  const [rowsById, setRowsById] = useState<Map<string, TemplateRow>>(new Map());
  const [dialog, setDialog] = useState<"new" | "materialise" | "delete" | null>(null);
  const [refreshKey, setRefreshKey] = useState(0);
  const remove = useDeleteTemplate();

  const baseFetch = useTemplatesGridFetcher({ includeInactive });
  const fetchRows = useMemo(
    () => async (request: Parameters<typeof baseFetch>[0]) => {
      const page = await baseFetch(request);
      setRowsById((prev) => {
        const next = new Map(prev);
        page.rows.forEach((row) => row.id && next.set(row.id, row));
        return next;
      });
      return page;
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [baseFetch, refreshKey],
  );

  const columns = useMemo<ColDef<TemplateRow>[]>(
    () => [
      { field: "name", headerName: t("Orders:Name"), flex: 1, minWidth: 180 },
      { field: "transportOrderTypeName", headerName: t("Orders:TransportOrderType"), minWidth: 140, sortable: false },
      { field: "customerName", headerName: t("Orders:Customer"), flex: 1, minWidth: 160 },
      { field: "recurrence", headerName: t("Orders:Recurrence"), minWidth: 110, valueFormatter: (p) => t(enumKey(TEMPLATE_RECURRENCES, p.value as number)) },
      { colId: "days", headerName: t("Orders:DaysOfWeek"), minWidth: 140, sortable: false, valueGetter: (p) => (p.data?.recurrence === 2 ? String(p.data?.dayOfMonth ?? "") : RECURRENCE_DAYS.filter((d) => (((p.data?.daysOfWeek as number | undefined) ?? 0) & d.flag) !== 0).map((d) => t(d.key).slice(0, 2)).join(" ")) },
      { field: "lastMaterialisedDate", headerName: t("Orders:LastMaterialisedDate"), minWidth: 120, cellRenderer: DateCell },
      { field: "lineCount", headerName: t("Orders:LineCount"), minWidth: 80, cellRenderer: NumberCell, type: "rightAligned" },
      { field: "isActive", headerName: t("Orders:IsActive"), minWidth: 90, cellRenderer: BoolCell },
      { field: "interval", headerName: t("Orders:Interval"), minWidth: 90, hide: true, cellRenderer: NumberCell, type: "rightAligned" },
      { field: "horizonDays", headerName: t("Orders:HorizonDays"), minWidth: 90, hide: true, cellRenderer: NumberCell, type: "rightAligned" },
      { field: "validFrom", headerName: t("Orders:ValidFrom"), minWidth: 110, hide: true, cellRenderer: DateCell },
      { field: "validTill", headerName: t("Orders:ValidTill"), minWidth: 110, hide: true, cellRenderer: DateCell },
      { field: "hasTripRequest", headerName: t("Orders:TripRequest"), minWidth: 90, hide: true, cellRenderer: BoolCell },
      { field: "description", headerName: t("MasterData:Remarks"), minWidth: 160, hide: true, sortable: false },
    ],
    [t],
  );

  if (features.isLoaded && !features.isFeatureEnabled("Apps.Transport.Orders.Templates")) return <FeatureUnavailable />;
  if (!canView) return <PermissionDenied />;

  const selectedIds = selection?.mode === "Explicit" ? selection.explicitIds : [];
  const singleId = selectedIds.length === 1 ? selectedIds[0] : undefined;
  const single = singleId ? rowsById.get(singleId) : undefined;
  const source: MaterialiseSource | undefined = single ? { kind: "template", id: single.id!, label: single.name ?? "" } : undefined;

  const commands: AppCommandBarCommand[] = [
    ...(canCreate ? [{ id: "new", labelKey: "Orders:NewTransportOrderTemplate", icon: Plus, isPrimary: true, onClick: () => setDialog("new") }] : []),
    ...(canCreateOrders ? [{ id: "materialise", labelKey: "Orders:Materialise", icon: CirclePlay, requiresSelection: true, disabled: !single || single.isActive === false, disabledReasonKey: single && single.isActive === false ? "OpenTms.Orders:TemplateInactive" : "Orders:SelectOneTemplate", onClick: () => setDialog("materialise") }] : []),
    ...(canDelete ? [{ id: "delete", labelKey: "Delete", icon: Trash2, variant: "destructive" as const, requiresSelection: true, disabled: !singleId, disabledReasonKey: "Orders:SelectOneTemplate", onClick: () => setDialog("delete") }] : []),
  ];

  return (
    <div className="flex h-full flex-col" data-slot="templates-view">
      <AppCommandBar commands={commands} selectionCount={selectedIds.length} />
      <div className="min-h-0 flex-1 px-6 pb-6">
        <LazyOpenTmsGrid<TemplateRow>
          gridId="orders.transport.transportOrderTemplates"
          columnDefs={columns}
          fetchRows={fetchRows}
          onSelectionChange={setSelection}
          onOpen={(row) => row.id && openRecord("TransportOrderTemplate", row.id)}
          rowActions={(rows) => [
            { id: "open", labelKey: "Open", onClick: () => rows[0]?.id && openRecord("TransportOrderTemplate", rows[0].id) },
            ...(canCreateOrders ? [{ id: "materialise", labelKey: "Orders:Materialise", icon: CirclePlay, disabled: rows[0]?.isActive === false, disabledReasonKey: "OpenTms.Orders:TemplateInactive", onClick: () => { setSelection({ mode: "Explicit", explicitIds: [rows[0].id!], filterRequest: null, excludedIds: [] }); setDialog("materialise"); } }] : []),
            ...(canDelete ? [{ id: "delete", labelKey: "Delete", icon: Trash2, destructive: true, onClick: () => { setSelection({ mode: "Explicit", explicitIds: [rows[0].id!], filterRequest: null, excludedIds: [] }); setDialog("delete"); } }] : []),
          ]}
          filters={
            <label className="flex items-center gap-2 text-sm" htmlFor="templates-inactive">
              <Checkbox id="templates-inactive" checked={includeInactive} onCheckedChange={(next) => setIncludeInactive(next === true)} />
              {t("Orders:ShowInactive")}
            </label>
          }
        />
      </div>
      <NewTemplateDialog open={dialog === "new"} onOpenChange={(next) => setDialog(next ? "new" : null)} />
      {source && <MaterialiseWizard open={dialog === "materialise"} onOpenChange={(next) => setDialog(next ? "materialise" : null)} source={source} />}
      <ConfirmDialog
        open={dialog === "delete"}
        onOpenChange={(next) => setDialog(next ? "delete" : null)}
        title={t("Orders:DeleteTransportOrderTemplate")}
        description={`${t("Orders:DeleteTransportOrderTemplateQuestion").replace("{0}", single?.name ?? "")} ${t("ThisCannotBeUndone")}`}
        confirmLabel={t("Delete")}
        destructive
        onConfirm={async () => {
          if (!singleId) return;
          try {
            await remove.mutateAsync(singleId);
            toast.success(t("Orders:TransportOrderTemplateDeleted"));
            setDialog(null);
            setSelection(null);
            setRefreshKey((k) => k + 1);
          } catch (err) {
            setDialog(null);
            toast.error(getApiErrorMessage(err, t));
          }
        }}
      />
    </div>
  );
}

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

/** New template: the minimum fields, then the page (as New order). */
function NewTemplateDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const { t } = useL();
  const openRecord = useOpenRecord();
  const create = useCreateTemplate();
  const types = useTransportOrderTypeRows(open);
  const typeOptions = useMemo(() => (types.data ?? []).map((row) => ({ value: row.id ?? "", label: row.name ?? row.code ?? "" })), [types.data]);
  const [error, setError] = useState<string | null>(null);
  const formRef = useRef<HTMLFormElement>(null);
  const initial = useMemo<NewTemplateValues>(() => ({ name: "", transportOrderTypeId: (types.data ?? []).find((row) => row.isDefault)?.id ?? null, customer: null }), [types.data]);
  const { form, isDirty, submit } = useFormState<NewTemplateValues>(initial, validateNewTemplate, false);

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
      if (created.id) openRecord("TransportOrderTemplate", created.id);
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
        <ComboField form={form} field="transportOrderTypeId" labelKey="Orders:TransportOrderType" required options={typeOptions} />
        <RelationField id={form.id("customer")} label={t("Orders:Customer")} value={form.values.customer} onChange={(next) => form.set("customer", next)} roles={1} required error={form.error("customer")} />
        <button type="submit" className="hidden" aria-hidden="true" tabIndex={-1} />
      </form>
    </EntityDialog>
  );
}
