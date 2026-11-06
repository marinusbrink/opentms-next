import { useCallback, useEffect, useMemo, useRef } from "react";
import type { ColDef } from "ag-grid-community";
import { toast } from "sonner";

import { usePaneFilters } from "@/apps/planboard/filters";
import { DRAG_COLUMN, useGridApiCapture } from "@/apps/planboard/panes/lists/grid-helpers";
import { DragHandleCell, InlineTextCell } from "@/apps/planboard/panes/lists/grid-support";
import { sameValue, saveWithStaleRetry } from "@/apps/planboard/panes/lists/inline-save";
import { PaneFilterBar } from "@/apps/planboard/panes/lists/PaneFilterBar";
import { setSelectedOrder } from "@/apps/planboard/panes/lists/selected-order";
import type { PlanboardPaneProps } from "@/apps/planboard/panes/registry";
import { AmountCell, DateCell, NumberCell } from "@/components/ui/grid-cells";
import type { GridRequest, GridSelectionDto } from "@/components/ui/opentms-grid";
import { LazyOpenTmsGrid } from "@/components/ui/opentms-grid-lazy";
import { PermissionDenied } from "@/components/ui/permission-denied";
import { StatusCell } from "@/components/ui/status-badge";
import {
  EXECUTION_PROGRESSES,
  fetchPlanboardOrders,
  fetchTransportOrder,
  orderStatusName,
  PLANBOARD_ORDER_EDIT_FEATURES,
  PLANBOARD_ORDER_FIELD,
  usePlanboardOrderField,
  type PlanboardOrderField,
  type TransportOrderRow,
  type TransportOrderStatus,
} from "@/domains/orders/transport-orders";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsFeatureEnabled, useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { useOpenRecord } from "@/lib/use-open-record";

/* Design 70, "Transport orders": the orders planned in the board's range (or the pane's own) on any of the chosen
 * organisation units, read from Orders' Planboard list (`GET /api/orders/planboard-order`). Customer reference and internal
 * remarks are edited in the cell (design 74 §6, through Orders' narrow field command), and the grip drags the order onto a trip
 * (design 74 §8). Double click or Open opens the order page in the Transport app. A single selected order is offered to Legs to schedule as its "this
 * order" filter. */

const GRID_ID = "orders.planboard.transportOrders";

export default function OrdersPane({ instanceId }: PlanboardPaneProps) {
  const { t } = useL();
  const openRecord = useOpenRecord();
  const canView = useIsGranted("Orders.TransportOrders");
  const canDrag = useIsGranted("PlanningExecution.TransportTrips.AssignLegs");
  const canUpdate = useIsGranted("Orders.TransportOrders.Update");
  const planboardOn = useIsFeatureEnabled(PLANBOARD_ORDER_EDIT_FEATURES[0]);
  const ordersOn = useIsFeatureEnabled(PLANBOARD_ORDER_EDIT_FEATURES[1]);
  const canEdit = canUpdate && planboardOn && ordersOn;
  const { mutateAsync: updateField } = usePlanboardOrderField();
  const grid = useGridApiCapture<TransportOrderRow>();
  const paneFilters = usePaneFilters(instanceId);
  const { range, organizationUnitIds } = paneFilters.filters;
  const filtersKey = JSON.stringify({ range, organizationUnitIds });
  const rows = useRef(new Map<string, TransportOrderRow>());

  const fetchRows = useCallback(
    async (request: GridRequest) => {
      const { range: r, organizationUnitIds: units } = JSON.parse(filtersKey) as { range: { from: string; till: string }; organizationUnitIds: string[] };
      const page = await fetchPlanboardOrders(request, { plannedFrom: r.from, plannedTo: r.till, organizationUnitIds: units });
      page.rows.forEach((row) => row.id && rows.current.set(row.id, row));
      return page;
    },
    [filtersKey],
  );

  /* Closing the pane takes its order selection with it. */
  useEffect(() => () => setSelectedOrder(null), []);

  const onSelectionChange = useCallback((selection: GridSelectionDto) => {
    const ids = selection.mode === "Explicit" ? selection.explicitIds : [];
    const row = ids.length === 1 ? rows.current.get(ids[0]) : undefined;
    setSelectedOrder(row?.id ? { id: row.id, number: row.number ?? "" } : null);
  }, []);

  const columns = useMemo<ColDef<TransportOrderRow>[]>(() => {
    /* Patched from the answer; the stamp is the row's own (a 409 goes through inline-save.ts). */
    const textColumn = (field: "customerReference" | "internalRemarks", headerName: string, orderField: PlanboardOrderField, maxLength: number, extra: Partial<ColDef<TransportOrderRow>> = {}): ColDef<TransportOrderRow> => ({
      field,
      headerName,
      minWidth: 140,
      cellRenderer: (p: { value: string | null | undefined; data: TransportOrderRow | undefined }) => (
        <InlineTextCell<TransportOrderRow>
          value={p.value}
          data={p.data}
          maxLength={maxLength}
          canEdit={() => canEdit}
          onCommit={async (row, value) => {
            try {
              const result = await saveWithStaleRetry({
                row,
                save: (current) => updateField({ orderId: current.id!, field: orderField, value, concurrencyStamp: current.concurrencyStamp ?? "" }),
                refetch: (current) => fetchTransportOrder(current.id!) as Promise<TransportOrderRow>,
                unchanged: (fresh, seen) => sameValue(fresh[field], seen[field]),
              });
              const next: TransportOrderRow = { ...row, customerReference: result.customerReference, internalRemarks: result.internalRemarks, concurrencyStamp: result.concurrencyStamp };
              rows.current.set(row.id!, next);
              grid.current()?.applyServerSideTransaction({ update: [next] });
            } catch (error) {
              toast.error(getApiErrorMessage(error, t));
              throw error;
            }
          }}
        />
      ),
      ...extra,
    });
    return [
      ...(canDrag
        ? [
            {
              ...DRAG_COLUMN,
              cellRenderer: (p: { data: TransportOrderRow | undefined }) => (
                <DragHandleCell<TransportOrderRow> data={p.data} labelKey="Planboard:Lists.DragOrderToTrip" payloadOf={(row) => (row.id ? { kind: "order", orderId: row.id } : null)} />
              ),
            } satisfies ColDef<TransportOrderRow>,
          ]
        : []),
      { field: "number", headerName: t("Orders:Number"), minWidth: 130 },
      { field: "status", headerName: t("Orders:Status"), minWidth: 120, cellRenderer: (p: { value: TransportOrderStatus | undefined }) => <StatusCell entity="TransportOrder" value={orderStatusName(p.value)} /> },
      { field: "orderDate", headerName: t("Orders:OrderDate"), minWidth: 110, cellRenderer: DateCell },
      { field: "customerName", headerName: t("Orders:Customer"), minWidth: 160 },
      textColumn("customerReference", t("Orders:CustomerReference"), PLANBOARD_ORDER_FIELD.CustomerReference, 128),
      textColumn("internalRemarks", t("Orders:InternalRemarks"), PLANBOARD_ORDER_FIELD.InternalRemarks, 2500, { minWidth: 180, sortable: false }),
      { colId: "firstStop", headerName: t("Orders:FirstStop"), minWidth: 170, sortable: false, valueGetter: (p) => [p.data?.firstStopName, p.data?.firstStopCity].filter(Boolean).join(", ") },
      { field: "firstStopDate", headerName: t("Orders:FirstStopDate"), minWidth: 110, cellRenderer: DateCell },
      { colId: "lastStop", headerName: t("Orders:LastStop"), minWidth: 170, sortable: false, valueGetter: (p) => [p.data?.lastStopName, p.data?.lastStopCity].filter(Boolean).join(", ") },
      { field: "lastStopDate", headerName: t("Orders:LastStopDate"), minWidth: 110, cellRenderer: DateCell },
      { field: "lineCount", headerName: t("Orders:LineCount"), minWidth: 80, cellRenderer: NumberCell, type: "rightAligned" },
      { field: "executionProgress", headerName: t("Orders:ExecutionProgress"), minWidth: 130, valueFormatter: (p) => t(EXECUTION_PROGRESSES.find((e) => e.value === p.value)?.key ?? "") },
      { field: "tripNumbers", headerName: t("Orders:TripNumbers"), minWidth: 130, sortable: false },
      { field: "transportOrderTypeName", headerName: t("Orders:TransportOrderType"), minWidth: 140, hide: true, sortable: false },
      { field: "revenueAmount", headerName: t("Orders:RevenueAmount"), minWidth: 110, hide: true, cellRenderer: AmountCell, type: "rightAligned", sortable: false },
    ];
  }, [t, canDrag, canEdit, updateField, grid]);

  if (!canView) return <PermissionDenied />;

  return (
    <div className="flex h-full min-h-0 flex-col p-2" data-slot="planboard-orders-pane">
      <LazyOpenTmsGrid<TransportOrderRow>
        gridId={GRID_ID}
        columnDefs={columns}
        fetchRows={fetchRows}
        onSelectionChange={onSelectionChange}
        clickSelects
        popupsOnPage
        onGridReady={grid.capture}
        onOpen={(row) => row.id && openRecord("TransportOrder", row.id, { returnTo: "/planboard" })}
        rowActions={(list) => [{ id: "open", labelKey: "PlanningExecution:OpenOrder", onClick: () => list[0]?.id && openRecord("TransportOrder", list[0].id, { returnTo: "/planboard" }) }]}
        filters={<PaneFilterBar instanceId={instanceId} paneFilters={paneFilters} />}
      />
    </div>
  );
}
