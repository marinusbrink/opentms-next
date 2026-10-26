import { useMemo, useState } from "react";
import type { ColDef } from "ag-grid-community";
import { CheckCheck, CircleX, Copy, LayoutTemplate, Plus, Printer, Trash2 } from "lucide-react";
import { toast } from "sonner";

import { useAppFeatures } from "@/app/app-features";
import { AppCommandBar, type AppCommandBarCommand } from "@/components/ui/app-command-bar";
import { CalculatingIndicator } from "@/components/ui/calculating-indicator";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { DatePicker } from "@/components/ui/date-picker";
import { EntityLookup } from "@/components/ui/entity-lookup";
import { FeatureUnavailable } from "@/components/ui/feature-unavailable";
import { GridExportDialog } from "@/components/ui/grid-export-dialog";
import { AmountCell, DateCell, NumberCell } from "@/components/ui/grid-cells";
import { MultiSelect } from "@/components/ui/multi-select";
import { LazyOpenTmsGrid } from "@/components/ui/opentms-grid-lazy";
import type { GridExportContext, GridSelectionDto } from "@/components/ui/opentms-grid";
import { PermissionDenied } from "@/components/ui/permission-denied";
import { StatusCell } from "@/components/ui/status-badge";
import { TreeSelect, buildTree } from "@/components/ui/tree-select";
import { ApproveDialog } from "@/apps/transport/orders/ApproveDialog";
import { NewOrderDialog } from "@/apps/transport/orders/NewOrderDialog";
import { TripRequestWarningCell } from "@/apps/transport/components/TripRequestWarning";
import { MaterialiseWizard, type MaterialiseSource } from "@/apps/transport/wizards/MaterialiseWizard";
import { PrintCmrWizard, type PrintableOrder } from "@/apps/transport/wizards/PrintCmrWizard";
import { relationLookupPage, type RelationOption } from "@/domains/master-data/entities";
import {
  EXECUTION_PROGRESSES,
  ORDER_VIEWS,
  TRANSPORT_ORDER_STATUSES,
  TRANSPORT_ORDER_TARGET_TYPE,
  enumKey,
  exportTransportOrders,
  orderStatusName,
  useBulkDeleteTransportOrders,
  useCancelTransportOrder,
  useTransportOrder,
  useTransportOrderTypeRows,
  useTransportOrdersGridFetcher,
  type TransportOrderListFilters,
  type TransportOrderRow,
  type TransportOrderStatus,
} from "@/domains/orders/transport-orders";
import { useAllOrganizationUnits } from "@/domains/platform/administration-organization-units";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { useDerivedValueStates, useRecordUpdates } from "@/lib/realtime/use-record-updates";
import { useOpenRecord } from "@/lib/use-open-record";

export interface OrdersViewProps {
  /** The saved filter from the URL (today, tomorrow, this-week, open, drafts, all; UI-PAGE-004). */
  filter?: string;
}

/* Design 61, "Orders list": a command bar on a full-height server-side OpenTmsGrid (UI-PAGE-001) with the saved filter
 * mapped onto the API's view, the toolbar filters (date range for All, status, customer, organisation unit), the
 * designed columns with CalculatingIndicator on the derived ones while a recalculation is pending (flow 2), the
 * selection-aware command bar (New per type, New from template, Copy, Print CMR, Approve, Cancel, Delete) and the row
 * actions; double click opens the record page through useOpenRecord. Export (UI-GRID-013) sends the list's request with the
 * chosen columns and rows to the server export and saves the xlsx. */
export function OrdersView({ filter }: OrdersViewProps) {
  const { t } = useL();
  const features = useAppFeatures();
  const openRecord = useOpenRecord();
  const canView = useIsGranted("Orders.TransportOrders");
  const canCreate = useIsGranted("Orders.TransportOrders.Create");
  const canDelete = useIsGranted("Orders.TransportOrders.Delete");
  const canCancel = useIsGranted("Orders.TransportOrders.Cancel");
  const canApprove = useIsGranted("Orders.TransportOrders.Approve");
  const canPrint = useIsGranted("Orders.TransportOrders.Print");
  const canTemplates = useIsGranted("Orders.TransportOrderTemplates");

  const [statuses, setStatuses] = useState<TransportOrderStatus[]>([]);
  const [dateFrom, setDateFrom] = useState<string | null>(null);
  const [dateTo, setDateTo] = useState<string | null>(null);
  const [customer, setCustomer] = useState<RelationOption | null>(null);
  const [unitId, setUnitId] = useState<string | null>(null);
  const [selection, setSelection] = useState<GridSelectionDto | null>(null);
  const [visibleIds, setVisibleIds] = useState<string[]>([]);
  const [dialog, setDialog] = useState<"new" | "template" | "copy" | "print" | "approve" | "cancel" | "delete" | null>(null);
  const [newTypeId, setNewTypeId] = useState<string | undefined>(undefined);
  const [refreshKey, setRefreshKey] = useState(0);
  const [exportContext, setExportContext] = useState<GridExportContext | null>(null);

  const view = ORDER_VIEWS[filter ?? "today"] ?? ORDER_VIEWS.today;
  const filters = useMemo<TransportOrderListFilters>(
    () => ({ view, status: statuses, dateFrom: view === ORDER_VIEWS.all ? (dateFrom ?? undefined) : undefined, dateTo: view === ORDER_VIEWS.all ? (dateTo ?? undefined) : undefined, customerRelationId: customer?.id, organizationUnitId: unitId ?? undefined }),
    [view, statuses, dateFrom, dateTo, customer?.id, unitId],
  );
  const baseFetch = useTransportOrdersGridFetcher(filters);
  const fetchRows = useMemo(
    () => async (request: Parameters<typeof baseFetch>[0]) => {
      const page = await baseFetch(request);
      setVisibleIds((prev) => {
        const next = [...new Set([...prev, ...page.rows.map((row) => row.id!).filter(Boolean)])];
        return next.length === prev.length && next.every((id, i) => id === prev[i]) ? prev : next.slice(-400);
      });
      return page;
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [baseFetch, refreshKey],
  );

  /* The derived columns watch the orders shown; a recalculation refreshes the grid (flow 2). */
  const states = useDerivedValueStates(TRANSPORT_ORDER_TARGET_TYPE, visibleIds);
  useRecordUpdates(TRANSPORT_ORDER_TARGET_TYPE, visibleIds, { queryKeysFor: () => [], onUpdate: () => setRefreshKey((k) => k + 1) });

  const types = useTransportOrderTypeRows(canCreate);
  const units = useAllOrganizationUnits();
  const unitTree = useMemo(() => buildTree(units.data ?? [], (u) => u.id ?? "", (u) => u.parentId, (u) => u.displayName ?? ""), [units.data]);
  const customerPage = useMemo(() => relationLookupPage(1), []);
  const cancelOrder = useCancelTransportOrder();
  const bulkDelete = useBulkDeleteTransportOrders();

  const selectedIds = selection?.mode === "Explicit" ? selection.explicitIds : [];
  /* A select-all over the filter (FilterBased) has no client-side count: the bar shows the bulk commands enabled. */
  const selectionCount = selection ? (selection.mode === "Explicit" ? selection.explicitIds.length : 1) : 0;
  const singleId = selectedIds.length === 1 ? selectedIds[0] : undefined;
  const single = useTransportOrder(dialog === "print" || dialog === "copy" ? singleId : undefined);

  const calculating = (id: string | undefined) => (id ? states.data?.get(id.toLowerCase()) : undefined);
  const derived = (field: keyof TransportOrderRow, headerName: string, cell: typeof AmountCell | typeof NumberCell, extra: Partial<ColDef<TransportOrderRow>> = {}): ColDef<TransportOrderRow> => ({
    field,
    headerName,
    minWidth: 110,
    type: "rightAligned",
    cellRenderer: (p: { value: number | null | undefined; data: TransportOrderRow | undefined }) => (
      <span className="inline-flex items-center justify-end gap-1">
        <CalculatingIndicator state={calculating(p.data?.id)} showLabel={false} />
        {cell({ value: p.value })}
      </span>
    ),
    ...extra,
  });

  const columns = useMemo<ColDef<TransportOrderRow>[]>(
    () => [
      { field: "number", headerName: t("Orders:Number"), minWidth: 130 },
      { field: "status", headerName: t("Orders:Status"), minWidth: 130, cellRenderer: (p: { value: TransportOrderStatus | undefined }) => <StatusCell entity="TransportOrder" value={orderStatusName(p.value)} /> },
      { field: "orderDate", headerName: t("Orders:OrderDate"), minWidth: 110, cellRenderer: DateCell },
      { field: "customerName", headerName: t("Orders:Customer"), flex: 1, minWidth: 160 },
      { field: "customerReference", headerName: t("Orders:CustomerReference"), minWidth: 140 },
      { field: "lineCount", headerName: t("Orders:LineCount"), minWidth: 80, cellRenderer: NumberCell, type: "rightAligned" },
      { colId: "firstStop", headerName: t("Orders:FirstStop"), flex: 1, minWidth: 180, valueGetter: (p) => [p.data?.firstStopName, p.data?.firstStopCity].filter(Boolean).join(", "), sortable: false },
      { field: "firstStopDate", headerName: t("Orders:FirstStopDate"), minWidth: 110, cellRenderer: DateCell },
      { colId: "lastStop", headerName: t("Orders:LastStop"), flex: 1, minWidth: 180, valueGetter: (p) => [p.data?.lastStopName, p.data?.lastStopCity].filter(Boolean).join(", "), sortable: false },
      { field: "lastStopDate", headerName: t("Orders:LastStopDate"), minWidth: 110, cellRenderer: DateCell },
      derived("revenueAmount", t("Orders:RevenueAmount"), AmountCell),
      derived("costOfRevenueAmount", t("Orders:CostOfRevenueAmount"), AmountCell),
      derived("marginAmount", t("Orders:MarginAmount"), AmountCell),
      derived("co2Kg", t("Orders:Co2Kg"), NumberCell),
      { field: "tripNumbers", headerName: t("Orders:TripNumbers"), minWidth: 130, sortable: false },
      { field: "executionProgress", headerName: t("Orders:ExecutionProgress"), minWidth: 130, valueFormatter: (p) => t(enumKey(EXECUTION_PROGRESSES, p.value as number)) },
      /* The trip request Planning could not honour (design 58 build note 19): an icon; not part of the server export. */
      { field: "tripRequestWarningCode", headerName: t("Orders:TripRequestWarning"), minWidth: 90, width: 100, sortable: false, context: { export: false }, cellRenderer: (p: { value: string | null | undefined }) => <TripRequestWarningCell value={p.value} /> },
      { field: "transportOrderTypeName", headerName: t("Orders:TransportOrderType"), minWidth: 140, hide: true, sortable: false },
      { field: "organizationUnitId", headerName: t("Orders:OrganizationUnit"), minWidth: 140, hide: true, valueFormatter: (p) => units.data?.find((u) => u.id === p.value)?.displayName ?? "" },
      { field: "currencyCode", headerName: t("Orders:CurrencyCode"), minWidth: 90, hide: true },
      { field: "stopCount", headerName: t("Orders:StopCount"), minWidth: 80, hide: true, cellRenderer: NumberCell, type: "rightAligned" },
      { field: "source", headerName: t("Orders:Source"), minWidth: 100, hide: true, valueFormatter: (p) => t(enumKey([{ value: 1, key: "Orders:OrderSource.Screen" }, { value: 2, key: "Orders:OrderSource.Template" }, { value: 3, key: "Orders:OrderSource.Agent" }, { value: 4, key: "Orders:OrderSource.Api" }], p.value as number)) },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [t, states.data, units.data],
  );

  if (features.isLoaded && !features.isFeatureEnabled("Apps.Transport.Orders")) return <FeatureUnavailable />;
  if (!canView) return <PermissionDenied />;

  const refresh = () => setRefreshKey((k) => k + 1);
  const commands: AppCommandBarCommand[] = [
    ...(canCreate
      ? [
          { id: "new", labelKey: "Orders:NewTransportOrder", icon: Plus, isPrimary: true, onClick: () => { setNewTypeId(undefined); setDialog("new"); } },
          ...((types.data ?? []).length > 1 ? (types.data ?? []).map((type) => ({ id: `new-${type.id}`, labelKey: type.name ?? type.code ?? "", icon: Plus, onClick: () => { setNewTypeId(type.id); setDialog("new"); } })) : []),
          ...(canTemplates ? [{ id: "from-template", labelKey: "Orders:NewFromTemplate", icon: LayoutTemplate, onClick: () => setDialog("template") }] : []),
          { id: "copy", labelKey: "Orders:CopyOrder", icon: Copy, requiresSelection: true, disabled: !singleId, disabledReasonKey: "Orders:SelectOneOrder", onClick: () => setDialog("copy") },
        ]
      : []),
    ...(canPrint ? [{ id: "print", labelKey: "Orders:PrintConsignmentNote", icon: Printer, requiresSelection: true, disabled: !singleId, disabledReasonKey: "Orders:SelectOneOrder", onClick: () => setDialog("print") }] : []),
    ...(canApprove ? [{ id: "approve", labelKey: "Orders:Approve", icon: CheckCheck, requiresSelection: true, disabled: selectedIds.length === 0, disabledReasonKey: "Orders:SelectOrders", onClick: () => setDialog("approve") }] : []),
    ...(canCancel ? [{ id: "cancel", labelKey: "Orders:Cancel", icon: CircleX, variant: "destructive" as const, requiresSelection: true, disabled: !singleId, disabledReasonKey: "Orders:SelectOneOrder", onClick: () => setDialog("cancel") }] : []),
    ...(canDelete ? [{ id: "delete", labelKey: "Delete", icon: Trash2, variant: "destructive" as const, requiresSelection: true, selectionLabelKeys: { zero: "Delete", one: "Delete", many: "Orders:DeleteN" }, onClick: () => setDialog("delete") }] : []),
  ];

  const printable: PrintableOrder[] = single.data ? [{ id: single.data.id!, number: single.data.number ?? "", lines: (single.data.lines ?? []).map((l, index) => ({ id: l.id!, position: l.position ?? index + 1, label: [l.transportUnitName, l.productName].filter(Boolean).join(" · ") })) }] : [];
  const copySource: MaterialiseSource | undefined = single.data ? { kind: "order", id: single.data.id!, label: `${t("Orders:TransportOrder")} ${single.data.number ?? ""}` } : undefined;

  return (
    <div className="flex h-full flex-col" data-slot="orders-view" data-filter={filter}>
      <AppCommandBar commands={commands} selectionCount={selectionCount} />
      <div className="min-h-0 flex-1 px-6 pb-6">
        <LazyOpenTmsGrid<TransportOrderRow>
          gridId="orders.transport.transportOrders"
          columnDefs={columns}
          fetchRows={fetchRows}
          onSelectionChange={setSelection}
          onExport={setExportContext}
          onOpen={(row) => row.id && openRecord("TransportOrder", row.id)}
          rowActions={(rows) => [
            { id: "open", labelKey: "Open", onClick: () => rows[0]?.id && openRecord("TransportOrder", rows[0].id) },
            ...(canPrint ? [{ id: "print", labelKey: "Orders:PrintConsignmentNote", icon: Printer, onClick: () => { setSelection({ mode: "Explicit", explicitIds: [rows[0].id!], filterRequest: null, excludedIds: [] }); setDialog("print"); } }] : []),
            ...(canCreate ? [{ id: "copy", labelKey: "Orders:CopyOrder", icon: Copy, onClick: () => { setSelection({ mode: "Explicit", explicitIds: [rows[0].id!], filterRequest: null, excludedIds: [] }); setDialog("copy"); } }] : []),
            ...(canCancel ? [{ id: "cancel", labelKey: "Orders:Cancel", icon: CircleX, destructive: true, disabled: rows[0]?.status !== 1 && rows[0]?.status !== 2, disabledReasonKey: "OpenTms.Orders:InvalidTransition", onClick: () => { setSelection({ mode: "Explicit", explicitIds: [rows[0].id!], filterRequest: null, excludedIds: [] }); setDialog("cancel"); } }] : []),
            ...(canDelete ? [{ id: "delete", labelKey: "Delete", icon: Trash2, destructive: true, onClick: () => { setSelection({ mode: "Explicit", explicitIds: rows.map((r) => r.id!), filterRequest: null, excludedIds: [] }); setDialog("delete"); } }] : []),
          ]}
          filters={
            <>
              {view === ORDER_VIEWS.all && (
                <>
                  <DatePicker id="orders-date-from-filter" value={dateFrom} onChange={setDateFrom} placeholder={t("Financial:From")} className="w-36" />
                  <DatePicker id="orders-date-to-filter" value={dateTo} onChange={setDateTo} placeholder={t("Financial:To")} className="w-36" />
                </>
              )}
              <MultiSelect id="orders-status-filter" value={statuses.map(String)} onChange={(next) => setStatuses(next.map(Number) as TransportOrderStatus[])} options={TRANSPORT_ORDER_STATUSES.map((s) => ({ id: String(s.value), label: t(s.key) }))} className="w-44" />
              <EntityLookup<RelationOption> id="orders-customer-filter" value={customer} onChange={setCustomer} fetchPage={customerPage} getLabel={(r) => r.name} placeholder={t("Orders:Customer")} className="w-56" />
              <TreeSelect id="orders-unit-filter" nodes={unitTree} value={unitId} onChange={setUnitId} noneLabel={t("Orders:OrganizationUnit")} className="w-48" />
            </>
          }
        />
      </div>
      <NewOrderDialog open={dialog === "new"} onOpenChange={(next) => setDialog(next ? "new" : null)} typeId={newTypeId} />
      <MaterialiseWizard open={dialog === "template"} onOpenChange={(next) => setDialog(next ? "template" : null)} />
      {copySource && <MaterialiseWizard open={dialog === "copy"} onOpenChange={(next) => setDialog(next ? "copy" : null)} source={copySource} />}
      <PrintCmrWizard open={dialog === "print" && printable.length > 0} onOpenChange={(next) => setDialog(next ? "print" : null)} orders={printable} />
      <ApproveDialog ids={selectedIds} open={dialog === "approve"} onOpenChange={(next) => setDialog(next ? "approve" : null)} onApproved={refresh} />
      <GridExportDialog
        open={exportContext != null}
        onOpenChange={(next) => !next && setExportContext(null)}
        columns={exportContext?.columns ?? []}
        onExport={(choice) => exportTransportOrders(exportContext!.request, filters, choice)}
      />
      <ConfirmDialog
        open={dialog === "cancel"}
        onOpenChange={(next) => setDialog(next ? "cancel" : null)}
        title={t("Orders:Cancel")}
        description={`${t("Orders:CancelTransportOrderQuestion").replace("{0}", "")} ${t("ThisCannotBeUndone")}`}
        confirmLabel={t("Orders:Cancel")}
        destructive
        onConfirm={async () => {
          if (!singleId) return;
          try {
            await cancelOrder.mutateAsync(singleId);
            toast.success(t("Orders:TransportOrderCancelled"));
            setDialog(null);
            refresh();
          } catch (err) {
            setDialog(null);
            toast.error(getApiErrorMessage(err, t));
          }
        }}
      />
      <ConfirmDialog
        open={dialog === "delete"}
        onOpenChange={(next) => setDialog(next ? "delete" : null)}
        title={t("Orders:DeleteTransportOrder")}
        description={`${t("Orders:DeleteNTransportOrdersQuestion").replace("{0}", String(selectionCount))} ${t("ThisCannotBeUndone")}`}
        confirmLabel={t("Delete")}
        destructive
        onConfirm={async () => {
          if (!selection) return;
          try {
            const result = await bulkDelete.mutateAsync({ selection, filters });
            toast.success(t("Orders:OrdersDeleted").replace("{0}", String(result?.deletedCount ?? 0)));
            if ((result?.skippedRows ?? []).length > 0) toast.warning(t("Orders:OrdersSkipped").replace("{0}", String(result!.skippedRows!.length)));
            setDialog(null);
            setSelection(null);
            refresh();
          } catch (err) {
            setDialog(null);
            toast.error(getApiErrorMessage(err, t));
          }
        }}
      />
    </div>
  );
}
