import { useEffect, useState } from "react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { FormSection } from "@/components/ui/form-section";
import { Wizard, type WizardTask } from "@/components/ui/wizard";
import { archiveTrip, transportTripKeys, type TransportTripRow } from "@/domains/planning-execution/trips";
import { getApiErrorMessage } from "@/lib/api/errors";
import { countText } from "@/lib/i18n/count-text";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { useQueryClient } from "@tanstack/react-query";

/* The Archive trips wizard (design 61; UI-WIZ-001 to -005): Selection (the trips selected on the list, with the ones
 * that cannot be archived named) → Archive with per-trip progress (design 59: Completed or Cancelled only). */

export interface ArchiveTripsWizardProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  trips: TransportTripRow[];
}

const COMPLETED = 3;
const CANCELLED = 4;

export function ArchiveTripsWizard({ open, onOpenChange, trips }: ArchiveTripsWizardProps) {
  const { t } = useL();
  const queryClient = useQueryClient();
  const [tasks, setTasks] = useState<WizardTask[] | undefined>(undefined);
  useEffect(() => {
    if (open) setTasks(undefined);
  }, [open]);

  const archivable = trips.filter((trip) => !trip.isArchived && (trip.status === COMPLETED || trip.status === CANCELLED));
  const skipped = trips.filter((trip) => !archivable.includes(trip));

  const run = async () => {
    setTasks(archivable.map((trip) => ({ id: trip.id!, label: `${t("PlanningExecution:TransportTrip")} ${trip.number ?? ""}`, status: "waiting" })));
    let done = 0;
    for (const trip of archivable) {
      setTasks((prev) => prev?.map((task) => (task.id === trip.id ? { ...task, status: "running" } : task)));
      try {
        await archiveTrip(trip.id!);
        done++;
        setTasks((prev) => prev?.map((task) => (task.id === trip.id ? { ...task, status: "done" } : task)));
      } catch (err) {
        setTasks((prev) => prev?.map((task) => (task.id === trip.id ? { ...task, status: "failed", reason: getApiErrorMessage(err, t) } : task)));
      }
    }
    void queryClient.invalidateQueries({ queryKey: transportTripKeys.all });
    toast.success(countText(t, "PlanningExecution:TripsArchived", done));
  };

  return (
    <Wizard
      open={open}
      onOpenChange={onOpenChange}
      title={t("PlanningExecution:ArchiveTrips")}
      finishLabel={t("PlanningExecution:Archive")}
      busyLabel={t("PlanningExecution:Archiving")}
      onFinish={run}
      tasks={tasks}
      steps={[
        {
          id: "selection",
          title: t("PlanningExecution:Selection"),
          isValid: archivable.length > 0,
          content: (
            <div className="flex flex-col gap-4">
              <p className="text-sm text-muted-foreground">{countText(t, "PlanningExecution:ArchiveTripsIntro", archivable.length)}</p>
              <FormSection title={t("PlanningExecution:TransportTrips")}>
                <ul className="grid grid-cols-2 gap-1 text-sm" data-slot="archive-selection">
                  {archivable.map((trip) => (
                    <li key={trip.id}>
                      {trip.number} {trip.name ? `· ${trip.name}` : ""}
                    </li>
                  ))}
                </ul>
              </FormSection>
              {skipped.length > 0 && (
                <Alert variant="warning">
                  <AlertDescription>{countText(t, "PlanningExecution:ArchiveTripsSkipped", skipped.length, skipped.map((trip) => trip.number).join(", "))}</AlertDescription>
                </Alert>
              )}
            </div>
          ),
        },
      ]}
    />
  );
}
