import { useCallback, useState, type ReactNode } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

import { orderLegRoute, planOrderDrop, previewOfOrderLegs, type OrderDropPlan } from "@/apps/planboard/order-drop-model";
import { useOutsideWindowConfirm } from "@/apps/planboard/outside-window";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { fetchLegs } from "@/domains/planning-execution/legs";
import {
  fetchAssignOrderPreview,
  PLANBOARD_STUCK_REASON,
  retryOnStaleStamp,
  useAssignOrderToTrip,
  type PlanboardAssignOrderResultDto,
  type PlanboardOrderLegDto,
  type PlanboardPulledFromDto,
} from "@/domains/planning-execution/planboard-lists";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* An order dropped on a trip (design 74 §8), the one flow of every drop target: the preview; nothing movable: a warning that
 * says why and no call; legs that would leave other trips: one confirmation listing them; then the assign, through the
 * outside-window flow and with a stale stamp retried once; then a toast with the moved count and, when legs stayed, a warning
 * naming them. The caller refreshes what it shows: the result names the target and the trips the legs left. */

const ORDER_LEGS_REQUEST = { startRow: 0, endRow: 200, sortModels: [], columnFilters: {}, wildcardSearch: null, rowGroupCols: [], groupKeys: [] };

interface PendingConfirm {
  tripNumber: string | null;
  pulledFrom: PlanboardPulledFromDto[];
  answer: (go: boolean) => void;
}

export interface OrderDropOnTrip {
  orderId: string;
  tripId: string;
  tripNumber?: string | null;
  /** The stamp the caller has; a stale one is replaced once. */
  stamp?: string | null;
}

export function useOrderDrop(): {
  /** The whole flow on an existing trip; null when nothing happened (the reason was shown). */
  dropOnTrip: (target: OrderDropOnTrip) => Promise<PlanboardAssignOrderResultDto | null>;
  /** The first half on an empty cell: true when the caller may create the trip with this order. */
  prepareNewTrip: (orderId: string) => Promise<boolean>;
  /** The toasts of a finished drop. */
  report: (result: PlanboardAssignOrderResultDto, tripNumber?: string | null) => void;
  dialog: ReactNode;
} {
  const { t } = useL();
  const queryClient = useQueryClient();
  const { mutateAsync: assign } = useAssignOrderToTrip();
  const { run: runOutsideWindow, dialog: outsideWindowDialog } = useOutsideWindowConfirm();
  const [pending, setPending] = useState<PendingConfirm | null>(null);

  const stuckText = useCallback(
    (legs: readonly PlanboardOrderLegDto[]) =>
      legs
        .map((leg) =>
          leg.stuckReason === PLANBOARD_STUCK_REASON.HasActuals
            ? t("Planboard:OrderDrop.StuckHasActuals").replace("{0}", orderLegRoute(leg))
            : t("Planboard:OrderDrop.StuckTripFinal").replace("{0}", orderLegRoute(leg)).replace("{1}", leg.tripNumber ?? ""),
        )
        .join("; "),
    [t],
  );

  /** Whether the flow goes on after the plan: says why when not, asks when it must. */
  const passes = useCallback(
    (plan: OrderDropPlan, tripNumber: string | null): Promise<boolean> => {
      if (plan.kind === "blocked") {
        toast.warning(plan.stuck.length > 0 ? t(plan.reasonKey).replace("{0}", stuckText(plan.stuck)) : t(plan.reasonKey));
        return Promise.resolve(false);
      }
      if (plan.kind === "go") return Promise.resolve(true);
      return new Promise<boolean>((resolve) => setPending({ tripNumber, pulledFrom: plan.pulledFrom, answer: resolve }));
    },
    [t, stuckText],
  );

  const report = useCallback(
    (result: PlanboardAssignOrderResultDto, tripNumber?: string | null) => {
      toast.success(t("Planboard:OrderDrop.LegsMoved").replace("{0}", String(result.moved?.length ?? 0)).replace("{1}", tripNumber ?? ""));
      const stuck = result.stuck ?? [];
      if (stuck.length > 0) toast.warning(t("Planboard:OrderDrop.LegsStuck").replace("{0}", stuckText(stuck)));
    },
    [t, stuckText],
  );

  const dropOnTrip = useCallback(
    async ({ orderId, tripId, tripNumber, stamp }: OrderDropOnTrip): Promise<PlanboardAssignOrderResultDto | null> => {
      try {
        const preview = await fetchAssignOrderPreview(tripId, orderId);
        if (!(await passes(planOrderDrop(preview), tripNumber ?? null))) return null;
        const outcome = await runOutsideWindow((allowOutsideWindow) =>
          retryOnStaleStamp(queryClient, tripId, stamp, (concurrencyStamp) => assign({ tripId, orderId, allowOutsideWindow, concurrencyStamp })),
        );
        if (outcome.cancelled) return null;
        report(outcome.value, tripNumber);
        return outcome.value;
      } catch (error) {
        toast.error(getApiErrorMessage(error, t));
        return null;
      }
    },
    [passes, runOutsideWindow, queryClient, assign, report, t],
  );

  const prepareNewTrip = useCallback(
    async (orderId: string): Promise<boolean> => {
      try {
        const { rows } = await fetchLegs(ORDER_LEGS_REQUEST, { orderId });
        return await passes(planOrderDrop(previewOfOrderLegs(rows)), null);
      } catch (error) {
        toast.error(getApiErrorMessage(error, t));
        return false;
      }
    },
    [passes, t],
  );

  const tripsText = pending?.pulledFrom.map((trip) => t("Planboard:OrderDrop.PulledTrip").replace("{0}", trip.number ?? "").replace("{1}", String(trip.legCount ?? 0))).join(", ") ?? "";

  const dialog = (
    <>
      {outsideWindowDialog}
      {pending && (
        <ConfirmDialog
          open
          onOpenChange={(open) => {
            if (!open) {
              pending.answer(false);
              setPending(null);
            }
          }}
          title={pending.tripNumber ? t("Planboard:OrderDrop.ConfirmTitle").replace("{0}", pending.tripNumber) : t("Planboard:OrderDrop.ConfirmTitleNewTrip")}
          description={t("Planboard:OrderDrop.ConfirmQuestion").replace("{0}", tripsText)}
          confirmLabel={t("Planboard:OrderDrop.ConfirmLabel")}
          onConfirm={() => {
            pending.answer(true);
            setPending(null);
          }}
        />
      )}
    </>
  );

  return { dropOnTrip, prepareNewTrip, report, dialog };
}
