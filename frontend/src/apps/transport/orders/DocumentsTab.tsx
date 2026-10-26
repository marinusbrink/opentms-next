import { useState } from "react";
import { Download, Printer } from "lucide-react";
import { toast } from "sonner";

import { Button } from "@/components/ui/button";
import { FormSection } from "@/components/ui/form-section";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { deliverBlob, downloadConsignmentNote, useConsignmentNotesOf } from "@/domains/orders/consignment-notes";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The Documents tab of an order or a trip (design 61): the printed CMRs (number, printed at, profile, file; download)
 * and the Print CMR command. A fixed, read-only list of a few documents, so a Table (UI-GRID-001 allows it); the
 * tenant-wide list is the Documents view's OpenTmsGrid. Attachments come later (design 61). */

export interface DocumentsTabProps {
  orderId?: string;
  tripId?: string;
  /** Opens the Print CMR wizard; absent without the Print permission (UI-PERM-002). */
  onPrint?: () => void;
}

export function DocumentsTab({ orderId, tripId, onPrint }: DocumentsTabProps) {
  const { t } = useL();
  const f = useFormat();
  const documents = useConsignmentNotesOf({ orderId, tripId }, orderId != null || tripId != null);
  const [busyId, setBusyId] = useState<string | null>(null);

  const download = async (id: string) => {
    setBusyId(id);
    try {
      const { blob, fileName } = await downloadConsignmentNote(id);
      deliverBlob(blob, fileName, "save");
    } catch (err) {
      toast.error(getApiErrorMessage(err, t));
    } finally {
      setBusyId(null);
    }
  };

  const rows = documents.data?.rows ?? [];
  return (
    <FormSection title={t("Orders:ConsignmentNotes")}>
      {onPrint && (
        <div className="mb-3">
          <Button type="button" size="sm" variant="outline" onClick={onPrint}>
            <Printer /> {t("Orders:PrintConsignmentNote")}
          </Button>
        </div>
      )}
      {rows.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t("Orders:NoDocuments")}</p>
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>{t("Orders:Number")}</TableHead>
              <TableHead>{t("Orders:PrintedAt")}</TableHead>
              {tripId && <TableHead>{t("Orders:TransportOrder")}</TableHead>}
              <TableHead>{t("Orders:Profile")}</TableHead>
              <TableHead>{t("Orders:FileName")}</TableHead>
              <TableHead />
            </TableRow>
          </TableHeader>
          <TableBody>
            {rows.map((row) => (
              <TableRow key={row.id}>
                <TableCell>{row.number}</TableCell>
                <TableCell className="tabular-nums">{f.dateTime(row.printedAt)}</TableCell>
                {tripId && <TableCell>{row.orderNumber}</TableCell>}
                <TableCell>{row.profileName}</TableCell>
                <TableCell>{row.fileName}</TableCell>
                <TableCell className="text-right">
                  <Button type="button" variant="ghost" size="icon-sm" aria-label={t("Orders:Download")} title={t("Orders:Download")} disabled={busyId === row.id} onClick={() => void download(row.id!)}>
                    <Download />
                  </Button>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </FormSection>
  );
}
