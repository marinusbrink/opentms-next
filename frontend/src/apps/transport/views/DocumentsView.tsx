import { useMemo, useState } from "react";
import type { ColDef } from "ag-grid-community";
import { Download } from "lucide-react";
import { toast } from "sonner";

import { useAppFeatures } from "@/app/app-features";
import { AppCommandBar } from "@/components/ui/app-command-bar";
import { DatePicker } from "@/components/ui/date-picker";
import { FeatureUnavailable } from "@/components/ui/feature-unavailable";
import { BytesCell, DateTimeCell, NumberCell } from "@/components/ui/grid-cells";
import { LazyOpenTmsGrid } from "@/components/ui/opentms-grid-lazy";
import { PermissionDenied } from "@/components/ui/permission-denied";
import { deliverBlob, downloadConsignmentNote, useConsignmentNotesGridFetcher, type ConsignmentNoteDocumentDto } from "@/domains/orders/consignment-notes";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { useOpenRecord } from "@/lib/use-open-record";

/* Design 61, "Documents view": the printed CMRs of the tenant (number, printed at, order, trip, lines, profile, by;
 * download; search by order number through the grid's search) on a full-height OpenTmsGrid. Double click opens the
 * order; the row action downloads the PDF through the authenticated client. */
export function DocumentsView() {
  const { t } = useL();
  const features = useAppFeatures();
  const openRecord = useOpenRecord();
  const canView = useIsGranted("Orders.TransportOrders");
  const [printedFrom, setPrintedFrom] = useState<string | null>(null);
  const [printedTo, setPrintedTo] = useState<string | null>(null);
  const fetchRows = useConsignmentNotesGridFetcher({ printedFrom: printedFrom ?? undefined, printedTo: printedTo ?? undefined });

  const download = async (row: ConsignmentNoteDocumentDto) => {
    try {
      const { blob, fileName } = await downloadConsignmentNote(row.id!);
      deliverBlob(blob, row.fileName ?? fileName, "save");
    } catch (err) {
      toast.error(getApiErrorMessage(err, t));
    }
  };

  const columns = useMemo<ColDef<ConsignmentNoteDocumentDto>[]>(
    () => [
      { field: "number", headerName: t("Orders:Number"), minWidth: 150 },
      { field: "printedAt", headerName: t("Orders:PrintedAt"), minWidth: 150, cellRenderer: DateTimeCell },
      { field: "orderNumber", headerName: t("Orders:TransportOrder"), minWidth: 140 },
      { field: "tripId", headerName: t("Orders:Trip"), minWidth: 120, hide: true, sortable: false },
      { colId: "lines", headerName: t("Orders:Lines"), minWidth: 80, type: "rightAligned", valueGetter: (p) => (p.data?.lineIds ?? []).length, cellRenderer: NumberCell, sortable: false },
      { field: "profileName", headerName: t("Orders:Profile"), minWidth: 140, sortable: false },
      { field: "printedByUserId", headerName: t("Orders:PrintedBy"), minWidth: 140, hide: true, sortable: false },
      { field: "fileName", headerName: t("Orders:FileName"), flex: 1, minWidth: 180, sortable: false },
      { field: "sizeBytes", headerName: t("Orders:FileSize"), minWidth: 100, cellRenderer: BytesCell, type: "rightAligned", hide: true, sortable: false },
    ],
    [t],
  );

  if (features.isLoaded && !features.isFeatureEnabled("Apps.Transport.Documents")) return <FeatureUnavailable />;
  if (!canView) return <PermissionDenied />;

  return (
    <div className="flex h-full flex-col" data-slot="documents-view">
      <AppCommandBar commands={[]} />
      <div className="min-h-0 flex-1 px-6 pb-6">
        <LazyOpenTmsGrid<ConsignmentNoteDocumentDto>
          gridId="orders.transport.consignmentNotes"
          columnDefs={columns}
          fetchRows={fetchRows}
          onOpen={(row) => row.orderId && openRecord("TransportOrder", row.orderId)}
          rowActions={(rows) => [
            { id: "download", labelKey: "Orders:Download", icon: Download, onClick: () => void download(rows[0]) },
            { id: "order", labelKey: "PlanningExecution:OpenOrder", onClick: () => rows[0]?.orderId && openRecord("TransportOrder", rows[0].orderId) },
          ]}
          filters={
            <>
              <DatePicker id="documents-from" value={printedFrom} onChange={setPrintedFrom} placeholder={t("Financial:From")} className="w-36" />
              <DatePicker id="documents-to" value={printedTo} onChange={setPrintedTo} placeholder={t("Financial:To")} className="w-36" />
            </>
          }
        />
      </div>
    </div>
  );
}
