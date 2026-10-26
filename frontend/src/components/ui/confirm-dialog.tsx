import { useEffect, useRef, useState, type ReactNode } from "react";

import {
  AlertDialog,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* Confirmations (UI-DLG-001/011, UI-ACT-003/005, UI-KEY-001/003): the title names the
 * action and the object, the primary is the verb, destructive actions are red and
 * start with focus on Cancel. A failure shows inside the dialog, which stays open. */
export interface ConfirmDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  description: string;
  confirmLabel: string;
  /** The label while the action runs, e.g. t("Deleting"). Defaults to the confirm label. */
  busyLabel?: string;
  /** The label of the button that closes without acting; defaults to t("Cancel") (e.g. "Keep editing"). */
  cancelLabel?: string;
  destructive?: boolean;
  onConfirm: () => Promise<void> | void;
  /** Keeps the confirm button disabled, e.g. until a typed confirmation matches (irreversible actions). */
  confirmDisabled?: boolean;
  children?: ReactNode;
}

export function ConfirmDialog({
  open,
  onOpenChange,
  title,
  description,
  confirmLabel,
  busyLabel,
  cancelLabel,
  destructive,
  onConfirm,
  confirmDisabled = false,
  children,
}: ConfirmDialogProps) {
  const { t } = useL();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const cancelRef = useRef<HTMLButtonElement>(null);
  const confirmRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (!open) {
      setError(null);
      setBusy(false);
    }
  }, [open]);

  const handleConfirm = async () => {
    setError(null);
    setBusy(true);
    try {
      await onConfirm();
    } catch (err) {
      setError(getApiErrorMessage(err, t));
      setBusy(false);
      return;
    }
    setBusy(false);
  };

  return (
    <AlertDialog
      open={open}
      onOpenChange={(next) => {
        if (!next && busy) return;
        onOpenChange(next);
      }}
    >
      <AlertDialogContent
        className="max-w-[calc(100%-2rem)] sm:max-w-dialog-confirm"
        initialFocus={destructive ? cancelRef : confirmRef}
      >
        <AlertDialogHeader className="place-items-start text-left">
          <AlertDialogTitle className="text-title font-semibold">{title}</AlertDialogTitle>
          <AlertDialogDescription>{description}</AlertDialogDescription>
        </AlertDialogHeader>

        {children}

        {error && (
          <Alert variant="destructive">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}

        <div className="flex justify-end gap-2">
          <Button ref={cancelRef} variant="outline" onClick={() => onOpenChange(false)} disabled={busy}>
            {cancelLabel ?? t("Cancel")}
          </Button>
          <Button
            ref={confirmRef}
            variant={destructive ? "destructive" : "default"}
            onClick={() => void handleConfirm()}
            disabled={busy || confirmDisabled}
          >
            {busy ? (busyLabel ?? confirmLabel) : confirmLabel}
          </Button>
        </div>
      </AlertDialogContent>
    </AlertDialog>
  );
}
