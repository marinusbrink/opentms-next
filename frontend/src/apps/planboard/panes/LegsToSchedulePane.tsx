import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { ColDef } from "ag-grid-community";
import { CornerUpLeft, ListTree, MapPinPlus, Split } from "lucide-react";
import { toast } from "sonner";

import { usePaneFilters } from "@/apps/planboard/filters";
import { MAX_DRAGGED_LEGS } from "@/apps/planboard/dnd";
import { PLANBOARD_RECORD_TYPES, usePlanboardLiveUpdates } from "@/apps/planboard/live-updates";
import { DRAG_COLUMN, lower, useGridApiCapture } from "@/apps/planboard/panes/lists/grid-helpers";
import { DragHandleCell } from "@/apps/planboard/panes/lists/grid-support";
import { PaneFilterBar } from "@/apps/planboard/panes/lists/PaneFilterBar";
import { useSelectedOrder } from "@/apps/planboard/panes/lists/selected-order";
import { useLegInlineColumns } from "@/apps/planboard/panes/lists/leg-columns";
import type { PlanboardPaneProps } from "@/apps/planboard/panes/registry";
import { usePlanboardSelection } from "@/apps/planboard/selection";
import { usePaneSettings } from "@/apps/planboard/settings-store";
import { Checkbox } from "@/components/ui/checkbox";
import { AmountCell, NumberCell } from "@/components/ui/grid-cells";
import type { GridRequest, GridResponse, GridRowAction, GridSelectionDto, GridTreeDataOptions } from "@/components/ui/opentms-grid";
import { LazyOpenTmsGrid } from "@/components/ui/opentms-grid-lazy";
import { PermissionDenied } from "@/components/ui/permission-denied";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { ManageLegsDialog } from "@/domains/planning-execution/components/ManageLegsDialog";
import { SplitLegDialog } from "@/domains/planning-execution/components/SplitLegDialog";
import { SplitLineDialog } from "@/domains/planning-execution/components/SplitLineDialog";
import { fetchLegs, legEndLabel, type LegDto } from "@/domains/planning-execution/legs";
import {
  fetchUnplannedLegGroups,
  fetchUnplannedLegs,
  fetchUnplannedLegsByIds,
  isLegGrouping,
  LEG_GROUPINGS,
  useUnplannedReturnLeg,
  type LegGrouping,
  type PlanboardListFilters,
} from "@/domains/planning-execution/planboard-lists";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { useOpenRecord } from "@/lib/use-open-record";

/* Design 70, "Legs to schedule": the unplanned legs of the board's range (origin date) in a server-side OpenTmsGrid,
 * optionally grouped on the D18 whitelist (origin date, customer, origin city, destination city): the groups come from
 * `/leg/groups` with their counts and expand into their legs (`groupKey`), all on the server. Multi-select; the drag handle
 * drags the selected legs (or the row's own) onto a trip row, a Scheduler row or bar, or the map. Row actions: add or modify legs
 * (design 74 §4), split line (design 74 §7), split at address (the Transport app's dialog, shared from the Planning domain),
 * return leg (design 68 §7, without a trip), open the order. The editable columns are edited in the cell (design 74 §6,
 * leg-columns.tsx). With an order selected in Transport orders, "This order"
 * shows that order's unplanned legs.
 *
 * Legs carry no organisation unit (design 68 assumption 1), so the units filter shows every leg and live notices are not
 * filtered on units. */

const GRID_ID = "planning.planboard.legsToSchedule";
const GROUP_PREFIX = "group:";
const NO_GROUPING = "none";

type LegRow = LegDto & { groupKey?: string; display?: string };

interface LegsToScheduleSettings {
  groupBy: LegGrouping | null;
}

const sanitize = (stored: Record<string, unknown>): Partial<LegsToScheduleSettings> => (stored.groupBy === null || isLegGrouping(stored.groupBy) ? { groupBy: stored.groupBy as LegGrouping | null } : {});
const DEFAULT_SETTINGS: LegsToScheduleSettings = { groupBy: null };
const isGroupRow = (row: LegRow | undefined) => row?.groupKey != null;

export default function LegsToSchedulePane({ instanceId }: PlanboardPaneProps) {
  const { t } = useL();
  const f = useFormat();
  const openRecord = useOpenRecord();
  const selection = usePlanboardSelection();
  const canView = useIsGranted("PlanningExecution.Legs");
  const canUpdate = useIsGranted("PlanningExecution.Legs.Update");
  const canDrag = useIsGranted("PlanningExecution.TransportTrips.AssignLegs");
  const paneFilters = usePaneFilters(instanceId);
  const { settings, update } = usePaneSettings<LegsToScheduleSettings>(instanceId, DEFAULT_SETTINGS, sanitize);
  const order = useSelectedOrder();
  const [orderOnly, setOrderOnly] = useState(false);
  const activeOrder = orderOnly ? order : null;
  const groupBy = activeOrder ? null : settings.groupBy;
  const returnLeg = useUnplannedReturnLeg();
  const [splitting, setSplitting] = useState<LegDto | null>(null);
  const [splittingAt, setSplittingAt] = useState<LegDto | null>(null);
  const [managing, setManaging] = useState<LegDto | null>(null);

  const filters = useMemo<PlanboardListFilters>(
    () => ({ from: paneFilters.filters.range.from, till: paneFilters.filters.range.till, organizationUnitIds: paneFilters.filters.organizationUnitIds }),
    [paneFilters.filters],
  );
  const filtersRef = useRef(filters);
  filtersRef.current = filters;
  const queryKey = JSON.stringify({ filters, groupBy, orderId: activeOrder?.id ?? null });
  const [reloadKey, setReloadKey] = useState(0);

  const grid = useGridApiCapture<LegRow>();
  const rows = useRef(new Map<string, LegRow>());
  const selected = useRef<GridSelectionDto | null>(null);

  const remember = (list: LegRow[]) => list.forEach((row) => row.id && !isGroupRow(row) && rows.current.set(lower(row.id), row));

  const fetchRows = useCallback(
    async (request: GridRequest): Promise<GridResponse<LegRow>> => {
      const { filters: current, groupBy: grouping, orderId } = JSON.parse(queryKey) as { filters: PlanboardListFilters; groupBy: LegGrouping | null; orderId: string | null };
      if (request.startRow === 0 && request.groupKeys.length === 0) rows.current.clear();
      if (orderId) {
        /* One order's legs: a bounded set (an order has a few dozen legs at most); the unplanned ones are shown. */
        const all = (await fetchLegs({ ...request, startRow: 0, endRow: 200, sortModels: [], wildcardSearch: null }, { orderId })).rows.filter((leg) => leg.tripId == null);
        remember(all);
        return { rows: all.slice(request.startRow, request.endRow), totalCount: all.length, filteredCount: all.length };
      }
      if (grouping && request.groupKeys.length === 0) {
        const page = await fetchUnplannedLegGroups(request, { ...current, groupBy: grouping });
        return {
          ...page,
          rows: page.rows.map((group) => ({ id: `${GROUP_PREFIX}${group.key ?? ""}`, groupKey: group.key ?? "", display: `${group.label ?? t("Planboard:Lists.NoValue")} (${f.number(group.count ?? 0)})` })),
        };
      }
      const page = await fetchUnplannedLegs(request, { ...current, groupBy: grouping, groupKey: grouping ? request.groupKeys[0] : null });
      const list = page.rows.map((leg) => ({ ...leg, display: leg.orderNumber ?? "" }));
      remember(list);
      return { ...page, rows: list };
    },
    // A new filter, grouping, order or reload gives the grid a new fetcher: it reloads from the top.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [queryKey, reloadKey, t, f],
  );

  const resync = useCallback(() => {
    const api = grid.current();
    if (api) api.refreshServerSide({ purge: false });
    else setReloadKey((k) => k + 1);
  }, [grid]);

  /** Refetches legs by id and patches their rows (flat list); grouped or new legs reload the visible blocks. */
  const refreshLegs = useCallback(
    async (ids: string[]) => {
      const api = grid.current();
      if (!api || groupBy || activeOrder) {
        resync();
        return;
      }
      let fresh: LegDto[];
      try {
        fresh = await fetchUnplannedLegsByIds(ids, filtersRef.current);
      } catch {
        resync();
        return;
      }
      const found = new Map(fresh.filter((leg) => leg.id).map((leg) => [lower(leg.id), leg]));
      const update = fresh.filter((leg) => rows.current.has(lower(leg.id))).map((leg) => ({ ...leg, display: leg.orderNumber ?? "" }));
      const remove = ids.filter((id) => rows.current.has(id) && !found.has(id)).map((id) => rows.current.get(id)!);
      remember(update);
      remove.forEach((leg) => rows.current.delete(lower(leg.id)));
      if (update.length > 0 || remove.length > 0) api.applyServerSideTransaction({ update, remove });
      if (fresh.some((leg) => !rows.current.has(lower(leg.id)))) resync();
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [grid, groupBy, activeOrder, resync],
  );

  const knownIds = useMemo(() => ({ has: (id: string) => rows.current.has(id) }), []);
  usePlanboardLiveUpdates({
    targetType: PLANBOARD_RECORD_TYPES.leg,
    predicate: (message) => {
      if (knownIds.has(lower(message.targetId))) return true;
      const hints = message.hints;
      if (!hints) return true;
      if (hints.removed) return false;
      const start = hints.from ?? hints.till;
      return start == null || (start.slice(0, 10) >= filtersRef.current.from && start.slice(0, 10) <= filtersRef.current.till);
    },
    onIds: (ids) => void refreshLegs(ids),
    onResync: resync,
    enabled: canView,
  });

  /* An order selection that goes away takes the filter with it. */
  useEffect(() => {
    if (!order) setOrderOnly(false);
  }, [order]);

  const draggedIds = (row: LegRow): string[] => {
    const current = selected.current;
    const id = row.id!;
    if (!current) return [id];
    if (current.mode === "Explicit") {
      const ids = current.explicitIds.filter((x) => !x.startsWith(GROUP_PREFIX));
      return ids.includes(id) ? ids.slice(0, MAX_DRAGGED_LEGS) : [id];
    }
    /* Select-all: the loaded legs except the ones taken out again. */
    const excluded = new Set(current.excludedIds.map(lower));
    const loaded = [...rows.current.values()].map((leg) => leg.id!).filter((x) => !excluded.has(lower(x)));
    return loaded.includes(id) ? loaded.slice(0, MAX_DRAGGED_LEGS) : [id];
  };

  const onSelectionChange = useCallback(
    (next: GridSelectionDto) => {
      selected.current = next;
      const ids = next.mode === "Explicit" ? next.explicitIds.filter((id) => !id.startsWith(GROUP_PREFIX)) : [];
      selection.setSelectedLegIds(ids);
    },
    [selection],
  );

  const treeData = useMemo<GridTreeDataOptions<LegRow> | undefined>(
    () => (groupBy ? { isGroup: isGroupRow, getGroupKey: (row) => row.groupKey ?? "", headerName: t(LEG_GROUPINGS.find((g) => g.value === groupBy)?.key ?? ""), field: "display", minWidth: 200 } : undefined),
    [groupBy, t],
  );

  /* A flat list patches its rows from the answer; a grouped one reloads the visible blocks, as its live updates do. */
  const editable = useLegInlineColumns<LegRow>({
    patchRows: (match, patch) => {
      const api = grid.current();
      if (!api || groupBy) {
        resync();
        return;
      }
      const update = [...rows.current.values()].filter(match).map(patch);
      remember(update);
      if (update.length > 0) api.applyServerSideTransaction({ update });
    },
  });

  const columns = useMemo<ColDef<LegRow>[]>(
    () => [
      ...(canDrag
        ? [
            {
              ...DRAG_COLUMN,
              cellRenderer: (p: { data: LegRow | undefined }) =>
                p.data && !isGroupRow(p.data) ? <DragHandleCell<LegRow> data={p.data} labelKey="Planboard:Lists.DragToTrip" payloadOf={(leg) => ({ kind: "legs", legIds: draggedIds(leg) })} /> : null,
            } satisfies ColDef<LegRow>,
          ]
        : []),
      { field: "orderNumber", headerName: t("PlanningExecution:Order"), minWidth: 120, sortable: false },
      { field: "customerName", headerName: t("PlanningExecution:Customer"), minWidth: 150, sortable: false },
      { colId: "origin", headerName: t("PlanningExecution:Origin"), minWidth: 160, sortable: false, valueGetter: (p) => legEndLabel(p.data?.origin) },
      { colId: "destination", headerName: t("PlanningExecution:Destination"), minWidth: 160, sortable: false, valueGetter: (p) => legEndLabel(p.data?.destination) },
      editable.customerReference(),
      editable.transportUnit(),
      editable.product(),
      editable.temperature({ hide: true }),
      editable.originDate(),
      editable.destinationDate(),
      editable.originWindow({ hide: true }),
      editable.destinationWindow({ hide: true }),
      { field: "quantity", headerName: t("PlanningExecution:Quantity"), minWidth: 90, cellRenderer: NumberCell, type: "rightAligned" },
      { field: "weightKg", headerName: t("PlanningExecution:WeightKg"), minWidth: 100, cellRenderer: NumberCell, type: "rightAligned" },
      { field: "loadingMeters", headerName: t("PlanningExecution:LoadingMeters"), minWidth: 100, cellRenderer: NumberCell, type: "rightAligned" },
      editable.originReference({ hide: true }),
      editable.destinationReference({ hide: true }),
      editable.remarks({ hide: true }),
      { colId: "distance", headerName: `${t("PlanningExecution:Distance")} (km)`, minWidth: 100, sortable: false, type: "rightAligned", valueGetter: (p) => (p.data?.distanceM == null ? null : p.data.distanceM / 1000), valueFormatter: (p) => f.number(p.value as number | null, { maxDecimals: 1 }), cellClass: "tabular-nums" },
      { field: "allocatedRevenueAmount", headerName: t("PlanningExecution:AllocatedRevenue"), minWidth: 110, sortable: false, hide: true, cellRenderer: AmountCell, type: "rightAligned" },
    ],
    // draggedIds reads refs only.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [t, f, canDrag, editable],
  );

  if (!canView) return <PermissionDenied />;

  const runReturnLeg = async (leg: LegDto) => {
    try {
      await returnLeg.mutateAsync(leg.id!);
      toast.success(t("PlanningExecution:ReturnLegCreated"));
      resync();
    } catch (err) {
      toast.error(getApiErrorMessage(err, t));
    }
  };

  const rowActions = (list: LegRow[]): GridRowAction[] => {
    const leg = list[0];
    if (!leg?.id || isGroupRow(leg)) return [];
    return [
      { id: "open-order", labelKey: "PlanningExecution:OpenOrder", onClick: () => leg.orderId && openRecord("TransportOrder", leg.orderId, { returnTo: "/planboard" }) },
      ...(canUpdate
        ? [
            { id: "manage-legs", labelKey: "PlanningExecution:ManageLegsAction", icon: ListTree, onClick: () => setManaging(leg) },
            /* The server splits decimals: only a line without a quantity has nothing to split. */
            { id: "split-line", labelKey: "PlanningExecution:SplitLine", icon: Split, disabled: (leg.quantity ?? 0) <= 0, disabledReasonKey: "Planboard:Lists.NothingToSplit", onClick: () => setSplitting(leg) },
            { id: "split-at-address", labelKey: "PlanningExecution:SplitLegAt", icon: MapPinPlus, onClick: () => setSplittingAt(leg) },
            { id: "return-leg", labelKey: "PlanningExecution:ReturnLeg", icon: CornerUpLeft, onClick: () => void runReturnLeg(leg) },
          ]
        : []),
    ];
  };

  const groupItems = [{ value: NO_GROUPING, label: t("Planboard:Lists.NoGrouping") }, ...LEG_GROUPINGS.map((g) => ({ value: g.value, label: t(g.key) }))];
  const groupSelectId = `planboard-${instanceId}-group-by`;

  const filterBar = (
    <>
      <PaneFilterBar instanceId={instanceId} paneFilters={paneFilters} />
      <label htmlFor={groupSelectId} className="text-label text-muted-foreground">
        {t("Planboard:Lists.GroupByLabel")}
      </label>
      <Select items={groupItems} value={settings.groupBy ?? NO_GROUPING} onValueChange={(next) => update({ groupBy: isLegGrouping(next) ? next : null })} disabled={activeOrder != null}>
        <SelectTrigger id={groupSelectId} className="h-8 w-44 bg-background">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          {groupItems.map((item) => (
            <SelectItem key={item.value} value={item.value}>
              {item.label}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
      {order && (
        <label className="flex items-center gap-2 text-sm" htmlFor={`planboard-${instanceId}-order-only`}>
          <Checkbox id={`planboard-${instanceId}-order-only`} checked={orderOnly} onCheckedChange={(next) => setOrderOnly(next === true)} />
          {t("Planboard:Lists.ThisOrderOnly").replace("{0}", order.number)}
        </label>
      )}
    </>
  );

  return (
    <div className="flex h-full min-h-0 flex-col p-2" data-slot="planboard-legs-to-schedule-pane">
      <LazyOpenTmsGrid<LegRow>
        /* The grouped grid has the tree's expanding column: it is a different grid for AG Grid. */
        key={groupBy ?? NO_GROUPING}
        gridId={GRID_ID}
        columnDefs={columns}
        fetchRows={fetchRows}
        treeData={treeData}
        onSelectionChange={onSelectionChange}
        clickSelects
        popupsOnPage
        onGridReady={grid.capture}
        onOpen={(leg) => !isGroupRow(leg) && leg.orderId && openRecord("TransportOrder", leg.orderId, { returnTo: "/planboard" })}
        rowActions={rowActions}
        filters={filterBar}
      />
      {managing && <ManageLegsDialog leg={managing} onClose={() => setManaging(null)} onDone={resync} />}
      {splitting && <SplitLineDialog leg={splitting} onClose={() => setSplitting(null)} onDone={resync} />}
      {splittingAt && <SplitLegDialog leg={splittingAt} onClose={() => setSplittingAt(null)} onDone={resync} />}
    </div>
  );
}
