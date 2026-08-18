import { useState } from "react";
import { Dialog } from "@base-ui/react/dialog";
import { Button } from "@/components/ui/button";
import { useBulkDeleteUsers, type BulkDeleteResult } from "@/domains/platform/administration-users";
import { useL } from "@/lib/i18n/LocalizationProvider";
import type { GridSelectionDto } from "@/components/ui/opentms-grid";

interface BulkDeleteUsersDialogProps {
  selection: GridSelectionDto;
  selectedCount: number;
  open: boolean;
  onClose: () => void;
  onSuccess: () => void;
}

function extractApiError(error: unknown): string {
  if (!error || typeof error !== "object") return String(error ?? "");
  const e = error as Record<string, unknown>;
  const abpMsg = (e["error"] as Record<string, unknown> | undefined)?.["message"];
  if (typeof abpMsg === "string") return abpMsg;
  if (typeof e["title"] === "string") return e["title"];
  return "";
}

type Step = "confirm" | "result";

export function BulkDeleteUsersDialog({
  selection,
  selectedCount,
  open,
  onClose,
  onSuccess,
}: BulkDeleteUsersDialogProps) {
  const { t } = useL();
  const bulkDelete = useBulkDeleteUsers();

  const [step, setStep] = useState<Step>("confirm");
  const [result, setResult] = useState<BulkDeleteResult | null>(null);
  const [error, setError] = useState("");

  const handleClose = () => {
    if (bulkDelete.isPending) return;
    setStep("confirm");
    setResult(null);
    setError("");
    if (step === "result") onSuccess();
    onClose();
  };

  const handleDelete = async () => {
    setError("");
    try {
      const r = await bulkDelete.mutateAsync(selection);
      setResult(r);
      setStep("result");
    } catch (err) {
      setError(extractApiError(err) || t("Administration:DeleteFailed"));
    }
  };

  const confirmMessage = t("Administration:ConfirmBulkDelete").replace(
    "{0}",
    String(selectedCount),
  );

  return (
    <Dialog.Root open={open} onOpenChange={(o: boolean) => { if (!o) handleClose(); }}>
      <Dialog.Portal>
        <Dialog.Backdrop className="fixed inset-0 z-40 bg-black/50 data-open:animate-in data-open:fade-in-0 data-closed:animate-out data-closed:fade-out-0 duration-150" />
        <Dialog.Popup className="fixed left-1/2 top-1/2 z-50 w-full max-w-md -translate-x-1/2 -translate-y-1/2 rounded-xl bg-background p-6 shadow-xl ring-1 ring-foreground/10 data-open:animate-in data-open:fade-in-0 data-open:zoom-in-95 data-closed:animate-out data-closed:fade-out-0 data-closed:zoom-out-95 duration-150">
          <Dialog.Title className="mb-4 text-base font-semibold">
            {t("Administration:BulkDelete")}
          </Dialog.Title>

          {step === "confirm" && (
            <div className="flex flex-col gap-4">
              <p className="text-sm">{confirmMessage}</p>

              {error && <p className="text-sm text-destructive">{error}</p>}

              <div className="flex justify-end gap-2">
                <Button
                  variant="outline"
                  onClick={handleClose}
                  disabled={bulkDelete.isPending}
                >
                  {t("Administration:Cancel")}
                </Button>
                <Button
                  variant="destructive"
                  onClick={() => { void handleDelete(); }}
                  disabled={bulkDelete.isPending}
                >
                  {bulkDelete.isPending
                    ? t("Administration:Deleting")
                    : t("Administration:DeleteUser")}
                </Button>
              </div>
            </div>
          )}

          {step === "result" && result && (
            <div className="flex flex-col gap-4">
              <p className="text-sm">
                {t("Administration:Deleted").replace("{0}", String(result.deletedCount))}
              </p>

              {result.skippedRows.length > 0 && (
                <div>
                  <p className="mb-1 text-sm font-medium">
                    {t("Administration:Skipped").replace("{0}", String(result.skippedRows.length))}
                  </p>
                  <ul className="space-y-1 text-sm text-muted-foreground">
                    {result.skippedRows.map((row) => (
                      <li key={row.id} className="flex gap-1">
                        <span className="font-medium text-foreground">{row.userName}</span>
                        <span>—</span>
                        <span>{t(row.reason)}</span>
                      </li>
                    ))}
                  </ul>
                </div>
              )}

              <div className="flex justify-end">
                <Button onClick={handleClose}>{t("Administration:Close")}</Button>
              </div>
            </div>
          )}
        </Dialog.Popup>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
