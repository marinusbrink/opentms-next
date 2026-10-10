import { useState } from "react";
import { TriangleAlert } from "lucide-react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { EntityDialog } from "@/components/ui/entity-dialog";
import type { GridSelectionDto } from "@/components/ui/opentms-grid";
import { useBulkDeleteRoles, type BulkDeleteRolesResult } from "@/domains/platform/administration-roles";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useL } from "@/lib/i18n/LocalizationProvider";

interface BulkDeleteRolesDialogProps {
  selection: GridSelectionDto;
  selectionCount: number;
  open: boolean;
  onClose: () => void;
  onSuccess: () => void;
}

type Step = "confirm" | "result";

/* A destructive action dialog (UI-DLG-002, UI-ACT-005): the body states the consequence in
 * an Alert and the primary is the verb, so the dialog itself is the confirmation. After the
 * run it shows the per-item outcome; the summary toast follows on Close (UI-FB-002). */
export function BulkDeleteRolesDialog({
  selection,
  selectionCount,
  open,
  onClose,
  onSuccess,
}: BulkDeleteRolesDialogProps) {
  const { t } = useL();
  const bulkDelete = useBulkDeleteRoles();

  const [step, setStep] = useState<Step>("confirm");
  const [result, setResult] = useState<BulkDeleteRolesResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  const handleClose = () => {
    if (bulkDelete.isPending) return;
    if (step === "result" && result) {
      onSuccess();
      toast.success(t("Administration:RolesDeleted").replace("{0}", String(result.deletedCount)));
    }
    onClose();
  };

  const handleDelete = async () => {
    setError(null);
    try {
      const r = await bulkDelete.mutateAsync(selection);
      setResult(r);
      setStep("result");
    } catch (err) {
      setError(getApiErrorMessage(err, t));
    }
  };

  const footer =
    step === "confirm" ? (
      <>
        <Button variant="outline" onClick={handleClose} disabled={bulkDelete.isPending}>
          {t("Cancel")}
        </Button>
        <Button variant="destructive" onClick={() => void handleDelete()} disabled={bulkDelete.isPending}>
          {bulkDelete.isPending ? t("Deleting") : t("Delete")}
        </Button>
      </>
    ) : (
      <Button onClick={handleClose}>{t("Close")}</Button>
    );

  return (
    <EntityDialog
      open={open}
      onOpenChange={(next) => {
        if (!next) handleClose();
      }}
      variant="action"
      size="sm"
      title={t("Administration:DeleteRoles")}
      isBusy={bulkDelete.isPending}
      footer={footer}
    >
      {step === "confirm" && (
        <div className="flex flex-col gap-3">
          <p className="text-sm">{t("Administration:ConfirmBulkDeleteRoles").replace("{0}", String(selectionCount))}</p>

          {error && (
            <Alert variant="destructive">
              <AlertDescription>{error}</AlertDescription>
            </Alert>
          )}

          <Alert variant="destructive">
            <TriangleAlert />
            <AlertDescription>{t("ThisCannotBeUndone")}</AlertDescription>
          </Alert>
        </div>
      )}

      {step === "result" && result && (
        <div className="flex flex-col gap-3">
          <p className="text-sm">{t("Administration:Deleted").replace("{0}", String(result.deletedCount))}</p>

          {result.skippedRows.length > 0 && (
            <div>
              <p className="mb-1 text-sm font-medium">
                {t("Administration:Skipped").replace("{0}", String(result.skippedRows.length))}
              </p>
              <ul className="space-y-1 text-sm text-muted-foreground">
                {result.skippedRows.map((row) => (
                  <li key={row.id}>
                    <span className="font-medium text-foreground">{row.name}</span>
                    {": "}
                    <span>{t(row.reason)}</span>
                  </li>
                ))}
              </ul>
            </div>
          )}
        </div>
      )}
    </EntityDialog>
  );
}
