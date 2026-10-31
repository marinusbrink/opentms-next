import { useCallback, useState, type ReactNode } from "react";

import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { getApiErrorCode, getApiErrorMessage } from "@/lib/api/errors";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* Legs dropped on a trip whose window they fall outside (design 68: the server refuses with `LegOutsideTripWindow`
 * unless `allowOutsideWindow` is set; D10: warn, never block). Every pane that assigns or moves legs runs the command
 * through `run`: the refusal opens a ConfirmDialog with the server's text (it names the window), and confirming retries
 * the same command with `allowOutsideWindow: true`. Any other failure is the caller's to show. */

/** The error code the server refuses a leg outside the trip's window with. */
export const LEG_OUTSIDE_TRIP_WINDOW = "OpenTms.PlanningExecution:LegOutsideTripWindow";

export function isLegOutsideTripWindow(error: unknown): boolean {
  return getApiErrorCode(error) === LEG_OUTSIDE_TRIP_WINDOW;
}

/** The outcome of `run`: the command's result, or `cancelled` when the user kept the legs out of the window. */
export type OutsideWindowResult<T> = { cancelled: false; value: T } | { cancelled: true };

/** Whether a command's outcome is a `run` the user cancelled (no success toast, no refresh needed). */
export function isCancelledOutcome(value: unknown): boolean {
  return value != null && typeof value === "object" && (value as { cancelled?: unknown }).cancelled === true;
}

interface Pending {
  message: string;
  retry: () => Promise<void>;
  cancel: () => void;
}

export function useOutsideWindowConfirm(): {
  run: <T>(attempt: (allowOutsideWindow: boolean) => Promise<T>) => Promise<OutsideWindowResult<T>>;
  dialog: ReactNode;
} {
  const { t } = useL();
  const [pending, setPending] = useState<Pending | null>(null);

  const run = useCallback(
    async <T,>(attempt: (allowOutsideWindow: boolean) => Promise<T>): Promise<OutsideWindowResult<T>> => {
      try {
        return { cancelled: false, value: await attempt(false) };
      } catch (error) {
        if (!isLegOutsideTripWindow(error)) throw error;
        return new Promise<OutsideWindowResult<T>>((resolve, reject) => {
          setPending({
            message: getApiErrorMessage(error, t),
            /* A failing retry closes the dialog and rejects: the caller shows the failure like any other. */
            retry: async () => {
              try {
                const value = await attempt(true);
                setPending(null);
                resolve({ cancelled: false, value });
              } catch (retryError) {
                setPending(null);
                reject(retryError);
              }
            },
            cancel: () => {
              setPending(null);
              resolve({ cancelled: true });
            },
          });
        });
      }
    },
    [t],
  );

  const dialog = pending ? (
    <ConfirmDialog
      open
      onOpenChange={(open) => {
        if (!open) pending.cancel();
      }}
      title={t("Planboard:OutsideWindow.Title")}
      description={pending.message}
      confirmLabel={t("Planboard:OutsideWindow.Confirm")}
      onConfirm={() => pending.retry()}
    />
  ) : null;

  return { run, dialog };
}
