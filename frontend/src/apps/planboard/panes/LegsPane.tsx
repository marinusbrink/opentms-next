import { useCallback, useMemo, useRef, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import type { ColDef } from "ag-grid-community";
import { Pencil, Split, Unlink } from "lucide-react";
import { toast } from "sonner";

import { PLANBOARD_RECORD_TYPES, usePlanboardLiveUpdates } from "@/apps/planboard/live-updates";
import { DRAG_COLUMN, lower } from "@/apps/planboard/panes/lists/grid-helpers";
import { DragHandleCell } from "@/apps/planboard/panes/lists/grid-support";
import { PaneMessage } from "@/apps/planboard/panes/lists/PaneMessage";
import { LegReferencesDialog } from "@/apps/planboard/panes/lists/stop-leg-dialogs";
import type { PlanboardPaneProps } from "@/apps/planboard/panes/registry";
import { usePlanboardSelection } from "@/apps/planboard/selection";
import { AmountCell, BoolCell, DateCell, NumberCell } from "@/components/ui/grid-cells";
import type { GridRequest, GridResponse, GridRowAction, GridSelectionDto } from "@/components/ui/opentms-grid";
import { LazyOpenTmsGrid } from "@/components/ui/opentms-grid-lazy";
import { PermissionDenied } from "@/components/ui/permission-denied";
import { Skeleton } from "@/components/ui/skeleton";
import { legEndLabel, legKeys, useLegsOfTrip, type LegDto } from "@/domains/planning-execution/legs";
import { useUnassignLegsFromTrip } from "@/domains/planning-execution/planboard-lists";
import { transportTripKeys, useCarryOutSeparately } from "@/domains/planning-execution/trips";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { useOpenRecord } from "@/lib/use-open-record";

/* Design 70, "Legs": the legs of the focused trip (P:Q16): order, customer, origin → destination, dates, windows,
 * quantities, references, remarks, distance and allocated cost and revenue. Actions: unassign (back to Legs to schedule),
 * carry out separately, edit references and remarks (the Planning fields only, D19), open the order. Drag the handle (the
 * selected legs, or the row's own) onto another trip in the trips pane, a Scheduler bar or the map to move them. */

const GRID_ID = "planning.planboard.tripLegs";

export default function LegsPane(_props: PlanboardPaneProps) {
  const { t } = useL();
  const { focusedTripId } = usePlanboardSelection();
  const canView = useIsGranted("PlanningExecution.Legs");
  const legs = useLegsOfTrip(focusedTripId ?? undefined);
  const queryClient = useQueryClient();
  const refetch = useCallback(() => void queryClient.invalidateQueries({ queryKey: legKeys.ofTrip(focusedTripId ?? undefined) }), [queryClient, focusedTripId]);
  const legIds = useMemo(() => new Set((legs.data ?? []).map((leg) => lower(leg.id))), [legs.data]);

  /* The trip's own notices (legs assigned, moved, unassigned) and notices of its legs refetch the list. */
  usePlanboardLiveUpdates({
    targetType: PLANBOARD_RECORD_TYPES.trip,
    predicate: (message) => focusedTripId != null && lower(message.targetId) === lower(focusedTripId),
    onIds: refetch,
    onResync: refetch,
    enabled: canView && focusedTripId != null,
  });
  usePlanboardLiveUpdates({
    targetType: PLANBOARD_RECORD_TYPES.leg,
    predicate: (message) => legIds.has(lower(message.targetId)),
    onIds: refetch,
    enabled: canView && focusedTripId != null,
  });

  if (!canView) return <PermissionDenied />;
  if (!focusedTripId) return <PaneMessage>{t("Planboard:Lists.SelectOneTripForLegs")}</PaneMessage>;
  if (legs.isLoading) return <Skeleton className="m-2 h-full" />;
  if (legs.isError) return <PaneMessage tone="error">{getApiErrorMessage(legs.error, t)}</PaneMessage>;
  return <LegsGrid tripId={focusedTripId} legs={legs.data ?? []} />;
}

function LegsGrid({ tripId, legs }: { tripId: string; legs: LegDto[] }) {
  const { t } = useL();
  const f = useFormat();
  const openRecord = useOpenRecord();
  const queryClient = useQueryClient();
  const selection = usePlanboardSelection();
  const canUpdate = useIsGranted("PlanningExecution.Legs.Update");
  const canAssign = useIsGranted("PlanningExecution.TransportTrips.AssignLegs");
  const canManageStops = useIsGranted("PlanningExecution.TransportTrips.ManageStops");
  const unassign = useUnassignLegsFromTrip();
  const carryOut = useCarryOutSeparately();
  /* Read by the drag handle when a drag starts (a ref, so a selection change does not rebuild the columns). */
  const selectedIds = useRef<string[]>([]);
  const [editing, setEditing] = useState<LegDto | null>(null);

  const fetchRows = useMemo(() => async (_request: GridRequest): Promise<GridResponse<LegDto>> => ({ rows: legs, totalCount: legs.length, filteredCount: legs.length }), [legs]);

  const onSelectionChange = useCallback(
    (next: GridSelectionDto) => {
      const ids = next.mode === "Explicit" ? next.explicitIds : next.mode === "FilterBased" ? legs.map((l) => l.id!).filter((id) => !next.excludedIds.includes(id)) : [];
      selectedIds.current = ids;
      selection.setSelectedLegIds(ids);
    },
    [legs, selection],
  );

  const run = async (action: Promise<unknown>, successKey: string) => {
    try {
      await action;
      toast.success(t(successKey));
      void queryClient.invalidateQueries({ queryKey: transportTripKeys.detail(tripId) });
    } catch (err) {
      toast.error(getApiErrorMessage(err, t));
    }
  };

  const columns = useMemo<ColDef<LegDto>[]>(
    () => [
      ...(canAssign
        ? [
            {
              ...DRAG_COLUMN,
              cellRenderer: (p: { data: LegDto | undefined }) => (
                <DragHandleCell<LegDto>
                  data={p.data}
                  labelKey="Planboard:Lists.DragToTrip"
                  payloadOf={(leg) => ({ kind: "legs", legIds: selectedIds.current.includes(leg.id!) ? selectedIds.current : [leg.id!], fromTripId: tripId })}
                />
              ),
            } satisfies ColDef<LegDto>,
          ]
        : []),
      { field: "orderNumber", headerName: t("PlanningExecution:Order"), minWidth: 120, sortable: false },
      { field: "customerName", headerName: t("PlanningExecution:Customer"), minWidth: 150, sortable: false },
      { colId: "origin", headerName: t("PlanningExecution:Origin"), minWidth: 160, sortable: false, valueGetter: (p) => legEndLabel(p.data?.origin) },
      { colId: "destination", headerName: t("PlanningExecution:Destination"), minWidth: 160, sortable: false, valueGetter: (p) => legEndLabel(p.data?.destination) },
      { colId: "originDate", headerName: t("Planboard:Lists.OriginDate"), minWidth: 110, sortable: false, valueGetter: (p) => p.data?.origin?.plannedDate, cellRenderer: DateCell },
      { colId: "destinationDate", headerName: t("Planboard:Lists.DestinationDate"), minWidth: 110, sortable: false, valueGetter: (p) => p.data?.destination?.plannedDate, cellRenderer: DateCell },
      {
        colId: "originWindow",
        headerName: t("Planboard:Lists.OriginWindow"),
        minWidth: 160,
        sortable: false,
        valueGetter: (p) => windowText(p.data?.origin?.windowFrom, p.data?.origin?.windowTill, f.dateTime),
      },
      {
        colId: "destinationWindow",
        headerName: t("Planboard:Lists.DestinationWindow"),
        minWidth: 160,
        sortable: false,
        valueGetter: (p) => windowText(p.data?.destination?.windowFrom, p.data?.destination?.windowTill, f.dateTime),
      },
      { field: "quantity", headerName: t("PlanningExecution:Quantity"), minWidth: 90, sortable: false, cellRenderer: NumberCell, type: "rightAligned" },
      { field: "weightKg", headerName: t("PlanningExecution:WeightKg"), minWidth: 100, sortable: false, cellRenderer: NumberCell, type: "rightAligned" },
      { field: "loadingMeters", headerName: t("PlanningExecution:LoadingMeters"), minWidth: 100, sortable: false, cellRenderer: NumberCell, type: "rightAligned" },
      { colId: "originReference", headerName: t("Planboard:Lists.OriginReference"), minWidth: 130, sortable: false, valueGetter: (p) => p.data?.origin?.reference },
      { colId: "destinationReference", headerName: t("Planboard:Lists.DestinationReference"), minWidth: 130, sortable: false, valueGetter: (p) => p.data?.destination?.reference },
      { field: "remarks", headerName: t("PlanningExecution:Remarks"), minWidth: 150, sortable: false },
      { colId: "distance", headerName: `${t("PlanningExecution:Distance")} (km)`, minWidth: 100, sortable: false, type: "rightAligned", valueGetter: (p) => (p.data?.distanceM == null ? null : p.data.distanceM / 1000), valueFormatter: (p) => f.number(p.value as number | null, { maxDecimals: 1 }), cellClass: "tabular-nums" },
      { field: "allocatedCostAmount", headerName: t("PlanningExecution:AllocatedCost"), minWidth: 110, sortable: false, cellRenderer: AmountCell, type: "rightAligned" },
      { field: "allocatedRevenueAmount", headerName: t("PlanningExecution:AllocatedRevenue"), minWidth: 110, sortable: false, cellRenderer: AmountCell, type: "rightAligned" },
      { field: "isCarriedOutSeparately", headerName: t("PlanningExecution:IsCarriedOutSeparately"), minWidth: 100, hide: true, sortable: false, cellRenderer: BoolCell },
    ],
    [t, f, canAssign, tripId],
  );

  const rowActions = (rows: LegDto[]): GridRowAction[] => {
    const leg = rows[0];
    if (!leg?.id) return [];
    return [
      ...(canUpdate ? [{ id: "edit", labelKey: "Planboard:Lists.EditLeg", icon: Pencil, onClick: () => setEditing(leg) }] : []),
      { id: "open-order", labelKey: "PlanningExecution:OpenOrder", onClick: () => leg.orderId && openRecord("TransportOrder", leg.orderId) },
      ...(canManageStops
        ? [{ id: "separately", labelKey: "PlanningExecution:CarryOutSeparately", icon: Split, disabled: leg.isCarriedOutSeparately === true, disabledReasonKey: "PlanningExecution:AlreadySeparate", onClick: () => void run(carryOut.mutateAsync({ id: tripId, legId: leg.id! }), "PlanningExecution:TransportTripSaved") }]
        : []),
      /* Reversible (the legs return to Legs to schedule): no confirmation, no destructive styling (UI-ACT-005). */
      ...(canAssign
        ? [{ id: "unassign", labelKey: "PlanningExecution:UnassignLegs", icon: Unlink, multi: true, onClick: () => void run(unassign.mutateAsync({ tripId, legIds: rows.map((r) => r.id!).filter(Boolean) }), "PlanningExecution:LegsUnassigned") }]
        : []),
    ];
  };

  if (legs.length === 0) return <PaneMessage>{t("PlanningExecution:NoLegs")}</PaneMessage>;

  return (
    <div className="flex h-full min-h-0 flex-col p-2" data-slot="planboard-legs-pane">
      <LazyOpenTmsGrid<LegDto>
        gridId={GRID_ID}
        columnDefs={columns}
        fetchRows={fetchRows}
        onSelectionChange={onSelectionChange}
        clickSelects
        onOpen={(leg) => canUpdate && setEditing(leg)}
        rowActions={rowActions}
      />
      {editing && <LegReferencesDialog leg={editing} onClose={() => setEditing(null)} />}
    </div>
  );
}

function windowText(from: string | null | undefined, till: string | null | undefined, format: (value: string) => string): string {
  if (!from && !till) return "";
  return `${from ? format(from) : ""} – ${till ? format(till) : ""}`;
}
