import { useMemo, useState } from "react";
import type { ColDef, ValueFormatterParams } from "ag-grid-community";
import { Archive, CircleCheck, CircleX, Printer } from "lucide-react";
import { toast } from "sonner";

import { useAppFeatures } from "@/app/app-features";
import { AppCommandBar, type AppCommandBarCommand } from "@/components/ui/app-command-bar";
import { CalculatingIndicator } from "@/components/ui/calculating-indicator";
import { Checkbox } from "@/components/ui/checkbox";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { DatePicker } from "@/components/ui/date-picker";
import { EntityLookup } from "@/components/ui/entity-lookup";
import { FeatureUnavailable } from "@/components/ui/feature-unavailable";
import { AmountCell, BoolCell, DateCell, DateTimeCell, NumberCell, PercentCell } from "@/components/ui/grid-cells";
import { MultiSelect } from "@/components/ui/multi-select";
import type { GridSelectionDto } from "@/components/ui/opentms-grid";
import { LazyOpenTmsGrid } from "@/components/ui/opentms-grid-lazy";
import { PermissionDenied } from "@/components/ui/permission-denied";
import { StatusCell } from "@/components/ui/status-badge";
import { TreeSelect, buildTree } from "@/components/ui/tree-select";
import { WarningsCell } from "@/apps/transport/components/WarningsCell";
import { ArchiveTripsWizard } from "@/apps/transport/wizards/ArchiveTripsWizard";
import { PrintCmrWizard } from "@/apps/transport/wizards/PrintCmrWizard";
import { relationLookupPage, type RelationOption } from "@/domains/master-data/entities";
import { TRIP_EXECUTIONS } from "@/domains/planning-execution/reference-data";
import {
  TRANSPORT_TRIP_STATUSES,
  TRANSPORT_TRIP_TARGET_TYPE,
  TRIP_VIEWS,
  tripStatusName,
  useCancelTrip,
  useCompleteTrip,
  useTransportTripsGridFetcher,
  type TransportTripListFilters,
  type TransportTripRow,
  type TransportTripStatus,
} from "@/domains/planning-execution/trips";
import { useAllOrganizationUnits } from "@/domains/platform/administration-organization-units";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { useDerivedValueStates, useRecordUpdates } from "@/lib/realtime/use-record-updates";
import { useOpenRecord } from "@/lib/use-open-record";

export interface TripsViewProps {
  /** The saved filter from the URL (today, this-week, open, all; UI-PAGE-004). */
  filter?: string;
}

/* Design 61, "Trips list and editor": the trips grid (number, status, date, name, type, carrier, vehicles, drivers,
 * planned departure/arrival, stops, legs, customers, orders, distance, load factor, CO₂, cost, revenue, warnings;
 * archived hidden) with the filters view, date range, status, carrier, organisation unit, hide empty, and the command
 * bar Print CMR, Archive (wizard), Cancel, Complete; no New (the Planboard, PO). Derived columns show the
 * CalculatingIndicator while pending (flow 2). Export waits for a server export endpoint. */
export function TripsView({ filter }: TripsViewProps) {
  const { t } = useL();
  const f = useFormat();
  const features = useAppFeatures();
  const openRecord = useOpenRecord();
  const canView = useIsGranted("PlanningExecution.TransportTrips");
  const canCancel = useIsGranted("PlanningExecution.TransportTrips.Cancel");
  const canComplete = useIsGranted("PlanningExecution.TransportTrips.Complete");
  const canArchive = useIsGranted("PlanningExecution.TransportTrips.Archive");
  const canPrint = useIsGranted("Orders.TransportOrders.Print");

  const [statuses, setStatuses] = useState<TransportTripStatus[]>([]);
  const [dateFrom, setDateFrom] = useState<string | null>(null);
  const [dateTo, setDateTo] = useState<string | null>(null);
  const [carrier, setCarrier] = useState<RelationOption | null>(null);
  const [unitId, setUnitId] = useState<string | null>(null);
  const [hideEmpty, setHideEmpty] = useState(false);
  const [selection, setSelection] = useState<GridSelectionDto | null>(null);
  const [rowsById, setRowsById] = useState<Map<string, TransportTripRow>>(new Map());
  const [visibleIds, setVisibleIds] = useState<string[]>([]);
  const [dialog, setDialog] = useState<"print" | "archive" | "cancel" | "complete" | null>(null);
  const [refreshKey, setRefreshKey] = useState(0);

  const view = TRIP_VIEWS[filter ?? "today"] ?? TRIP_VIEWS.today;
  const filters = useMemo<TransportTripListFilters>(
    () => ({ view, status: statuses, dateFrom: view === TRIP_VIEWS.all ? (dateFrom ?? undefined) : undefined, dateTo: view === TRIP_VIEWS.all ? (dateTo ?? undefined) : undefined, carrierRelationId: carrier?.id, organizationUnitId: unitId ?? undefined, hideEmpty, isArchived: false }),
    [view, statuses, dateFrom, dateTo, carrier?.id, unitId, hideEmpty],
  );
  const baseFetch = useTransportTripsGridFetcher(filters);
  const fetchRows = useMemo(
    () => async (request: Parameters<typeof baseFetch>[0]) => {
      const page = await baseFetch(request);
      setRowsById((prev) => {
        const next = new Map(prev);
        page.rows.forEach((row) => row.id && next.set(row.id, row));
        return next;
      });
      setVisibleIds((prev) => {
        const next = [...new Set([...prev, ...page.rows.map((row) => row.id!).filter(Boolean)])].slice(-400);
        return next.length === prev.length && next.every((id, i) => id === prev[i]) ? prev : next;
      });
      return page;
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [baseFetch, refreshKey],
  );
  const states = useDerivedValueStates(TRANSPORT_TRIP_TARGET_TYPE, visibleIds);
  useRecordUpdates(TRANSPORT_TRIP_TARGET_TYPE, visibleIds, { queryKeysFor: () => [], onUpdate: () => setRefreshKey((k) => k + 1) });

  const units = useAllOrganizationUnits();
  const unitTree = useMemo(() => buildTree(units.data ?? [], (u) => u.id ?? "", (u) => u.parentId, (u) => u.displayName ?? ""), [units.data]);
  const carrierPage = useMemo(() => relationLookupPage(2), []);
  const cancelTrip = useCancelTrip();
  const completeTrip = useCompleteTrip();

  const selectedIds = selection?.mode === "Explicit" ? selection.explicitIds : [];
  const selectedRows = selectedIds.map((id) => rowsById.get(id)).filter((row): row is TransportTripRow => row != null);
  const singleId = selectedIds.length === 1 ? selectedIds[0] : undefined;
  const single = singleId ? rowsById.get(singleId) : undefined;

  const calculating = (id: string | undefined) => (id ? states.data?.get(id.toLowerCase()) : undefined);
  const derived = (field: keyof TransportTripRow, headerName: string, cell: (p: { value: number | null | undefined }) => unknown, extra: Partial<ColDef<TransportTripRow>> = {}): ColDef<TransportTripRow> => ({
    field,
    headerName,
    minWidth: 110,
    type: "rightAligned",
    sortable: false,
    cellRenderer: (p: { value: number | null | undefined; data: TransportTripRow | undefined }) => (
      <span className="inline-flex items-center justify-end gap-1">
        <CalculatingIndicator state={calculating(p.data?.id)} showLabel={false} />
        {cell({ value: p.value }) as never}
      </span>
    ),
    ...extra,
  });

  const columns = useMemo<ColDef<TransportTripRow>[]>(
    () => [
      { field: "number", headerName: t("PlanningExecution:Number"), minWidth: 130 },
      { field: "status", headerName: t("PlanningExecution:Status"), minWidth: 120, cellRenderer: (p: { value: TransportTripStatus | undefined }) => <StatusCell entity="TransportTrip" value={tripStatusName(p.value)} /> },
      { field: "tripDate", headerName: t("PlanningExecution:TripDate"), minWidth: 110, cellRenderer: DateCell },
      { field: "name", headerName: t("Orders:Name"), minWidth: 140 },
      { field: "transportTripTypeName", headerName: t("PlanningExecution:TransportTripType"), minWidth: 130, sortable: false },
      { field: "carrierName", headerName: t("PlanningExecution:Carrier"), minWidth: 150 },
      { colId: "vehicles", headerName: t("PlanningExecution:Vehicles"), minWidth: 140, valueGetter: (p) => (p.data?.vehicles ?? []).join(", "), sortable: false },
      { colId: "drivers", headerName: t("PlanningExecution:Drivers"), minWidth: 140, valueGetter: (p) => (p.data?.drivers ?? []).join(", "), sortable: false },
      { field: "plannedDeparture", headerName: t("PlanningExecution:PlannedDeparture"), minWidth: 140, cellRenderer: DateTimeCell },
      { field: "plannedArrival", headerName: t("PlanningExecution:PlannedArrival"), minWidth: 140, cellRenderer: DateTimeCell },
      { field: "stopCount", headerName: t("PlanningExecution:Stops"), minWidth: 80, cellRenderer: NumberCell, type: "rightAligned", sortable: false },
      { field: "legCount", headerName: t("PlanningExecution:Legs"), minWidth: 80, cellRenderer: NumberCell, type: "rightAligned", sortable: false },
      { colId: "customers", headerName: t("PlanningExecution:Customers"), minWidth: 160, valueGetter: (p) => (p.data?.customers ?? []).join(", "), sortable: false },
      { colId: "orders", headerName: t("PlanningExecution:Orders"), minWidth: 160, valueGetter: (p) => (p.data?.orderNumbers ?? []).join(", "), sortable: false },
      derived("plannedDistanceM", `${t("PlanningExecution:Distance")} (km)`, (p) => (p.value == null ? "" : f.number(p.value / 1000, { maxDecimals: 1 }))),
      derived("loadFactor", t("PlanningExecution:LoadFactor"), (p) => PercentCell({ value: p.value == null ? null : p.value * 100 })),
      derived("co2Kg", t("PlanningExecution:Co2Kg"), (p) => NumberCell({ value: p.value })),
      derived("costAmount", t("PlanningExecution:CostAmount"), (p) => AmountCell({ value: p.value })),
      derived("revenueAmount", t("PlanningExecution:RevenueAmount"), (p) => AmountCell({ value: p.value })),
      { field: "warnings", headerName: t("PlanningExecution:Warnings"), minWidth: 100, sortable: false, cellRenderer: (p: { value: TransportTripRow["warnings"] }) => <WarningsCell value={p.value} /> },
      { field: "isArchived", headerName: t("MasterData:IsArchived"), minWidth: 90, hide: true, cellRenderer: BoolCell },
      { field: "execution", headerName: t("PlanningExecution:Execution"), minWidth: 110, hide: true, valueFormatter: (p: ValueFormatterParams<TransportTripRow, number>) => t(TRIP_EXECUTIONS.find((e) => e.value === p.value)?.key ?? "") },
      { colId: "route", headerName: t("PlanningExecution:Route"), minWidth: 160, hide: true, valueGetter: (p) => [p.data?.firstStopCity, p.data?.lastStopCity].filter(Boolean).join(" → "), sortable: false },
      { field: "actualDeparture", headerName: t("PlanningExecution:ActualDeparture"), minWidth: 140, hide: true, cellRenderer: DateTimeCell },
      { field: "actualArrival", headerName: t("PlanningExecution:ActualArrival"), minWidth: 140, hide: true, cellRenderer: DateTimeCell },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [t, f, states.data],
  );

  if (features.isLoaded && !features.isFeatureEnabled("Apps.Transport.Trips")) return <FeatureUnavailable />;
  if (!canView) return <PermissionDenied />;

  const refresh = () => setRefreshKey((k) => k + 1);
  const selectOne = (row: TransportTripRow) => setSelection({ mode: "Explicit", explicitIds: [row.id!], filterRequest: null, excludedIds: [] });
  const openStatus = (row: TransportTripRow | undefined) => row?.status === 1 || row?.status === 2;
  const commands: AppCommandBarCommand[] = [
    ...(canPrint ? [{ id: "print", labelKey: "Orders:PrintConsignmentNote", icon: Printer, requiresSelection: true, disabled: !singleId, disabledReasonKey: "PlanningExecution:SelectOneTrip", onClick: () => setDialog("print") }] : []),
    ...(canArchive ? [{ id: "archive", labelKey: "PlanningExecution:ArchiveTrips", icon: Archive, requiresSelection: true, disabled: selectedRows.length === 0, disabledReasonKey: "PlanningExecution:SelectTrips", onClick: () => setDialog("archive") }] : []),
    ...(canComplete ? [{ id: "complete", labelKey: "PlanningExecution:Complete", icon: CircleCheck, requiresSelection: true, disabled: !openStatus(single), disabledReasonKey: "PlanningExecution:SelectOneOpenTrip", onClick: () => setDialog("complete") }] : []),
    ...(canCancel ? [{ id: "cancel", labelKey: "PlanningExecution:CancelTrip", icon: CircleX, variant: "destructive" as const, requiresSelection: true, disabled: !openStatus(single), disabledReasonKey: "PlanningExecution:SelectOneOpenTrip", onClick: () => setDialog("cancel") }] : []),
  ];

  return (
    <div className="flex h-full flex-col" data-slot="trips-view" data-filter={filter}>
      <AppCommandBar commands={commands} selectionCount={selection ? (selection.mode === "Explicit" ? selection.explicitIds.length : 1) : 0} />
      <div className="min-h-0 flex-1 px-6 pb-6">
        <LazyOpenTmsGrid<TransportTripRow>
          gridId="planning.transport.transportTrips"
          columnDefs={columns}
          fetchRows={fetchRows}
          onSelectionChange={setSelection}
          onOpen={(row) => row.id && openRecord("TransportTrip", row.id)}
          rowActions={(rows) => [
            { id: "open", labelKey: "Open", onClick: () => rows[0]?.id && openRecord("TransportTrip", rows[0].id) },
            ...(canPrint ? [{ id: "print", labelKey: "Orders:PrintConsignmentNote", icon: Printer, onClick: () => { selectOne(rows[0]); setDialog("print"); } }] : []),
            ...(canComplete ? [{ id: "complete", labelKey: "PlanningExecution:Complete", icon: CircleCheck, disabled: !openStatus(rows[0]), disabledReasonKey: "OpenTms.PlanningExecution:InvalidTransition", onClick: () => { selectOne(rows[0]); setDialog("complete"); } }] : []),
            ...(canArchive ? [{ id: "archive", labelKey: "PlanningExecution:Archive", icon: Archive, disabled: openStatus(rows[0]) || rows[0]?.isArchived === true, disabledReasonKey: "OpenTms.PlanningExecution:InvalidTransition", onClick: () => { setSelection({ mode: "Explicit", explicitIds: rows.map((r) => r.id!), filterRequest: null, excludedIds: [] }); setDialog("archive"); } }] : []),
            ...(canCancel ? [{ id: "cancel", labelKey: "PlanningExecution:CancelTrip", icon: CircleX, destructive: true, disabled: !openStatus(rows[0]), disabledReasonKey: "OpenTms.PlanningExecution:InvalidTransition", onClick: () => { selectOne(rows[0]); setDialog("cancel"); } }] : []),
          ]}
          filters={
            <>
              {view === TRIP_VIEWS.all && (
                <>
                  <DatePicker id="trips-date-from-filter" value={dateFrom} onChange={setDateFrom} placeholder={t("Financial:From")} className="w-36" />
                  <DatePicker id="trips-date-to-filter" value={dateTo} onChange={setDateTo} placeholder={t("Financial:To")} className="w-36" />
                </>
              )}
              <MultiSelect id="trips-status-filter" value={statuses.map(String)} onChange={(next) => setStatuses(next.map(Number) as TransportTripStatus[])} options={TRANSPORT_TRIP_STATUSES.map((s) => ({ id: String(s.value), label: t(s.key) }))} className="w-44" />
              <EntityLookup<RelationOption> id="trips-carrier-filter" value={carrier} onChange={setCarrier} fetchPage={carrierPage} getLabel={(r) => r.name} placeholder={t("PlanningExecution:Carrier")} className="w-56" />
              <TreeSelect id="trips-unit-filter" nodes={unitTree} value={unitId} onChange={setUnitId} noneLabel={t("PlanningExecution:OrganizationUnit")} className="w-48" />
              <label className="flex items-center gap-2 text-sm" htmlFor="trips-hide-empty">
                <Checkbox id="trips-hide-empty" checked={hideEmpty} onCheckedChange={(next) => setHideEmpty(next === true)} />
                {t("PlanningExecution:HideEmpty")}
              </label>
            </>
          }
        />
      </div>
      {single && <PrintCmrWizard open={dialog === "print"} onOpenChange={(next) => setDialog(next ? "print" : null)} trip={{ id: single.id!, number: single.number ?? "" }} />}
      <ArchiveTripsWizard open={dialog === "archive"} onOpenChange={(next) => { setDialog(next ? "archive" : null); if (!next) refresh(); }} trips={selectedRows} />
      <ConfirmDialog
        open={dialog === "complete"}
        onOpenChange={(next) => setDialog(next ? "complete" : null)}
        title={t("PlanningExecution:CompleteTrip")}
        description={t("PlanningExecution:CompleteTripQuestion").replace("{0}", single?.number ?? "")}
        confirmLabel={t("PlanningExecution:Complete")}
        onConfirm={async () => {
          if (!singleId) return;
          try {
            await completeTrip.mutateAsync({ id: singleId });
            toast.success(t("PlanningExecution:TransportTripCompleted"));
            setDialog(null);
            refresh();
          } catch (err) {
            setDialog(null);
            toast.error(getApiErrorMessage(err, t));
          }
        }}
      />
      <ConfirmDialog
        open={dialog === "cancel"}
        onOpenChange={(next) => setDialog(next ? "cancel" : null)}
        title={t("PlanningExecution:CancelTrip")}
        description={`${t("PlanningExecution:CancelTripQuestion").replace("{0}", single?.number ?? "")} ${t("ThisCannotBeUndone")}`}
        confirmLabel={t("PlanningExecution:CancelTrip")}
        destructive
        onConfirm={async () => {
          if (!singleId) return;
          try {
            await cancelTrip.mutateAsync(singleId);
            toast.success(t("PlanningExecution:TransportTripCancelled"));
            setDialog(null);
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
