import { useEffect, useState } from "react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { EntityDialog } from "@/components/ui/entity-dialog";
import { Skeleton } from "@/components/ui/skeleton";
import { StatusBadge } from "@/components/ui/status-badge";
import { orderStatusName, useApproveMany, useValidateApproval, type ApprovalValidationDto } from "@/domains/orders/transport-orders";
import { getApiErrorMessage } from "@/lib/api/errors";
import { countText } from "@/lib/i18n/count-text";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* Approve (design 58, Q6): the dialog runs the approval check for the selected orders, shows every rule result with
 * its severity and the proposed status per order (ReadyForInvoicing, or Archived when there is nothing to invoice),
 * and approves the approvable ones with the hash the check returned; a stale hash is refused by the server
 * (`Orders:ApprovalResultsStale`). Action dialog (UI-DLG-002/013): the verb is Approve. */

const APPROVAL_RULES: Record<number, string> = {
  1: "OrderMustBeOpen",
  2: "ReferencesMustExist",
  3: "NextInvoicePrice",
  4: "FreightDocumentsAvailable",
  5: "PriceListApplied",
  6: "PriceCalculationCorrect",
  7: "DuplicateCustomerReference",
  8: "EmptyLinePrice",
  9: "FinancialDefaultsMismatch",
  10: "FixedPriceFilled",
  11: "LegDistanceMatchesLine",
  12: "AllStopsArrived",
};

export interface ApproveDialogProps {
  ids: string[];
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onApproved?: (approvedIds: string[]) => void;
}

export function ApproveDialog({ ids, open, onOpenChange, onApproved }: ApproveDialogProps) {
  const { t } = useL();
  const validate = useValidateApproval();
  const approve = useApproveMany();
  const [validation, setValidation] = useState<ApprovalValidationDto | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open || ids.length === 0) return;
    setValidation(null);
    setError(null);
    validate
      .mutateAsync(ids)
      .then(setValidation)
      .catch((err: unknown) => setError(getApiErrorMessage(err, t)));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, ids.join(",")]);

  const approvable = (validation?.orders ?? []).filter((o) => o.canApprove).map((o) => o.orderId!);

  const run = async () => {
    if (!validation?.hash) return;
    setError(null);
    try {
      const outcome = await approve.mutateAsync({ ids: approvable, hash: validation.hash });
      const approved = outcome.approvedIds ?? [];
      toast.success(countText(t, "Orders:OrdersApproved", approved.length));
      onApproved?.(approved);
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
      size="md"
      title={t("Orders:ApproveTransportOrders")}
      isBusy={approve.isPending}
      footer={
        <>
          <Button type="button" variant="outline" onClick={() => onOpenChange(false)} disabled={approve.isPending}>
            {t("Cancel")}
          </Button>
          <Button type="button" onClick={() => void run()} disabled={approve.isPending || approvable.length === 0}>
            {approve.isPending ? t("Orders:Approving") : `${t("Orders:Approve")} (${approvable.length})`}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3" data-slot="approval-results">
        {error && (
          <Alert variant="destructive">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}
        {!validation && !error && (
          <div className="grid gap-2">
            <Skeleton className="h-8 w-full" />
            <Skeleton className="h-8 w-2/3" />
          </div>
        )}
        {(validation?.orders ?? []).map((order) => (
          <div key={order.orderId} className="rounded-lg border bg-background p-3" data-can-approve={order.canApprove ? "true" : "false"}>
            <div className="flex flex-wrap items-center gap-2 text-sm">
              <span className="font-medium">{order.number ?? t("Orders:Draft")}</span>
              <StatusBadge entity="TransportOrder" state={orderStatusName(order.currentStatus)} />
              <span className="text-label text-muted-foreground">→</span>
              <span className="text-label text-muted-foreground">{t("Orders:ProposedStatus")}:</span>
              <StatusBadge entity="TransportOrder" state={orderStatusName(order.proposedStatus)} />
              {!order.canApprove && <Badge variant="destructive">{t("Orders:CannotApprove")}</Badge>}
            </div>
            {(order.results ?? []).length > 0 && (
              <ul className="mt-2 flex flex-col gap-1 text-label">
                {(order.results ?? []).map((result, index) => (
                  <li key={`${result.rule}-${index}`} className="flex items-start gap-2">
                    <Badge variant={result.severity === 2 ? "destructive" : "secondary"}>{t(`Orders:ApprovalSeverity.${result.severity === 2 ? "Error" : "Warning"}`)}</Badge>
                    <span className="font-medium">{t(`Orders:ApprovalRule.${APPROVAL_RULES[result.rule ?? 0] ?? ""}`)}</span>
                    <span className="text-muted-foreground">{result.message ?? (result.messageKey ? t(result.messageKey) : "")}</span>
                  </li>
                ))}
              </ul>
            )}
          </div>
        ))}
      </div>
    </EntityDialog>
  );
}
