import { useCallback, useEffect, useMemo, useRef } from "react";
import type { ColDef } from "ag-grid-community";

import { usePaneFilters } from "@/apps/planboard/filters";
import { PaneFilterBar } from "@/apps/planboard/panes/lists/PaneFilterBar";
import { setSelectedOrder } from "@/apps/planboard/panes/lists/selected-order";
import type { PlanboardPaneProps } from "@/apps/planboard/panes/registry";
import { AmountCell, DateCell, NumberCell } from "@/components/ui/grid-cells";
import type { GridRequest, GridSelectionDto } from "@/components/ui/opentms-grid";
import { LazyOpenTmsGrid } from "@/components/ui/opentms-grid-lazy";
import { PermissionDenied } from "@/components/ui/permission-denied";
import { StatusCell } from "@/components/ui/status-badge";
import { EXECUTION_PROGRESSES, ORDER_VIEWS, fetchTransportOrders, orderStatusName, type TransportOrderRow, type TransportOrderStatus } from "@/domains/orders/transport-orders";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { useOpenRecord } from "@/lib/use-open-record";

/* Design 70, "Transport orders": the orders of the board's range (or the pane's own) on Orders' existing list endpoint,
 * read-only; double click or Open opens the order page in the Transport app. A single selected order is offered to Legs
 * to schedule as its "this order" filter.
 *
 * The Orders list filters on one organisation unit only: with exactly one unit chosen the list filters on it, with
 * several it shows every unit (reported; the endpoint takes no list of units). */

const GRID_ID = "orders.planboard.transportOrders";

export default function OrdersPane({ instanceId }: PlanboardPaneProps) {
  const { t } = useL();
  const openRecord = useOpenRecord();
  const canView = useIsGranted("Orders.TransportOrders");
  const paneFilters = usePaneFilters(instanceId);
  const { range, organizationUnitIds } = paneFilters.filters;
  const filtersKey = JSON.stringify({ range, organizationUnitIds });
  const rows = useRef(new Map<string, TransportOrderRow>());

  const fetchRows = useCallback(
    async (request: GridRequest) => {
      const { range: r, organizationUnitIds: units } = JSON.parse(filtersKey) as { range: { from: string; till: string }; organizationUnitIds: string[] };
      const page = await fetchTransportOrders(request, { view: ORDER_VIEWS.all, dateFrom: r.from, dateTo: r.till, organizationUnitId: units.length === 1 ? units[0] : undefined });
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

  const columns = useMemo<ColDef<TransportOrderRow>[]>(
    () => [
      { field: "number", headerName: t("Orders:Number"), minWidth: 130 },
      { field: "status", headerName: t("Orders:Status"), minWidth: 120, cellRenderer: (p: { value: TransportOrderStatus | undefined }) => <StatusCell entity="TransportOrder" value={orderStatusName(p.value)} /> },
      { field: "orderDate", headerName: t("Orders:OrderDate"), minWidth: 110, cellRenderer: DateCell },
      { field: "customerName", headerName: t("Orders:Customer"), minWidth: 160 },
      { field: "customerReference", headerName: t("Orders:CustomerReference"), minWidth: 140 },
      { colId: "firstStop", headerName: t("Orders:FirstStop"), minWidth: 170, sortable: false, valueGetter: (p) => [p.data?.firstStopName, p.data?.firstStopCity].filter(Boolean).join(", ") },
      { field: "firstStopDate", headerName: t("Orders:FirstStopDate"), minWidth: 110, cellRenderer: DateCell },
      { colId: "lastStop", headerName: t("Orders:LastStop"), minWidth: 170, sortable: false, valueGetter: (p) => [p.data?.lastStopName, p.data?.lastStopCity].filter(Boolean).join(", ") },
      { field: "lastStopDate", headerName: t("Orders:LastStopDate"), minWidth: 110, cellRenderer: DateCell },
      { field: "lineCount", headerName: t("Orders:LineCount"), minWidth: 80, cellRenderer: NumberCell, type: "rightAligned" },
      { field: "executionProgress", headerName: t("Orders:ExecutionProgress"), minWidth: 130, valueFormatter: (p) => t(EXECUTION_PROGRESSES.find((e) => e.value === p.value)?.key ?? "") },
      { field: "tripNumbers", headerName: t("Orders:TripNumbers"), minWidth: 130, sortable: false },
      { field: "transportOrderTypeName", headerName: t("Orders:TransportOrderType"), minWidth: 140, hide: true, sortable: false },
      { field: "revenueAmount", headerName: t("Orders:RevenueAmount"), minWidth: 110, hide: true, cellRenderer: AmountCell, type: "rightAligned", sortable: false },
    ],
    [t],
  );

  if (!canView) return <PermissionDenied />;

  return (
    <div className="flex h-full min-h-0 flex-col p-2" data-slot="planboard-orders-pane">
      <LazyOpenTmsGrid<TransportOrderRow>
        gridId={GRID_ID}
        columnDefs={columns}
        fetchRows={fetchRows}
        onSelectionChange={onSelectionChange}
        onOpen={(row) => row.id && openRecord("TransportOrder", row.id)}
        rowActions={(list) => [{ id: "open", labelKey: "PlanningExecution:OpenOrder", onClick: () => list[0]?.id && openRecord("TransportOrder", list[0].id) }]}
        filters={<PaneFilterBar instanceId={instanceId} paneFilters={paneFilters} />}
      />
    </div>
  );
}
