import { useEffect, useState } from "react";
import { TriangleAlert } from "lucide-react";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { EntityDialog } from "@/components/ui/entity-dialog";
import { FormField } from "@/components/ui/form-field";
import { Textarea } from "@/components/ui/textarea";
import { useRejectOrderIntake } from "@/domains/orders/order-intakes";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useL } from "@/lib/i18n/LocalizationProvider";

export interface RejectIntakeDialogProps {
  /** The intake to reject; null: closed. */
  intakeId: string | null;
  onOpenChange: (open: boolean) => void;
  onRejected: () => void;
}

/** The reason's maximum length (OrderIntake.RejectReason, design 72). */
const MAX_REASON = 512;

/* Reject an intake (design 72 "Review"): an action dialog with the reason (required). The consequence is stated in the
 * dialog (UI-FB-008), so the destructive primary is the confirmation (UI-ACT-005); a failure stays in the dialog. */
export function RejectIntakeDialog({ intakeId, onOpenChange, onRejected }: RejectIntakeDialogProps) {
  const { t } = useL();
  const reject = useRejectOrderIntake();
  const [reason, setReason] = useState("");
  const [touched, setTouched] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (intakeId) {
      setReason("");
      setTouched(false);
      setError(null);
    }
  }, [intakeId]);

  const reasonError = touched && !reason.trim() ? t("MasterData:Required") : undefined;

  const submit = async () => {
    setTouched(true);
    if (!intakeId || !reason.trim()) return;
    setError(null);
    try {
      await reject.mutateAsync({ id: intakeId, reason: reason.trim() });
      onRejected();
    } catch (err) {
      setError(getApiErrorMessage(err, t));
    }
  };

  const footer = (
    <div className="flex justify-end gap-2">
      <Button variant="outline" onClick={() => onOpenChange(false)} disabled={reject.isPending}>
        {t("Cancel")}
      </Button>
      <Button variant="destructive" onClick={() => void submit()} disabled={reject.isPending}>
        {reject.isPending ? t("Orders:Rejecting") : t("Orders:Reject")}
      </Button>
    </div>
  );

  return (
    <EntityDialog
      open={intakeId != null}
      onOpenChange={onOpenChange}
      variant="action"
      size="sm"
      title={t("Orders:RejectIntake")}
      isBusy={reject.isPending}
      isDirty={reason.trim() !== ""}
      footer={footer}
    >
      <div className="grid grid-cols-1 gap-3">
        {error && (
          <Alert variant="destructive">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}
        <FormField label={t("Orders:RejectReason")} htmlFor="reject-intake-reason" required error={reasonError} floating="always">
          <Textarea id="reject-intake-reason" value={reason} maxLength={MAX_REASON} rows={3} autoFocus onChange={(e) => setReason(e.target.value)} onBlur={() => setTouched(true)} />
        </FormField>
        <Alert variant="warning">
          <TriangleAlert />
          <AlertDescription>{t("Orders:RejectIntakeConsequence")}</AlertDescription>
        </Alert>
      </div>
    </EntityDialog>
  );
}
