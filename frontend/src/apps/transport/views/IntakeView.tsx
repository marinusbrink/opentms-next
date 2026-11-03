import { useMemo, useState } from "react";
import type { ColDef } from "ag-grid-community";
import { CircleX, ClipboardList, FileSearch } from "lucide-react";
import { toast } from "sonner";

import { useAppFeatures } from "@/app/app-features";
import { AppCommandBar } from "@/components/ui/app-command-bar";
import { DatePicker } from "@/components/ui/date-picker";
import { FeatureUnavailable } from "@/components/ui/feature-unavailable";
import { DateTimeCell, NumberCell } from "@/components/ui/grid-cells";
import { MultiSelect } from "@/components/ui/multi-select";
import type { GridRowAction } from "@/components/ui/opentms-grid";
import { LazyOpenTmsGrid } from "@/components/ui/opentms-grid-lazy";
import { PermissionDenied } from "@/components/ui/permission-denied";
import { StatusCell } from "@/components/ui/status-badge";
import { RejectIntakeDialog } from "@/apps/transport/intake/RejectIntakeDialog";
import { ORDER_INTAKE_FEATURE, channelLabel } from "@/apps/transport/intake/review-model";
import {
  ORDER_INTAKE_STATUSES,
  orderIntakeStatusName,
  useOrderIntakesGridFetcher,
  type OrderIntakeRow,
  type OrderIntakeStatus,
} from "@/domains/orders/order-intakes";
import { useFeatureState, useIsGranted } from "@/lib/auth/permissions";
import { toIsoDate } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { useOpenRecord } from "@/lib/use-open-record";

/* Transport app → Intake (design 72 "UI design"): the review queue and the history of order intakes on a full-height
 * server-side grid (UI-PAGE-001). It opens on Needs review (the status filter's default) and the last six months
 * (UI-GRID-005); opening a row opens the review page through useOpenRecord. Row actions: Review, Open order, Reject. */

function sixMonthsAgo(): string {
  const date = new Date();
  date.setMonth(date.getMonth() - 6);
  return toIsoDate(date);
}

const NEEDS_REVIEW: OrderIntakeStatus = 1;

export function IntakeView() {
  const { t } = useL();
  const features = useAppFeatures();
  const feature = useFeatureState(ORDER_INTAKE_FEATURE);
  const openRecord = useOpenRecord();
  const canView = useIsGranted("Orders.OrderIntakes");
  const canReject = useIsGranted("Orders.OrderIntakes.Reject");
  const [statuses, setStatuses] = useState<OrderIntakeStatus[]>([NEEDS_REVIEW]);
  const [receivedFrom, setReceivedFrom] = useState<string | null>(sixMonthsAgo);
  const [receivedTo, setReceivedTo] = useState<string | null>(null);
  const [refreshKey, setRefreshKey] = useState(0);
  const [rejecting, setRejecting] = useState<string | null>(null);
  const filters = useMemo(() => ({ status: statuses, receivedFrom: receivedFrom ?? undefined, receivedTo: receivedTo ?? undefined }), [statuses, receivedFrom, receivedTo]);
  const fetchRows = useOrderIntakesGridFetcher(filters, refreshKey);

  const columns = useMemo<ColDef<OrderIntakeRow>[]>(
    () => [
      { colId: "receivedAt", field: "receivedAt", headerName: t("Orders:IntakeReceivedAt"), minWidth: 150, cellRenderer: DateTimeCell },
      { field: "channel", headerName: t("Orders:IntakeChannel"), minWidth: 120, valueFormatter: (p) => channelLabel(t, p.value as string) },
      { field: "senderAddress", headerName: t("Orders:IntakeSender"), flex: 1, minWidth: 180 },
      { colId: "subject", field: "subject", headerName: t("Orders:IntakeSubjectOrFile"), flex: 2, minWidth: 200, valueGetter: (p) => p.data?.subject || p.data?.fileName || "" },
      { field: "customerName", headerName: t("Orders:Customer"), flex: 1, minWidth: 160 },
      { field: "status", headerName: t("Orders:Status"), minWidth: 130, cellRenderer: (p: { value: OrderIntakeStatus | undefined }) => <StatusCell entity="OrderIntake" value={orderIntakeStatusName(p.value)} /> },
      { field: "openFieldCount", headerName: t("Orders:IntakeOpenFields"), minWidth: 110, cellRenderer: NumberCell, type: "rightAligned" },
      { field: "customerReference", headerName: t("Orders:CustomerReference"), minWidth: 140, hide: true },
      { field: "fileName", headerName: t("Orders:IntakeFileName"), minWidth: 160, hide: true },
      { field: "orderNumber", headerName: t("Orders:Number"), minWidth: 130, hide: true, sortable: false },
    ],
    [t],
  );

  if (feature === "off" || (features.isLoaded && !features.isFeatureEnabled("Apps.Transport.Intake"))) return <FeatureUnavailable />;
  if (!canView) return <PermissionDenied />;

  const rowActions = (rows: OrderIntakeRow[]): GridRowAction[] => {
    const row = rows[0];
    const id = row?.id;
    if (!id) return [];
    return [
      { id: "review", labelKey: "Orders:IntakeReview", icon: FileSearch, onClick: () => openRecord("OrderIntake", id) },
      ...(row.transportOrderId ? [{ id: "open-order", labelKey: "Orders:IntakeOpenOrder", icon: ClipboardList, onClick: () => openRecord("TransportOrder", row.transportOrderId!) }] : []),
      ...(canReject && row.status === NEEDS_REVIEW ? [{ id: "reject", labelKey: "Orders:Reject", icon: CircleX, destructive: true, onClick: () => setRejecting(id) }] : []),
    ];
  };

  return (
    <div className="flex h-full flex-col" data-slot="intake-view">
      <AppCommandBar commands={[]} />
      <div className="min-h-0 flex-1 px-6 pb-6">
        <LazyOpenTmsGrid<OrderIntakeRow>
          gridId="orders.transport.orderIntakes"
          columnDefs={columns}
          fetchRows={fetchRows}
          onOpen={(row) => row.id && openRecord("OrderIntake", row.id)}
          rowActions={rowActions}
          filters={
            <>
              <DatePicker id="intake-received-from-filter" value={receivedFrom} onChange={setReceivedFrom} placeholder={t("Orders:IntakeReceivedFrom")} className="w-36" />
              <DatePicker id="intake-received-to-filter" value={receivedTo} onChange={setReceivedTo} placeholder={t("Orders:IntakeReceivedTo")} className="w-36" />
              <MultiSelect
                id="intake-status-filter"
                value={statuses.map(String)}
                onChange={(next) => setStatuses(next.map(Number) as OrderIntakeStatus[])}
                options={Object.entries(ORDER_INTAKE_STATUSES).map(([value, name]) => ({ id: value, label: t(`Orders:OrderIntakeStatus.${name}`) }))}
                className="w-44"
              />
            </>
          }
        />
      </div>
      <RejectIntakeDialog
        intakeId={rejecting}
        onOpenChange={(next) => !next && setRejecting(null)}
        onRejected={() => {
          setRejecting(null);
          toast.success(t("Orders:IntakeRejected"));
          setRefreshKey((k) => k + 1);
        }}
      />
    </div>
  );
}
