import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { Archive, ArchiveRestore, CircleCheck, CirclePlay, CircleX, Printer, Save } from "lucide-react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import type { EntityDialogTab, RibbonCommand } from "@/components/ui/entity-dialog";
import { RecordPage } from "@/components/ui/record-page";
import { recordIdentificationTab } from "@/components/ui/record-identification-tab";
import { StatusBadge } from "@/components/ui/status-badge";
import { DocumentsTab } from "@/apps/transport/orders/DocumentsTab";
import { TripCostRevenueTab } from "@/apps/transport/trips/TripCostRevenueTab";
import { TripGeneralTab } from "@/apps/transport/trips/TripGeneralTab";
import { TripLegsTab } from "@/apps/transport/trips/TripLegsTab";
import { TripStopsTab } from "@/apps/transport/trips/TripStopsTab";
import { tripValuesOf, validateTrip, type TripValues } from "@/apps/transport/trips/trip-form";
import { PrintCmrWizard } from "@/apps/transport/wizards/PrintCmrWizard";
import {
  tripStatusName,
  useArchiveTrip,
  useCancelTrip,
  useCompleteTrip,
  useStartTrip,
  useTransportTripUpdates,
  useUnarchiveTrip,
  useUpdateTransportTrip,
  type TransportTripDto,
} from "@/domains/planning-execution/trips";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { parseInteger, useFormState } from "@/lib/forms/use-form-state";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { useCloseRecord } from "@/lib/use-open-record";
import { joinDateTime } from "@/apps/transport/orders/order-form";

/* The trip editor (design 61 "Trips list and editor"): a RecordPage with the ribbon Save, Start, Complete, Cancel,
 * Archive/Unarchive, Print CMR and the tabs General, Legs, Stops, Cost and revenue, Documents, Record identification.
 * The header saves through PUT; carrier, vehicles and drivers have their own commands (design 59) and are edited in
 * the General tab only while the trip is Planned and was created from an order (Tevens trip), otherwise read-only with
 * a Planboard link (design 61). Derived values arrive by push (flow 2). */

const PLANNED = 1;
const IN_PROGRESS = 2;

export interface TripEditorProps {
  trip: TransportTripDto;
  readOnly: boolean;
}

export function TripEditor({ trip, readOnly }: TripEditorProps) {
  const { t } = useL();
  const closeRecord = useCloseRecord();
  const formRef = useRef<HTMLDivElement>(null);
  const [activeTab, setActiveTab] = useState("general");
  const [serverError, setServerError] = useState<string | null>(null);
  const [dialog, setDialog] = useState<"print" | "cancel" | "archive" | null>(null);

  const status = trip.status ?? PLANNED;
  const editable = !trip.isArchived && (status === PLANNED || status === IN_PROGRESS);
  const formReadOnly = readOnly || !editable;

  const initial = useMemo(() => tripValuesOf(trip), [trip]);
  const { form, isDirty, submit, reset } = useFormState<TripValues>(initial, validateTrip, formReadOnly);
  const loadedKey = `${trip.id}|${trip.concurrencyStamp ?? ""}`;
  const lastLoadedKey = useRef<string | null>(null);
  useEffect(() => {
    if (lastLoadedKey.current !== loadedKey) {
      const first = lastLoadedKey.current == null;
      lastLoadedKey.current = loadedKey;
      if (first || !isDirty) reset(initial);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [loadedKey, initial]);

  useTransportTripUpdates([trip.id]);

  const can = {
    start: useIsGranted("PlanningExecution.TransportTrips.Start"),
    complete: useIsGranted("PlanningExecution.TransportTrips.Complete"),
    cancel: useIsGranted("PlanningExecution.TransportTrips.Cancel"),
    archive: useIsGranted("PlanningExecution.TransportTrips.Archive"),
    assign: useIsGranted("PlanningExecution.TransportTrips.AssignLegs"),
    stops: useIsGranted("PlanningExecution.TransportTrips.ManageStops"),
    legs: useIsGranted("PlanningExecution.Legs.Update"),
    print: useIsGranted("Orders.TransportOrders.Print"),
    rateLines: useIsGranted("Financial.RateLines.Update"),
  };

  const update = useUpdateTransportTrip();
  const start = useStartTrip();
  const complete = useCompleteTrip();
  const cancel = useCancelTrip();
  const archive = useArchiveTrip();
  const unarchive = useUnarchiveTrip();
  const isBusy = update.isPending || start.isPending || complete.isPending || cancel.isPending || archive.isPending || unarchive.isPending;

  const save = useCallback(async (): Promise<boolean> => {
    if (formReadOnly) return true;
    setServerError(null);
    const values = submit(formRef.current);
    if (!values) {
      setActiveTab("general");
      return false;
    }
    try {
      const saved = await update.mutateAsync({
        id: trip.id!,
        dto: {
          concurrencyStamp: trip.concurrencyStamp ?? "",
          tripDate: values.tripDate ?? "",
          name: values.name.trim() || null,
          execution: values.execution,
          stopCompositionMode: values.stopCompositionMode,
          firstLastStopRule: values.firstLastStopRule,
          departureRelationId: values.departureRelation?.id ?? null,
          departureAddressId: values.departureAddressId,
          arrivalRelationId: values.arrivalRelation?.id ?? null,
          arrivalAddressId: values.arrivalAddressId,
          plannedDeparture: joinDateTime(values.plannedDepartureDate, values.plannedDepartureTime) ?? trip.plannedDeparture ?? "",
          plannedArrival: joinDateTime(values.plannedArrivalDate, values.plannedArrivalTime),
          actualDistanceM: values.actualDistanceKm.trim() ? Math.round((parseInteger(values.actualDistanceKm.replace(",", ".").split(".")[0]) ?? 0) * 1000) : null,
          organizationUnitId: values.organizationUnitId,
          remarks: values.remarks.trim() || null,
          driverInstructions: values.driverInstructions.trim() || null,
          attributes: values.attributes,
        },
      });
      lastLoadedKey.current = `${saved.id}|${saved.concurrencyStamp ?? ""}`;
      reset(tripValuesOf(saved));
      toast.success(t("PlanningExecution:TransportTripSaved"));
      return true;
    } catch (err) {
      setServerError(getApiErrorMessage(err, t));
      return false;
    }
  }, [formReadOnly, submit, update, trip, reset, t]);

  const saveIfDirty = useCallback(async () => (isDirty ? save() : true), [isDirty, save]);
  const runCommand = async (run: () => Promise<unknown>, successKey: string) => {
    setServerError(null);
    if (!(await saveIfDirty())) return;
    try {
      await run();
      toast.success(t(successKey));
    } catch (err) {
      setServerError(getApiErrorMessage(err, t));
    }
  };

  const commands: RibbonCommand[] = [];
  if (!formReadOnly) commands.push({ id: "save", labelKey: update.isPending ? "Saving" : "Save", icon: Save, isPrimary: true, onClick: () => void save() });
  if (!readOnly && !trip.isArchived) {
    if (status === PLANNED && can.start) commands.push({ id: "start", labelKey: "PlanningExecution:Start", icon: CirclePlay, groupKey: "PlanningExecution:Status", onClick: () => void runCommand(() => start.mutateAsync({ id: trip.id! }), "PlanningExecution:TransportTripStarted") });
    if ((status === PLANNED || status === IN_PROGRESS) && can.complete) commands.push({ id: "complete", labelKey: "PlanningExecution:Complete", icon: CircleCheck, groupKey: "PlanningExecution:Status", onClick: () => void runCommand(() => complete.mutateAsync({ id: trip.id! }), "PlanningExecution:TransportTripCompleted") });
    if ((status === PLANNED || status === IN_PROGRESS) && can.cancel) commands.push({ id: "cancel", labelKey: "PlanningExecution:CancelTrip", icon: CircleX, variant: "destructive", groupKey: "PlanningExecution:Status", onClick: () => setDialog("cancel") });
  }
  if (!readOnly && can.archive && (status === 3 || status === 4)) {
    if (trip.isArchived) commands.push({ id: "unarchive", labelKey: "PlanningExecution:Unarchive", icon: ArchiveRestore, groupKey: "PlanningExecution:Status", onClick: () => void runCommand(() => unarchive.mutateAsync(trip.id!), "PlanningExecution:TransportTripUnarchived") });
    else commands.push({ id: "archive", labelKey: "PlanningExecution:Archive", icon: Archive, groupKey: "PlanningExecution:Status", onClick: () => setDialog("archive") });
  }
  if (can.print) commands.push({ id: "print", labelKey: "Orders:PrintConsignmentNote", icon: Printer, groupKey: "Orders:Documents", disabled: (trip.stops ?? []).every((s) => (s.legs ?? []).length === 0), disabledReasonKey: "PlanningExecution:NoLegs", onClick: () => void saveIfDirty().then((ok) => ok && setDialog("print")) });

  const withAlert = (content: ReactNode, tab: string) => (
    <div className="flex h-full min-h-0 flex-col gap-4">
      {serverError && activeTab === tab && (
        <Alert variant="destructive">
          <AlertDescription>{serverError}</AlertDescription>
        </Alert>
      )}
      {content}
    </div>
  );

  const tabs: EntityDialogTab[] = [
    { value: "general", label: t("General"), hasError: form.error("tripDate") != null || form.error("plannedDepartureDate") != null, content: withAlert(<TripGeneralTab form={form} trip={trip} readOnly={readOnly} />, "general") },
    { value: "legs", label: t("PlanningExecution:Legs"), content: withAlert(<TripLegsTab trip={trip} canAssign={can.assign && editable && !readOnly} canEditLegs={can.legs && !readOnly} />, "legs") },
    { value: "stops", label: t("PlanningExecution:Stops"), content: withAlert(<TripStopsTab trip={trip} canManage={can.stops && editable && !readOnly} />, "stops") },
    { value: "costs", label: t("PlanningExecution:CostAndRevenue"), content: withAlert(<TripCostRevenueTab trip={trip} canEdit={can.rateLines && editable && !readOnly} />, "costs") },
    { value: "documents", label: t("Orders:Documents"), content: withAlert(<DocumentsTab tripId={trip.id} onPrint={can.print ? () => void saveIfDirty().then((ok) => ok && setDialog("print")) : undefined} />, "documents") },
    recordIdentificationTab(t, { id: trip.id, creationTime: trip.creationTime, lastModificationTime: trip.lastModificationTime }),
  ];

  return (
    <div ref={formRef} className="h-full" data-slot="trip-page" data-record-id={trip.id} data-status={tripStatusName(trip.status)}>
      <RecordPage
        title={`${t("PlanningExecution:TransportTrip")} ${trip.number ?? ""}`}
        badges={
          <>
            <StatusBadge entity="TransportTrip" state={tripStatusName(trip.status)} />
            {trip.isArchived && <StatusBadge entity="TransportTrip" state="Archived" />}
          </>
        }
        commands={commands}
        tabs={tabs}
        activeTab={activeTab}
        onActiveTabChange={setActiveTab}
        isDirty={isDirty}
        isBusy={isBusy}
        onClose={() => closeRecord("TransportTrip")}
      />
      <PrintCmrWizard open={dialog === "print"} onOpenChange={(next) => setDialog(next ? "print" : null)} trip={{ id: trip.id!, number: trip.number ?? "" }} />
      <ConfirmDialog
        open={dialog === "cancel"}
        onOpenChange={(next) => setDialog(next ? "cancel" : null)}
        title={t("PlanningExecution:CancelTrip")}
        description={`${t("PlanningExecution:CancelTripQuestion").replace("{0}", trip.number ?? "")} ${t("ThisCannotBeUndone")}`}
        confirmLabel={t("PlanningExecution:CancelTrip")}
        destructive
        onConfirm={async () => {
          setDialog(null);
          await runCommand(() => cancel.mutateAsync(trip.id!), "PlanningExecution:TransportTripCancelled");
        }}
      />
      <ConfirmDialog
        open={dialog === "archive"}
        onOpenChange={(next) => setDialog(next ? "archive" : null)}
        title={t("PlanningExecution:ArchiveTrips")}
        description={t("PlanningExecution:ArchiveTripQuestion").replace("{0}", trip.number ?? "")}
        confirmLabel={t("PlanningExecution:Archive")}
        onConfirm={async () => {
          setDialog(null);
          await runCommand(() => archive.mutateAsync(trip.id!), "PlanningExecution:TransportTripArchived");
        }}
      />
    </div>
  );
}
