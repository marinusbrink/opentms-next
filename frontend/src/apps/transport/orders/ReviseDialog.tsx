import { useState, type FormEvent } from "react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { EntityDialog } from "@/components/ui/entity-dialog";
import { FormField } from "@/components/ui/form-field";
import { Textarea } from "@/components/ui/textarea";
import { useReviseTransportOrder } from "@/domains/orders/transport-orders";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* Revise (design 58, Q15): ReadyForInvoicing or Archived back to Open with a required reason, stored as a revision
 * remark. A small action dialog: Enter submits (UI-KEY-001), the verb is Revise (UI-DLG-013). */

export interface ReviseDialogProps {
  orderId: string;
  number: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function ReviseDialog({ orderId, number, open, onOpenChange }: ReviseDialogProps) {
  const { t } = useL();
  const revise = useReviseTransportOrder();
  const [reason, setReason] = useState("");
  const [submitted, setSubmitted] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const reasonError = submitted && !reason.trim() ? t("OpenTms.Orders:RevisionReasonRequired") : undefined;

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setSubmitted(true);
    if (!reason.trim()) return;
    setError(null);
    try {
      await revise.mutateAsync({ id: orderId, reason: reason.trim() });
      toast.success(t("Orders:TransportOrderRevised"));
      setReason("");
      setSubmitted(false);
      onOpenChange(false);
    } catch (err) {
      setError(getApiErrorMessage(err, t));
    }
  };

  return (
    <EntityDialog
      open={open}
      onOpenChange={onOpenChange}
      variant="action"
      size="sm"
      title={`${t("Orders:ReviseTransportOrder")} ${number}`}
      isBusy={revise.isPending}
      footer={
        <>
          <Button type="button" variant="outline" onClick={() => onOpenChange(false)} disabled={revise.isPending}>
            {t("Cancel")}
          </Button>
          <Button type="submit" form="revise-form" disabled={revise.isPending}>
            {revise.isPending ? t("Saving") : t("Orders:Revise")}
          </Button>
        </>
      }
    >
      <form id="revise-form" onSubmit={(e) => void submit(e)} noValidate className="grid grid-cols-1 gap-3">
        {error && (
          <Alert variant="destructive">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}
        <FormField label={t("Orders:RevisionReason")} htmlFor="revise-reason" required error={reasonError} floating="always">
          <Textarea id="revise-reason" rows={3} value={reason} maxLength={1000} autoFocus onChange={(e) => setReason(e.target.value)} />
        </FormField>
      </form>
    </EntityDialog>
  );
}
